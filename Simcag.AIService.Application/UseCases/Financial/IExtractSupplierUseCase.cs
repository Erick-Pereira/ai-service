using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.UseCases.Financial;

/// <summary>
/// Caso de uso: extrair fornecedor (nome, documento fiscal quando disponível) do texto bruto.
/// </summary>
public interface IExtractSupplierUseCase
{
    Task<SupplierExtractionResult> ExecuteAsync(RawFinancialDataEvent financialData, CancellationToken cancellationToken);
}
