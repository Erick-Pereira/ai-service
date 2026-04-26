namespace Simcag.AIService.Application.Interfaces;

public interface IIdempotencyStore
{
    Task<bool> HasProcessedAsync(string key, CancellationToken ct);
    Task MarkProcessedAsync(string key, TimeSpan ttl, CancellationToken ct);
}
