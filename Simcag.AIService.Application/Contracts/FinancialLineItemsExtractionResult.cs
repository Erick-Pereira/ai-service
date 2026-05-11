using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Contracts;

/// <summary>
/// Resultado da extração LLM de linhas de despesa (descrição + valor BRL).
/// </summary>
public sealed record FinancialLineItemsExtractionResult(
    IReadOnlyList<FinancialItem> Items,
    string? DocumentTitle,
    decimal Confidence,
    bool UsedFallback);
