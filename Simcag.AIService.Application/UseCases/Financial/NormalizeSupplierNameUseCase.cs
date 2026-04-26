using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;

namespace Simcag.AIService.Application.UseCases.Financial;

public sealed class NormalizeSupplierNameUseCase : INormalizeSupplierNameUseCase
{
    private readonly INameNormalizationService _normalizationService;

    public NormalizeSupplierNameUseCase(INameNormalizationService normalizationService)
    {
        _normalizationService = normalizationService;
    }

    public Task<NormalizedNameResult> ExecuteAsync(string rawName, CancellationToken cancellationToken) =>
        _normalizationService.NormalizeAsync(rawName, cancellationToken);
}
