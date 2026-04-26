using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Interfaces;

/// <summary>
/// Orquestrador do pipeline completo de enriquecimento financeiro.
/// </summary>
public interface IFinancialEnrichmentOrchestrator
{
    /// <summary>Monta o evento enriquecido e publica em <c>enriched-financial-data-events</c> (fluxo worker / integração síncrona que deve propagar).</summary>
    Task<EnrichedFinancialDataEvent> EnrichAsync(RawFinancialDataEvent rawData, CancellationToken ct);

    /// <summary>Mesmo pipeline de <see cref="EnrichAsync"/> sem publicar no barramento (pré-visualização HTTP, testes, dry-run).</summary>
    Task<EnrichedFinancialDataEvent> PreviewEnrichmentAsync(RawFinancialDataEvent rawData, CancellationToken ct);
}
