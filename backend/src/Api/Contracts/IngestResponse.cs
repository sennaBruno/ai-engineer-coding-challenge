namespace Api.Contracts;

public sealed class IngestResponse
{
    public bool Accepted { get; init; }

    public string Message { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public int ChunksCreated { get; init; }

    public int RecordsPersisted { get; init; }

    public string VectorStorePath { get; init; } = string.Empty;
}
