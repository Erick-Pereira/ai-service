namespace Simcag.AIService.Domain.Events;

/// <summary>Evento de domínio disparado quando um produto é categorizado.</summary>
public sealed record ProductCategorizedEvent(
    Guid ProductId,
    string ProductName,
    Guid CategoryId,
    string CategoryName,
    decimal Confidence,
    bool UsedFallback,
    DateTime OccurredAt
) : IDomainEvent;
