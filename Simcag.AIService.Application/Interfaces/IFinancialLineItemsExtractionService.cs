using Simcag.AIService.Application.Contracts;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Interfaces;

/// <summary>
/// Extrai linhas de despesa estruturadas a partir do texto bruto (PDF/OCR) via LLM.
/// </summary>
public interface IFinancialLineItemsExtractionService
{
    Task<FinancialLineItemsExtractionResult> ExtractAsync(RawFinancialDataEvent financialData, CancellationToken ct);
}
