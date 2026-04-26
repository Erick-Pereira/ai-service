namespace Simcag.AIService.Domain.Events;

/// <summary>Evento de domínio disparado quando entidades são extraídas de uma descrição.</summary>
public sealed record EntitiesExtractedEvent(
    Guid ProductId,
    string ProductName,
    string Brand,
    string Model,
    IReadOnlyList<string> Features,
    decimal Confidence,
    bool UsedFallback,
    DateTime OccurredAt
) : IDomainEvent;
