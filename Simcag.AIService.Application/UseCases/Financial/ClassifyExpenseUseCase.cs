using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.UseCases.Financial;

public sealed class ClassifyExpenseUseCase : IClassifyExpenseUseCase
{
    private readonly IExpenseClassificationService _classificationService;

    public ClassifyExpenseUseCase(IExpenseClassificationService classificationService)
    {
        _classificationService = classificationService;
    }

    public Task<CategoryResult> ExecuteAsync(RawFinancialDataEvent financialData, CancellationToken cancellationToken) =>
        _classificationService.ClassifyAsync(financialData, cancellationToken);
}
