using Microsoft.AspNetCore.Mvc;
using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Contracts;

namespace Simcag.AIService.Api.Controllers.Financial;

/// <summary>Capacidade operacional, Ollama e variáveis efetivas (sem segredos).</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "system-diagnostics")]
[Route("api/ai/system")]
public sealed class FinancialAiDiagnosticsController : ControllerBase
{
    private readonly IOllamaClient _ollama;

    public FinancialAiDiagnosticsController(IOllamaClient ollama)
    {
        _ollama = ollama;
    }

    [HttpGet("capabilities")]
    public ActionResult<ApiResponse<AiServiceCapabilitiesResponse>> Capabilities()
    {
        var exFromEnv = Environment.GetEnvironmentVariable("RABBITMQ_EVENTS_EXCHANGE")
            ?? Environment.GetEnvironmentVariable("RABBITMQ__EVENTS_EXCHANGE");
        const string defaultDomainEventsExchange = "events";
        var resolved = string.IsNullOrWhiteSpace(exFromEnv) ? defaultDomainEventsExchange : exFromEnv.Trim();

        var body = new AiServiceCapabilitiesResponse(
            Service: "Simcag.AIService",
            ConfiguredModel: AiServiceEnvironment.ModelName,
            IdempotencyTtlHours: AiServiceEnvironment.IdempotencyTtl.TotalHours,
            InferenceCacheTtlHours: AiServiceEnvironment.InferenceCacheTtl.TotalHours,
            SupplierNormalizationTtlHours: AiServiceEnvironment.SupplierNormalizationCacheTtl.TotalHours,
            EventsExchangeFromEnv: string.IsNullOrWhiteSpace(exFromEnv) ? null : exFromEnv.Trim(),
            ResolvedEventsExchange: resolved);

        return Ok(ApiResponse<AiServiceCapabilitiesResponse>.Ok(body));
    }

    [HttpGet("ollama/health")]
    public async Task<ActionResult<ApiResponse<OllamaHealthResponse>>> OllamaHealth(CancellationToken cancellationToken)
    {
        var ok = await _ollama.IsAvailableAsync(cancellationToken);
        return Ok(ApiResponse<OllamaHealthResponse>.Ok(new OllamaHealthResponse(ok)));
    }

    [HttpGet("ollama/models")]
    public async Task<ActionResult<ApiResponse<OllamaModelsResponse>>> OllamaModels(CancellationToken cancellationToken)
    {
        var names = await _ollama.ListInstalledModelNamesAsync(cancellationToken);
        return Ok(ApiResponse<OllamaModelsResponse>.Ok(new OllamaModelsResponse(names)));
    }
}
