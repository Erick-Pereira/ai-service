using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.UseCases.Financial;

public sealed class PreviewFinancialEnrichmentUseCase : IPreviewFinancialEnrichmentUseCase
{
    private readonly IFinancialEnrichmentOrchestrator _orchestrator;

    public PreviewFinancialEnrichmentUseCase(IFinancialEnrichmentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<EnrichedFinancialDataEvent> ExecuteAsync(RawFinancialDataEvent rawData, CancellationToken cancellationToken) =>
        _orchestrator.PreviewEnrichmentAsync(rawData, cancellationToken);
}
