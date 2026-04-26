using Microsoft.Extensions.Caching.Distributed;
using Simcag.AIService.Application.Interfaces;

namespace Simcag.AIService.Infrastructure.Cache;

public sealed class DistributedAiInferenceCache : IAiInferenceCache
{
    private readonly IDistributedCache _cache;

    public DistributedAiInferenceCache(IDistributedCache cache)
    {
        _cache = cache;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct)
        => _cache.GetStringAsync(key, ct);

    public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };

        return _cache.SetStringAsync(key, value, options, ct);
    }
}

