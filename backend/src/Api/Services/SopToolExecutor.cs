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
        // Debug-level: argumentsJson can echo user-provided substrings ("lookup SSN 123-...").
        // Production log sinks should not capture that at Information level.
        logger.LogDebug("Executing tool {Tool} with args {Args}", toolName, argumentsJson);

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

        // Wrap each chunk body in <sop_chunk> delimiters. The system prompt instructs
        // the model to treat content inside these tags as untrusted data, not instructions
        // — this is our prompt-injection guard for retrieved SOP text.
        var payload = new
        {
            query = args.Query,
            results = matches.Select(match =>
            {
                var section = match.Record.Metadata.GetValueOrDefault("section", match.Record.Source);
                int.TryParse(match.Record.Metadata.GetValueOrDefault("startLine"), out var startLine);
                int.TryParse(match.Record.Metadata.GetValueOrDefault("endLine"), out var endLine);
                return new
                {
                    chunk_id = match.Record.Id,
                    section,
                    source = match.Record.Source,
                    start_line = startLine > 0 ? startLine : (int?)null,
                    end_line = endLine > 0 ? endLine : (int?)null,
                    score = Math.Round(match.Score, 4),
                    content = FormatAsSopChunk(section, startLine, endLine, match.Record.ChunkText)
                };
            })
        };

        return new ToolExecutionResult(JsonSerializer.Serialize(payload, OutputJsonOptions), matches);
    }

    private static string FormatAsSopChunk(string section, int startLine, int endLine, string body)
    {
        var lineAttr = startLine > 0
            ? (endLine > startLine ? $"lines=\"{startLine}-{endLine}\" " : $"line=\"{startLine}\" ")
            : string.Empty;
        // Keep the section attribute quoted and HTML-escape quotes so a crafted section
        // name can't break out of the attribute.
        var safeSection = section.Replace("\"", "&quot;");
        return $"<sop_chunk section=\"{safeSection}\" {lineAttr}>\n{body}\n</sop_chunk>";
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

        // Direct hit first, then deterministic substring match ranked by: exact prefix
        // match → shortest catalog key that contains the query → longest catalog key
        // contained by the query. Non-deterministic dictionary-order FirstOrDefault
        // previously made "ice" match either "ice cream" or "rice" depending on
        // insertion order.
        //
        // Reject pathologically short queries (e.g. "a", "s") from the substring
        // fallback — they would match half the catalog by accident and pick the
        // shortest key as a winner. Exact-match only for queries under 3 chars.
        const int MinSubstringQueryLength = 3;
        ProductLocation? location = null;
        if (!ProductCatalog.TryGetValue(normalized, out location) &&
            normalized.Length >= MinSubstringQueryLength)
        {
            var ranked = ProductCatalog
                .Where(entry => entry.Key.Contains(normalized) || normalized.Contains(entry.Key))
                .Select(entry => new
                {
                    entry.Key,
                    entry.Value,
                    PrefixMatch = entry.Key.StartsWith(normalized) || normalized.StartsWith(entry.Key) ? 0 : 1,
                    Length = entry.Key.Length
                })
                .OrderBy(x => x.PrefixMatch)
                .ThenBy(x => x.Length)
                .FirstOrDefault();
            location = ranked?.Value;
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
