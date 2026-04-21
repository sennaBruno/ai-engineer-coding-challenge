using Api.Contracts;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController(IRetrievalChatService retrievalChatService) : ControllerBase
{
    // DoS + wallet-drain guards: an unauthenticated endpoint must cap the input
    // an attacker can force us to forward to OpenAI.
    private const int MaxMessages = 40;
    private const int MaxCharsPerMessage = 8_000;

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Post(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Messages.Count == 0)
        {
            return BadRequest(new { error = "At least one chat message is required." });
        }

        if (request.Messages.Count > MaxMessages)
        {
            return BadRequest(new { error = $"Too many messages ({request.Messages.Count}). Maximum per request is {MaxMessages}." });
        }

        var oversized = request.Messages.FirstOrDefault(m => (m.Content?.Length ?? 0) > MaxCharsPerMessage);
        if (oversized is not null)
        {
            return BadRequest(new { error = $"Message content exceeds {MaxCharsPerMessage} characters." });
        }

        var response = await retrievalChatService.GenerateResponseAsync(request, cancellationToken);

        // Surface upstream model failures as HTTP 502 so log-based alerting and the
        // TypeScript client's !response.ok branch both trigger correctly. The JSON
        // body shape stays the same, so the frontend still sees the assistant text
        // and can render the error banner.
        return response.Status switch
        {
            "error" => StatusCode(StatusCodes.Status502BadGateway, response),
            _ => Ok(response)
        };
    }
}
