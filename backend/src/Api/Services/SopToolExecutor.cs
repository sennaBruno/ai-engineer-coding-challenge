using System.Text.Json;
using Api.Models;

namespace Api.Services;

public sealed class SopToolExecutor(
    IEmbeddingService embeddingService,
    IVectorStoreService vectorStore,
    ILogger<SopToolExecutor> logger) : ISopToolExecutor
{
    // Output payloads use snake_case to match the tool's declared JSON schema so the
    // model reads results in the same shape it reasons about when planning tool calls.
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    // Input args follow the model's JSON schema exactly (snake_case), so the deserializer
    // must translate snake_case → PascalCase C# property names.
    private static readonly JsonSerializerOptions ArgsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // Seed catalogue kept in code for a POC. In production this would live in
    // the product DB — the point of the tool is to show that not every lookup
    // should be a vector search.
    private static readonly Dictionary<string, ProductLocation> ProductCatalog = BuildCatalog();

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Executing tool {Tool} with args {Args}", toolName, argumentsJson);

        return toolName switch
        {
            "search_sop" => await ExecuteSearchAsync(argumentsJson, cancellationToken),
            "lookup_product_location" => ExecuteLookup(argumentsJson),
            _ => new ToolExecutionResult(
                JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" }, OutputJsonOptions),
                [])
        };
    }

    private async Task<ToolExecutionResult> ExecuteSearchAsync(string argumentsJson, CancellationToken ct)
    {
        SearchSopArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<SearchSopArgs>(argumentsJson, ArgsJsonOptions);
        }
        catch (JsonException ex)
        {
            return new ToolExecutionResult(
                JsonSerializer.Serialize(new { error = $"Invalid tool arguments: {ex.Message}" }, OutputJsonOptions),
                []);
        }

        if (args is null || string.IsNullOrWhiteSpace(args.Query))
        {
            return new ToolExecutionResult(
                JsonSerializer.Serialize(new { error = "query is required" }, OutputJsonOptions),
                []);
        }

        var topK = Math.Clamp(args.TopK ?? 4, 1, 8);
        var queryEmbedding = await embeddingService.EmbedAsync(args.Query, ct);
        var matches = await vectorStore.SearchAsync(queryEmbedding, topK, ct);

        if (matches.Count == 0)
        {
            return new ToolExecutionResult(
                JsonSerializer.Serialize(new
                {
                    query = args.Query,
                    results = Array.Empty<object>(),
                    note = "No SOP content found. The document may not be ingested yet."
                }, OutputJsonOptions),
                []);
        }

        var payload = new
        {
            query = args.Query,
            results = matches.Select(match => new
            {
                chunk_id = match.Record.Id,
                section = match.Record.Metadata.GetValueOrDefault("section", match.Record.Source),
                source = match.Record.Source,
                start_line = match.Record.Metadata.GetValueOrDefault("startLine"),
                end_line = match.Record.Metadata.GetValueOrDefault("endLine"),
                score = Math.Round(match.Score, 4),
                content = match.Record.ChunkText
            })
        };

        return new ToolExecutionResult(JsonSerializer.Serialize(payload, OutputJsonOptions), matches);
    }

    private static ToolExecutionResult ExecuteLookup(string argumentsJson)
    {
        LookupArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<LookupArgs>(argumentsJson, ArgsJsonOptions);
        }
        catch (JsonException ex)
        {
            return new ToolExecutionResult(
                JsonSerializer.Serialize(new { error = $"Invalid tool arguments: {ex.Message}" }, OutputJsonOptions),
                []);
        }

        if (args is null || string.IsNullOrWhiteSpace(args.ItemName))
        {
            return new ToolExecutionResult(
                JsonSerializer.Serialize(new { error = "item_name is required" }, OutputJsonOptions),
                []);
        }

        var normalized = args.ItemName.Trim().ToLowerInvariant();

        // Direct hit first, then fall back to substring match.
        if (!ProductCatalog.TryGetValue(normalized, out var location))
        {
            location = ProductCatalog
                .FirstOrDefault(entry => normalized.Contains(entry.Key) || entry.Key.Contains(normalized))
                .Value;
        }

        if (location is null)
        {
            return new ToolExecutionResult(
                JsonSerializer.Serialize(new
                {
                    item = args.ItemName,
                    found = false,
                    note = "Item not found in the aisle map. Recommend checking the SOP department directory or asking a manager."
                }, OutputJsonOptions),
                []);
        }

        return new ToolExecutionResult(
            JsonSerializer.Serialize(new
            {
                item = args.ItemName,
                found = true,
                department = location.Department,
                aisle = location.Aisle,
                notes = location.Notes
            }, OutputJsonOptions),
            []);
    }

    private static Dictionary<string, ProductLocation> BuildCatalog()
    {
        // Keys are lowercase; lookup uses substring fall-back so "whole milk" → "milk".
        return new Dictionary<string, ProductLocation>(StringComparer.OrdinalIgnoreCase)
        {
            ["milk"] = new("Dairy", "Aisle 12 (refrigerated wall)", "Check expiration dates on rotation — FIFO."),
            ["yogurt"] = new("Dairy", "Aisle 12", null),
            ["cheese"] = new("Dairy / Deli", "Aisle 12 pre-packaged; fresh at Deli counter", null),
            ["eggs"] = new("Dairy", "Aisle 12 end-cap", null),
            ["bread"] = new("Bakery", "Aisle 3 and in-store Bakery counter", "Day-old bread marked down per SOP §13."),
            ["apples"] = new("Produce", "Produce — front-right bins", "Rotate to front; check for bruising hourly."),
            ["bananas"] = new("Produce", "Produce — center island", null),
            ["lettuce"] = new("Produce", "Produce refrigerated rack", null),
            ["chicken"] = new("Meat", "Aisle 10 refrigerated case; Deli counter for prepared", null),
            ["beef"] = new("Meat", "Aisle 10 refrigerated case", null),
            ["frozen pizza"] = new("Frozen", "Aisle 6", null),
            ["ice cream"] = new("Frozen", "Aisle 7", null),
            ["soda"] = new("Beverages", "Aisle 5", null),
            ["water"] = new("Beverages", "Aisle 5 end-cap", null),
            ["coffee"] = new("Beverages / Dry Goods", "Aisle 4", null),
            ["cereal"] = new("Dry Goods", "Aisle 2", null),
            ["pasta"] = new("Dry Goods", "Aisle 1", null),
            ["rice"] = new("Dry Goods", "Aisle 1", null),
            ["shampoo"] = new("Health & Beauty", "Aisle 14", null),
            ["soap"] = new("Household", "Aisle 15", null),
            ["detergent"] = new("Household", "Aisle 15", null),
            ["paper towels"] = new("Household", "Aisle 15", null),
            ["toilet paper"] = new("Household", "Aisle 15", null),
            ["baby food"] = new("Baby & Pharmacy", "Aisle 13", "Tamper-proof seals must be intact — per SOP §14."),
            ["diapers"] = new("Baby & Pharmacy", "Aisle 13", null),
            ["alcohol"] = new("Beer / Wine / Spirits", "Aisle 8 — age-restricted, see SOP §20", "ID check required; see SOP §20."),
            ["beer"] = new("Beer / Wine / Spirits", "Aisle 8", "ID check required."),
            ["wine"] = new("Beer / Wine / Spirits", "Aisle 8", "ID check required.")
        };
    }

    private sealed record ProductLocation(string Department, string Aisle, string? Notes);

    private sealed record SearchSopArgs(string? Query, int? TopK);

    private sealed record LookupArgs(string? ItemName);
}
