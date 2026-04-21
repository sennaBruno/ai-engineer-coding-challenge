using OpenAI.Embeddings;

namespace Api.Services;

public sealed class OpenAIEmbeddingService(EmbeddingClient embeddingClient) : IEmbeddingService
{
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        return result.Value.ToFloats().ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var result = await embeddingClient.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);
        return result.Value.Select(e => e.ToFloats().ToArray()).ToList();
    }
}
