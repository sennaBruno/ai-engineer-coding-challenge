using System.Text.Json;
using Api.Options;
using Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
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

app.UseCors("LocalFrontend");
app.MapControllers();

app.Run();
