namespace Simcag.AIService.Application.Contracts;

/// <summary>Dicas de produto/serviço extraídas do texto (marca, modelo, bullet features), com confiança e fallback independentes do fornecedor.</summary>
public sealed record ProductExtractionResult(
    string? Brand,
    string? Model,
    IReadOnlyList<string> Features,
    decimal Confidence,
    bool UsedFallback);
