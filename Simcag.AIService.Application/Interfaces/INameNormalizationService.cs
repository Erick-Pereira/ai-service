using Simcag.AIService.Application.Contracts;

namespace Simcag.AIService.Application.Interfaces;

/// <summary>
/// Serviço de normalização de nomes (fornecedores, descrições).
/// </summary>
public interface INameNormalizationService
{
    Task<NormalizedNameResult> NormalizeAsync(string rawName, CancellationToken ct);
}
