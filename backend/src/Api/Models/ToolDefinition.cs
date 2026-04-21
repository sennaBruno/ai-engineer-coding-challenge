namespace Api.Models;

public sealed class ToolDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string ParametersSchemaJson { get; init; } = "{\"type\":\"object\",\"properties\":{}}";
}
