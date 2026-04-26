using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;

namespace Simcag.AIService.Application.UseCases.Financial;

/// <summary>
/// Caso de uso: normalizar nome de fornecedor (deduplicação semântica / canonicalização técnica).
/// </summary>
public interface INormalizeSupplierNameUseCase
{
    Task<NormalizedNameResult> ExecuteAsync(string rawName, CancellationToken cancellationToken);
}
