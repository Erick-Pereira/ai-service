namespace Simcag.AIService.Application.DTOs;

/// <summary>
/// Resultado da categorização de um produto.
/// </summary>
public sealed record CategoryResultDto(Guid CategoryId, string CategoryName, decimal Confidence, string Reasoning, bool UsedFallback);

/// <summary>
/// Resultado da extração de entidades (brand, model, features).
/// </summary>
public sealed record EntityExtractionResultDto(string Brand, string Model, IReadOnlyList<string> Features, decimal Confidence, bool UsedFallback);

/// <summary>
/// Resultado da padronização de nome de produto.
/// </summary>
public sealed record StandardizationResultDto(string StandardizedName, string OriginalName, decimal Confidence, bool UsedFallback);

/// <summary>
/// Resultado combinado do processamento completo de um produto.
/// </summary>
public sealed record ProductProcessingResultDto(
    string OriginalDescription,
    CategoryResultDto Category,
    EntityExtractionResultDto Entities,
    StandardizationResultDto StandardizedName,
    DateTime ProcessedAt
);
