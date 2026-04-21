using Api.Contracts;
using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("ingest")]
public sealed class IngestController(
    IConfiguration configuration,
    IChunkingService chunkingService,
    IEmbeddingService embeddingService,
    IVectorStoreService vectorStore,
    IWebHostEnvironment environment,
    ILogger<IngestController> logger) : ControllerBase
{
    // Hard cap so a huge file (legitimate mistake or malicious pointer) can't blow up
    // memory or trigger an unbounded embedding bill. 10 MB fits anything reasonable
    // for an SOP document.
    private const long MaxSourceFileBytes = 10 * 1024 * 1024;

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

        var knowledgeBaseRoot = FindKnowledgeBaseRoot(environment);
        var resolvedSource = ResolveSopPath(sourcePath, environment, knowledgeBaseRoot);
        if (resolvedSource is null)
        {
            return BadRequest(new
            {
                error = "SOP source document not found. The source path must resolve to " +
                        $"a file under the repository's knowledge-base/ directory. Attempted: '{sourcePath}'."
            });
        }

        var fileInfo = new FileInfo(resolvedSource);
        if (fileInfo.Length > MaxSourceFileBytes)
        {
            return BadRequest(new
            {
                error = $"Source file is {fileInfo.Length / 1024} KB which exceeds the " +
                        $"{MaxSourceFileBytes / 1024 / 1024} MB ingest cap."
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
                VectorStorePath = vectorStorePath
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
                VectorStorePath = vectorStorePath
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
            VectorStorePath = vectorStorePath
        });
    }

    /// <summary>
    /// Walks up from the content root until we find a `knowledge-base/` directory.
    /// Returns its absolute path (or null if we can't find one within 6 levels).
    /// This directory is the only place an ingest source is allowed to live — see
    /// <see cref="ResolveSopPath"/> for the containment check.
    /// </summary>
    private static string? FindKnowledgeBaseRoot(IWebHostEnvironment environment)
    {
        var current = new DirectoryInfo(environment.ContentRootPath);
        for (var i = 0; i < 6 && current is not null; i++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "knowledge-base");
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the supplied SOP path and verifies the result lives inside the
    /// repository's knowledge-base/ directory. This blocks path-traversal
    /// (../../etc/passwd, absolute /root/secret, symlinks that escape the root)
    /// which is important because the request body comes from an unauthenticated
    /// client in this POC.
    /// </summary>
    private static string? ResolveSopPath(
        string path,
        IWebHostEnvironment environment,
        string? knowledgeBaseRoot)
    {
        if (knowledgeBaseRoot is null)
        {
            return null;
        }

        var normalized = path.Replace('\\', Path.DirectorySeparatorChar);

        // Build candidate absolute paths from the relative input.
        var candidates = new List<string>();
        if (Path.IsPathRooted(normalized))
        {
            candidates.Add(normalized);
        }
        else
        {
            candidates.Add(Path.GetFullPath(Path.Combine(environment.ContentRootPath, normalized)));
            candidates.Add(Path.GetFullPath(Path.Combine(knowledgeBaseRoot, Path.GetFileName(normalized))));
        }

        foreach (var candidate in candidates)
        {
            if (!System.IO.File.Exists(candidate))
            {
                continue;
            }

            // Resolve any symlinks before the containment check so a symlink inside
            // knowledge-base pointing to /etc/passwd doesn't slip through.
            var resolved = Path.GetFullPath(new FileInfo(candidate).ResolveLinkTarget(true)?.FullName ?? candidate);
            var rootWithSep = knowledgeBaseRoot.EndsWith(Path.DirectorySeparatorChar)
                ? knowledgeBaseRoot
                : knowledgeBaseRoot + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(rootWithSep, StringComparison.Ordinal))
            {
                return resolved;
            }
        }
        return null;
    }
}
