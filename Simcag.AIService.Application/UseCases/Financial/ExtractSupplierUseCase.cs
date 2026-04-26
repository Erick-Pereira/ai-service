using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.UseCases.Financial;

public sealed class ExtractSupplierUseCase : IExtractSupplierUseCase
{
    private readonly ISupplierExtractionService _supplierExtractionService;

    public ExtractSupplierUseCase(ISupplierExtractionService supplierExtractionService)
    {
        _supplierExtractionService = supplierExtractionService;
    }

    public Task<SupplierExtractionResult> ExecuteAsync(RawFinancialDataEvent financialData, CancellationToken cancellationToken) =>
        _supplierExtractionService.ExtractAsync(financialData, cancellationToken);
}
