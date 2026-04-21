using Api.Contracts;
using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class IngestController(
    IConfiguration configuration,
    IChunkingService chunkingService,
    IEmbeddingService embeddingService,
    IVectorStoreService vectorStore,
    IWebHostEnvironment environment,
    ILogger<IngestController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<IngestResponse>> Post(
        [FromBody] IngestRequest? request,
        CancellationToken cancellationToken)
    {
        var configuredSourcePath = configuration["Challenge:SourceDocumentPath"]
            ?? "../../../knowledge-base/Grocery_Store_SOP.md";
        var vectorStorePath = configuration["Challenge:VectorStoreJsonPath"] ?? "Data/vector-store.json";

        var sourcePath = string.IsNullOrWhiteSpace(request?.SourcePath)
            ? configuredSourcePath
            : request!.SourcePath;

        var resolvedSource = ResolveSopPath(sourcePath, environment);
        if (resolvedSource is null)
        {
            return BadRequest(new
            {
                error = $"SOP source document not found. Tried resolving '{sourcePath}' against the backend content root and common parent directories. " +
                        "Pass an absolute path via the request body if your source lives elsewhere."
            });
        }

        var existing = await vectorStore.LoadAsync(cancellationToken);
        if (existing.Count > 0 && request?.ForceReingest != true)
        {
            logger.LogInformation("Ingest skipped — {Count} records already present", existing.Count);
            return Ok(new IngestResponse
            {
                Accepted = true,
                Message = $"Vector store already contains {existing.Count} records. Send forceReingest=true to rebuild.",
                SourcePath = sourcePath,
                ChunksCreated = existing.Count,
                RecordsPersisted = existing.Count,
                VectorStorePath = vectorStorePath,
                IsPlaceholder = false
            });
        }

        var rawText = await System.IO.File.ReadAllTextAsync(resolvedSource, cancellationToken);
        var sourceName = Path.GetFileName(resolvedSource);

        var chunks = await chunkingService.ChunkAsync(rawText, sourceName, cancellationToken);
        logger.LogInformation("Chunked {File} into {Count} sections", sourceName, chunks.Count);

        if (chunks.Count == 0)
        {
            return Ok(new IngestResponse
            {
                Accepted = false,
                Message = "Chunking produced 0 chunks — is the source document empty?",
                SourcePath = sourcePath,
                ChunksCreated = 0,
                RecordsPersisted = 0,
                VectorStorePath = vectorStorePath,
                IsPlaceholder = false
            });
        }

        var embeddings = await embeddingService.EmbedBatchAsync(
            chunks.Select(c => c.Content).ToList(),
            cancellationToken);

        var records = chunks.Zip(embeddings, (chunk, embedding) => new VectorRecord
        {
            Id = chunk.Id,
            Source = chunk.Source,
            ChunkText = chunk.Content,
            Embedding = embedding,
            Metadata = new Dictionary<string, string>
            {
                ["section"] = chunk.Section,
                ["index"] = chunk.Index.ToString(),
                ["startLine"] = chunk.StartLine.ToString(),
                ["endLine"] = chunk.EndLine.ToString()
            }
        }).ToList();

        await vectorStore.SaveAsync(records, cancellationToken);

        return Ok(new IngestResponse
        {
            Accepted = true,
            Message = $"Ingested {records.Count} chunks from {sourceName}.",
            SourcePath = sourcePath,
            ChunksCreated = chunks.Count,
            RecordsPersisted = records.Count,
            VectorStorePath = vectorStorePath,
            IsPlaceholder = false
        });
    }

    /// <summary>
    /// Resolves the SOP path against multiple plausible roots so the challenge works whether
    /// the API is launched from the project directory, a bin/ output folder, or via an IDE.
    /// </summary>
    private static string? ResolveSopPath(string path, IWebHostEnvironment environment)
    {
        var normalized = path.Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            return System.IO.File.Exists(normalized) ? normalized : null;
        }

        var candidates = new List<string>
        {
            Path.GetFullPath(Path.Combine(environment.ContentRootPath, normalized)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, normalized)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), normalized))
        };

        // Walk up from the content root until we find a sibling knowledge-base/Grocery_Store_SOP.md.
        var current = new DirectoryInfo(environment.ContentRootPath);
        var fileName = Path.GetFileName(normalized);
        for (var i = 0; i < 6 && current is not null; i++, current = current.Parent)
        {
            candidates.Add(Path.Combine(current.FullName, "knowledge-base", fileName));
        }

        return candidates.FirstOrDefault(System.IO.File.Exists);
    }
}
