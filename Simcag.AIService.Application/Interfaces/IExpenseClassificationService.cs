using Simcag.AIService.Application.Contracts;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Interfaces;

/// <summary>
/// Serviço de classificação de categoria de despesa.
/// </summary>
public interface IExpenseClassificationService
{
    Task<CategoryResult> ClassifyAsync(RawFinancialDataEvent financialData, CancellationToken ct);
}
