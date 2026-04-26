using Simcag.Shared.Events;

namespace Simcag.AIService.Application.UseCases.Financial;

/// <summary>
/// Caso de uso: executar o pipeline completo (classificar, extrair, normalizar, validar) e produzir <see cref="EnrichedFinancialDataEvent"/>.
/// </summary>
public interface IBuildEnrichedFinancialDataEventUseCase
{
    Task<EnrichedFinancialDataEvent> ExecuteAsync(RawFinancialDataEvent rawData, CancellationToken cancellationToken);
}
