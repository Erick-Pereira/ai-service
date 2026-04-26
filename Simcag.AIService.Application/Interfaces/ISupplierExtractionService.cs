using Simcag.AIService.Application.Contracts;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Interfaces;

/// <summary>
/// Serviço de extração de fornecedor (nome, documento fiscal) e de produto/serviço (marca, modelo, funcionalidades).
/// </summary>
public interface ISupplierExtractionService
{
    Task<SupplierExtractionResult> ExtractAsync(RawFinancialDataEvent financialData, CancellationToken ct);
}
