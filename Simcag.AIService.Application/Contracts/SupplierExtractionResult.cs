namespace Simcag.AIService.Application.Contracts;

/// <summary>Fornecedor + produto/serviço. <see cref="Confidence"/> / <see cref="UsedFallback"/> agregam fornecedor (heurística legada).</summary>
public sealed record SupplierExtractionResult(
    string RawSupplierName,
    string NormalizedSupplierName,
    string? TaxId,
    decimal Confidence,
    bool UsedFallback,
    ProductExtractionResult Product);
