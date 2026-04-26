namespace Simcag.AIService.Domain.Events;

/// <summary>Evento de domínio disparado quando o nome de um produto é padronizado.</summary>
public sealed record ProductStandardizedEvent(
    Guid ProductId,
    string OriginalName,
    string StandardizedName,
    decimal Confidence,
    bool UsedFallback,
    DateTime OccurredAt
) : IDomainEvent;
