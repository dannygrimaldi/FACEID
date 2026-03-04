using Microsoft.Extensions.Caching.Memory;

namespace Face.Api.Services;

public interface IRateLimitService
{
    bool AllowRequest(string clientId, string scope, int maxRequests, TimeSpan window);
}

public class InMemoryRateLimitService : IRateLimitService
{
    private readonly IMemoryCache _cache;
    private static readonly object _sync = new();

    public InMemoryRateLimitService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool AllowRequest(string clientId, string scope, int maxRequests, TimeSpan window)
    {
        var safeClientId = string.IsNullOrWhiteSpace(clientId) ? "unknown" : clientId;
        var safeScope = string.IsNullOrWhiteSpace(scope) ? "default" : scope;
        var key = $"rate_limit_{safeScope}_{safeClientId}";

        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var requests = _cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = window;
                return new List<DateTime>();
            })!;

            // Limpiar requests expiradas
            requests.RemoveAll(r => r < now - window);

            if (requests.Count >= maxRequests)
                return false;

            requests.Add(now);
            _cache.Set(key, requests, window);
            return true;
        }
    }
}
