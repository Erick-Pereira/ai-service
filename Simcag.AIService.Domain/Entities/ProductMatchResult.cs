using System;

namespace Simcag.AIService.Domain.Entities;

/// <summary>
/// Resultado do processamento para categorização e normalização de produtos.
/// </summary>
public class ProductMatchResult
{
    public Guid OriginalProductId { get; init; }
    public Guid? MatchedProductId { get; init; }
    public string OriginalProductName { get; init; } = string.Empty;
    public string MatchedProductName { get; init; } = string.Empty;
    public string MatchedCategory { get; init; } = string.Empty;
    public decimal MatchConfidence { get; init; }
    public string MatchReason { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}