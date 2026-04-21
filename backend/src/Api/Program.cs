using Api.Options;
using Api.Services;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

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
// appsettings (intended for Development only). An empty/placeholder key short-circuits
// into helpful startup guidance instead of mysteriously failing on the first request.
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

app.UseCors("LocalFrontend");
app.MapControllers();

app.Run();
