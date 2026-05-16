using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Simcag.AIService.Api.Models.Insights;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Exceptions;
using Simcag.AIService.Application.UseCases.Insights;
using Simcag.Shared.Contracts;

namespace Simcag.AIService.Api.Controllers.Insights;

/// <summary>Narração explicativa (LLM) sobre insights já calculados pelo processing-service.</summary>
[ApiController]
[Route("api/ai/insights")]
public sealed class OperationalInsightsNarrativeController : ControllerBase
{
    private readonly INarrateOperationalInsightsUseCase _narrate;

    public OperationalInsightsNarrativeController(INarrateOperationalInsightsUseCase narrate) => _narrate = narrate;

    [HttpPost("narrative")]
    public async Task<IActionResult> Narrative([FromBody] NarrateOperationalInsightsRequest? body, CancellationToken ct)
    {
        if (body?.Items is null || body.Items.Count == 0)
            return BadRequest(ApiResponse<string>.Fail("items é obrigatório e não pode ser vazio."));

        try
        {
            var input = new NarrateOperationalInsightsInput
            {
                Language = string.IsNullOrWhiteSpace(body.Language) ? "pt" : body.Language.Trim(),
                Items = body.Items.Select(i => new NarrateOperationalInsightItemInput
                {
                    Id = i.Id ?? "",
                    Kind = i.Kind ?? "",
                    Title = i.Title ?? "",
                    Summary = i.Summary ?? "",
                    Severity = i.Severity ?? "",
                    ImpactScore = i.ImpactScore,
                    SimpleExplanation = i.SimpleExplanation ?? "",
                    Evidence = i.Evidence
                }).ToList()
            };

            var result = await _narrate.ExecuteAsync(input, ct).ConfigureAwait(false);
            return Ok(ApiResponse<NarrateOperationalInsightsResult>.Ok(result));
        }
        catch (AiServiceException ex)
        {
            return new ObjectResult(ApiResponse<string>.Fail(ex.Message)) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }
    }
}
