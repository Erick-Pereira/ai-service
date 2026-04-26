namespace Simcag.AIService.Application.Interfaces;

public interface IAiInferenceCache
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct);
}
