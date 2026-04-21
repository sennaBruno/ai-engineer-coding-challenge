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
    // Byte cap alone isn't enough: 10 MB of 1-char paragraphs = 100K chunks, each billed
    // as a separate embedding. Cap chunk count before calling the embedding API.
    private const int MaxChunks = 500;

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

        if (chunks.Count > MaxChunks)
        {
            return BadRequest(new
            {
                error = $"Chunking produced {chunks.Count} chunks, exceeding the {MaxChunks} cap. " +
                        "This prevents runaway embedding spend on pathological input."
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

        // Build candidate absolute paths from the input. For a relative input we
        // only consider it as a filename under knowledge-base/. Resolving
        // arbitrary relatives against ContentRootPath was defense-in-depth-safe
        // (the containment check still applied) but added an attack surface that
        // the containment check had to clean up. Dropping it keeps the first
        // resolved path inside the allowed root by construction.
        var candidates = new List<string>();
        if (Path.IsPathRooted(normalized))
        {
            candidates.Add(normalized);
        }
        else
        {
            candidates.Add(Path.GetFullPath(Path.Combine(knowledgeBaseRoot, Path.GetFileName(normalized))));
        }

        // Resolve the root with symlinks followed, so both sides of the containment
        // check are in the same realpath space.
        var resolvedRoot = ResolveLinksFully(knowledgeBaseRoot);
        if (resolvedRoot is null)
        {
            return null;
        }
        var rootWithSep = resolvedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? resolvedRoot
            : resolvedRoot + Path.DirectorySeparatorChar;

        foreach (var candidate in candidates)
        {
            if (!System.IO.File.Exists(candidate))
            {
                continue;
            }

            // Realpath the candidate AND every parent directory. Resolving only the
            // final file misses the case where a parent dir itself is a symlink
            // pointing outside the knowledge-base/ tree.
            var resolved = ResolveLinksFully(candidate);
            if (resolved is null)
            {
                continue;
            }
            if (resolved.StartsWith(rootWithSep, StringComparison.Ordinal))
            {
                return resolved;
            }
        }
        return null;
    }

    /// <summary>
    /// Walks every path segment and follows symlinks so the returned path is a
    /// realpath — no symlinks anywhere in the chain. Prevents parent-directory
    /// symlink bypasses (codex P2): a symlinked dir inside knowledge-base/
    /// pointing elsewhere used to pass the final-file-only resolve.
    /// </summary>
    private static string? ResolveLinksFully(string path)
    {
        try
        {
            var absolute = Path.GetFullPath(path);
            var fsInfo = System.IO.File.Exists(absolute)
                ? (FileSystemInfo)new FileInfo(absolute)
                : Directory.Exists(absolute) ? new DirectoryInfo(absolute) : null;
            if (fsInfo is null)
            {
                return null;
            }

            // LinkTarget + recursive resolution catches chained symlinks; if the
            // target is itself a FileInfo, fold back through the same helper. .NET's
            // ResolveLinkTarget(returnFinalTarget:true) follows the chain in one call.
            var target = fsInfo.ResolveLinkTarget(returnFinalTarget: true);
            var resolvedFile = Path.GetFullPath(target?.FullName ?? absolute);

            // Now walk every parent dir and resolve each segment in turn.
            var dir = Path.GetDirectoryName(resolvedFile);
            if (string.IsNullOrEmpty(dir))
            {
                return resolvedFile;
            }

            var resolvedDir = ResolveDirectoryLinks(dir);
            return resolvedDir is null
                ? null
                : Path.Combine(resolvedDir, Path.GetFileName(resolvedFile));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveDirectoryLinks(string dir)
    {
        try
        {
            var absolute = Path.GetFullPath(dir);
            var info = new DirectoryInfo(absolute);
            if (!info.Exists)
            {
                return null;
            }
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            var resolved = Path.GetFullPath(target?.FullName ?? absolute);

            var parent = Path.GetDirectoryName(resolved);
            if (string.IsNullOrEmpty(parent) || parent == resolved)
            {
                return resolved;
            }

            var resolvedParent = ResolveDirectoryLinks(parent);
            return resolvedParent is null
                ? null
                : Path.Combine(resolvedParent, Path.GetFileName(resolved));
        }
        catch
        {
            return null;
        }
    }
}
