using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.UseCases.Financial;

public sealed class BuildEnrichedFinancialDataEventUseCase : IBuildEnrichedFinancialDataEventUseCase
{
    private readonly IFinancialEnrichmentOrchestrator _orchestrator;

    public BuildEnrichedFinancialDataEventUseCase(IFinancialEnrichmentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<EnrichedFinancialDataEvent> ExecuteAsync(RawFinancialDataEvent rawData, CancellationToken cancellationToken) =>
        _orchestrator.EnrichAsync(rawData, cancellationToken);
}
