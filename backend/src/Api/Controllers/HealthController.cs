using Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse
        {
            Status = "ok",
            Service = "grocery-store-sop-assistant-api",
            UtcTime = DateTimeOffset.UtcNow,
            Notes =
            [
                "Ingest chunks the SOP on markdown H2 boundaries and embeds via OpenAI.",
                "Chat grounds responses through two tools: search_sop (RAG) and lookup_product_location."
            ]
        });
    }
}