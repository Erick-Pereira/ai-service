using Microsoft.Extensions.Caching.Distributed;
using Simcag.AIService.Application.Interfaces;

namespace Simcag.AIService.Infrastructure.Cache;

public sealed class DistributedIdempotencyStore : IIdempotencyStore
{
    private readonly IDistributedCache _cache;

    public DistributedIdempotencyStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<bool> HasProcessedAsync(string key, CancellationToken ct)
        => await _cache.GetStringAsync(key, ct) is not null;

    public Task MarkProcessedAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };

        // Value doesn't matter; existence is enough.
        return _cache.SetStringAsync(key, "1", options, ct);
    }
}

