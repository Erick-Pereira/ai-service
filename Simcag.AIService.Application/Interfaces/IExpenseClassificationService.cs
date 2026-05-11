using Simcag.AIService.Application.Contracts;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Interfaces;

/// <summary>
/// Serviço de classificação de categoria de despesa.
/// </summary>
public interface IExpenseClassificationService
{
    /// <param name="classificationTextOverride">
    /// Texto enviado ao LLM em vez de <see cref="RawFinancialDataEvent.RawText"/> (ex.: linhas estruturadas extraídas por outro passo).
    /// </param>
    Task<CategoryResult> ClassifyAsync(RawFinancialDataEvent financialData, CancellationToken ct, string? classificationTextOverride = null);
}
