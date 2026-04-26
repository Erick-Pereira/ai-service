using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.UseCases.Financial;

/// <summary>
/// Caso de uso: classificar categoria de despesa a partir de dados financeiros brutos.
/// </summary>
public interface IClassifyExpenseUseCase
{
    Task<CategoryResult> ExecuteAsync(RawFinancialDataEvent financialData, CancellationToken cancellationToken);
}
