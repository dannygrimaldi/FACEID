using Microsoft.Extensions.Caching.Memory;

namespace Face.Api.Services;

public interface IRateLimitService
{
    bool AllowRequest(string clientId, int maxRequests, TimeSpan window);
}

public class InMemoryRateLimitService : IRateLimitService
{
    private readonly IMemoryCache _cache;

    public InMemoryRateLimitService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool AllowRequest(string clientId, int maxRequests, TimeSpan window)
    {
        var key = $"rate_limit_{clientId}";
        var requests = _cache.GetOrCreate<List<DateTime>>(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = window;
            return new List<DateTime>();
        });

        // Limpiar requests expiradas
        requests.RemoveAll(r => r < DateTime.UtcNow - window);

        if (requests.Count >= maxRequests)
        {
            return false;
        }

        requests.Add(DateTime.UtcNow);
        _cache.Set(key, requests, window);
        return true;
    }
}