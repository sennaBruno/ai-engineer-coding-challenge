using Api.Models;

namespace Api.Services;

/// <summary>
/// Defines the tool surface the assistant can call. We expose two tools:
///
///   • search_sop — semantic retrieval over chunked SOP content. The primary
///     grounding mechanism: the model decides when a question requires
///     procedural detail and searches accordingly.
///
///   • lookup_product_location — deterministic aisle-map lookup for "where is X?"
///     style questions. Demonstrates the *when NOT to RAG* judgment: a structured
///     lookup is faster, cheaper, and more reliable than vector search for this.
///
/// Tool schemas are declared as JSON Schema strings so the registry stays
/// provider-agnostic; the ChatService converts them to OpenAI ChatTools.
/// </summary>
public sealed class SopToolRegistry : IToolRegistryService
{
    private static readonly IReadOnlyList<ToolDefinition> Tools =
    [
        new ToolDefinition
        {
            Name = "search_sop",
            Description =
                "Search the grocery store Standard Operating Procedures for passages relevant to the user's question. " +
                "Use this whenever the answer depends on specific procedures, policies, numbers, or safety rules from the SOP. " +
                "Returns the most relevant SOP passages with their source section.",
            ParametersSchemaJson = """
                {
                  "type": "object",
                  "properties": {
                    "query": {
                      "type": "string",
                      "description": "A focused natural-language query describing what to look up in the SOP."
                    },
                    "top_k": {
                      "type": "integer",
                      "description": "How many passages to retrieve. Use 3 for simple questions, up to 6 for broader questions.",
                      "default": 4,
                      "minimum": 1,
                      "maximum": 8
                    }
                  },
                  "required": ["query"],
                  "additionalProperties": false
                }
                """
        },
        new ToolDefinition
        {
            Name = "lookup_product_location",
            Description =
                "Look up the aisle and department where a product is stocked in the store. " +
                "Use this when an employee or customer asks where to find a specific item — " +
                "it returns a structured location answer faster and more reliably than a document search.",
            ParametersSchemaJson = """
                {
                  "type": "object",
                  "properties": {
                    "item_name": {
                      "type": "string",
                      "description": "The product the employee is trying to locate, e.g. 'milk', 'bread', 'shampoo'."
                    }
                  },
                  "required": ["item_name"],
                  "additionalProperties": false
                }
                """
        }
    ];

    public IReadOnlyList<ToolDefinition> GetAvailableTools() => Tools;
}
