using Simcag.AIService.Application.Contracts;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Interfaces;

/// <summary>
/// Serviço de extração de fornecedor (nome, documento fiscal) e de produto/serviço (marca, modelo, funcionalidades).
/// </summary>
public interface ISupplierExtractionService
{
    /// <param name="supplierPromptTextOverride">
    /// Texto base do prompt de extração em vez do corpo completo do documento (ex.: resumo de linhas de despesa).
    /// Fallback heurístico continua usando <see cref="RawFinancialDataEvent.RawText"/>.
    /// </param>
    Task<SupplierExtractionResult> ExtractAsync(RawFinancialDataEvent financialData, CancellationToken ct, string? supplierPromptTextOverride = null);
}
