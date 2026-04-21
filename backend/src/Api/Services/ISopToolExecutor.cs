using Api.Models;

namespace Api.Services;

public interface ISopToolExecutor
{
    Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default);
}

public sealed record ToolExecutionResult(
    string JsonPayload,
    IReadOnlyList<VectorSearchMatch> RetrievedChunks);
