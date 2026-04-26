using Simcag.Shared.Events;

namespace Simcag.AIService.Application.UseCases.Financial;

/// <summary>
/// Executa o pipeline de enriquecimento sem publicar no RabbitMQ (pré-visualização / dry-run).
/// </summary>
public interface IPreviewFinancialEnrichmentUseCase
{
    Task<EnrichedFinancialDataEvent> ExecuteAsync(RawFinancialDataEvent rawData, CancellationToken cancellationToken);
}
