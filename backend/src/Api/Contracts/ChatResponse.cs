namespace Api.Contracts;

public sealed class ChatResponse
{
    public string ConversationId { get; init; } = string.Empty;

    public string AssistantMessage { get; init; } = string.Empty;

    /// <summary>
    /// One of: "ok", "error", "tool-loop-exhausted". Mirror this in the TypeScript
    /// client as a discriminated union when adding new values.
    /// </summary>
    public string Status { get; init; } = "ok";

    public List<string> ToolCalls { get; init; } = [];

    public List<CitationDto> Citations { get; init; } = [];
}
