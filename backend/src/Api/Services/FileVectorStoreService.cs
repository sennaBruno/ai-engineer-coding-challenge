using System.Text.Json;
using Api.Models;

namespace Api.Services;

/// <summary>
/// In-memory vector store persisted as a single JSON array on disk.
/// Records are loaded lazily on first access and kept in memory for the
/// lifetime of the process. Cosine similarity is used for ranking —
/// adequate for a small-scale local POC (n ≈ tens to low thousands of vectors).
/// </summary>
public sealed class FileVectorStoreService : IVectorStoreService
{
    private readonly string _artifactPath;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly ILogger<FileVectorStoreService> _logger;
    private List<VectorRecord>? _cache;

    public FileVectorStoreService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<FileVectorStoreService> logger)
    {
        _artifactPath = ResolveArtifactPath(configuration, environment);
        _logger = logger;
    }

    public async Task<IReadOnlyList<VectorRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _cache!;
    }

    public async Task SaveAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var list = records.ToList();
            var directory = Path.GetDirectoryName(_artifactPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temp file first, then atomically replace the target —
            // protects against partial writes if the process dies mid-save.
            var tempPath = _artifactPath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, list, SerializerOptions, cancellationToken);
            }
            File.Move(tempPath, _artifactPath, overwrite: true);

            _cache = list;
            _logger.LogInformation("Persisted {Count} vector records to {Path}", list.Count, _artifactPath);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<IReadOnlyList<VectorSearchMatch>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        var records = _cache!;
        if (records.Count == 0 || topK <= 0)
        {
            return [];
        }

        var queryNorm = Norm(queryEmbedding);
        if (queryNorm == 0)
        {
            return [];
        }

        return records
            .Select(record => new VectorSearchMatch
            {
                Record = record,
                Score = CosineSimilarity(queryEmbedding, queryNorm, record.Embedding)
            })
            .OrderByDescending(match => match.Score)
            .Take(topK)
            .ToList();
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return;
        }

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null)
            {
                return;
            }

            if (!File.Exists(_artifactPath))
            {
                _cache = [];
                return;
            }

            await using var stream = File.OpenRead(_artifactPath);
            var loaded = await JsonSerializer.DeserializeAsync<List<VectorRecord>>(
                stream,
                SerializerOptions,
                cancellationToken);
            _cache = loaded ?? [];
            _logger.LogInformation("Loaded {Count} vector records from {Path}", _cache.Count, _artifactPath);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static double CosineSimilarity(float[] query, double queryNorm, float[] candidate)
    {
        if (candidate.Length != query.Length || candidate.Length == 0)
        {
            return 0;
        }

        double dot = 0;
        double candidateNormSquared = 0;
        for (var i = 0; i < query.Length; i++)
        {
            dot += query[i] * candidate[i];
            candidateNormSquared += candidate[i] * candidate[i];
        }

        var candidateNorm = Math.Sqrt(candidateNormSquared);
        if (candidateNorm == 0)
        {
            return 0;
        }

        return dot / (queryNorm * candidateNorm);
    }

    private static double Norm(float[] vector)
    {
        double sum = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }
        return Math.Sqrt(sum);
    }

    private static string ResolveArtifactPath(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["Challenge:VectorStoreJsonPath"] ?? "Data/vector-store.json";
        var normalized = configuredPath.Replace('\\', Path.DirectorySeparatorChar);

        return Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(environment.ContentRootPath, normalized);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
