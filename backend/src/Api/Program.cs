using System.Text.Json;
using System.Threading.RateLimiting;
using Api.Options;
using Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy =>
    {
        // Tight CORS: bounded origin list + only the methods/headers the frontend
        // actually uses. No credentials, no cookies, so the risk surface is small,
        // but defense-in-depth is cheap here.
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "OPTIONS")
            .WithHeaders("Content-Type");
    });
});

// Rate limiter. Even a POC proxying to a paid API needs a cap so an attacker
// (or a misbehaving script) can't fire 1000 req/s and drain the OpenAI budget.
// Chat is stricter because every turn may call tools and multiple completions.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("chat", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("ingest", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// OpenAI wiring. The API key is read from OPENAI_API_KEY env var first, then from
// appsettings (intended for Development only). An empty/placeholder key fails fast
// at startup with a helpful message, not a mysterious 500 on the first request.
var openAiOptions = new OpenAIOptions();
builder.Configuration.GetSection(OpenAIOptions.SectionName).Bind(openAiOptions);

var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (!string.IsNullOrWhiteSpace(envKey))
{
    openAiOptions.ApiKey = envKey;
}

if (string.IsNullOrWhiteSpace(openAiOptions.ApiKey) ||
    openAiOptions.ApiKey.Equals("YOUR_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "OpenAI API key is not configured. Set the OPENAI_API_KEY environment variable " +
        "or update appsettings.Development.json → OpenAI:ApiKey.");
}

builder.Services.AddSingleton(openAiOptions);

var openAiClient = new OpenAIClient(openAiOptions.ApiKey);
builder.Services.AddSingleton(openAiClient.GetChatClient(openAiOptions.ChatModel));
builder.Services.AddSingleton(openAiClient.GetEmbeddingClient(openAiOptions.EmbeddingModel));

// Application services.
builder.Services.AddSingleton<IChunkingService, MarkdownChunkingService>();
builder.Services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();
builder.Services.AddSingleton<IVectorStoreService, FileVectorStoreService>();
builder.Services.AddSingleton<IToolRegistryService, SopToolRegistry>();
builder.Services.AddSingleton<ISopToolExecutor, SopToolExecutor>();
builder.Services.AddSingleton<IRetrievalChatService, OpenAIRetrievalChatService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Inline stack traces during local debugging. Production keeps the sanitised
    // { "error": "..." } handler below so raw exception text never reaches clients.
    app.UseDeveloperExceptionPage();
}
else
{
    // Global exception handler. Every unhandled exception becomes a uniform
    // { "error": "..." } JSON body so the TypeScript client's error parser sees
    // a consistent shape. Full details land in the logger for operators.
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var ex = feature?.Error;
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("UnhandledException");
            logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new
            {
                error = "An unexpected error occurred. Check the server logs for details."
            });
            await context.Response.WriteAsync(body);
        });
    });
}

// Minimal security headers. API returns JSON so XSS risk is low, but nosniff +
// frame deny + referrer policy cost nothing and block a few entire attack classes
// (MIME-sniffing shenanigans, clickjacking if someone embeds the JSON endpoints).
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseCors("LocalFrontend");
app.UseRateLimiter();
app.MapControllers();

app.Run();
