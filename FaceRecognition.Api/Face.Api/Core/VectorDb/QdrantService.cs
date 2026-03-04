using System.Net.Http.Json;
using System.Diagnostics;

namespace Face.Api.Core.VectorDb;

public class QdrantService
{
    private readonly HttpClient _http;
    private readonly ILogger<QdrantService> _logger;

    public QdrantService(HttpClient http, ILogger<QdrantService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task UpsertAsync(
        string collection,
        string id,
        float[] vector,
        object payload)
    {
        var stopwatch = Stopwatch.StartNew();
        var request = new QdrantUpsertRequest
        {
            points = new List<QdrantPoint>
            {
                new QdrantPoint
                {
                    id = id,
                    vector = vector,
                    payload = payload
                }
            }
        };

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.PutAsJsonAsync(
                $"collections/{collection}/points",
                request
            );

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await ReadBodySnippetAsync(response);
                _logger.LogError(
                    "Qdrant upsert failed. Collection={Collection} Id={Id} StatusCode={StatusCode} ElapsedMs={ElapsedMs:0.000} Body={Body}",
                    collection,
                    id,
                    (int)response.StatusCode,
                    stopwatch.Elapsed.TotalMilliseconds,
                    responseBody);
            }

            response.EnsureSuccessStatusCode();
            _logger.LogDebug(
                "Qdrant upsert succeeded. Collection={Collection} Id={Id} ElapsedMs={ElapsedMs:0.000}",
                collection,
                id,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Qdrant upsert exception. Collection={Collection} Id={Id} ElapsedMs={ElapsedMs:0.000}",
                collection,
                id,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    public async Task<QdrantSearchResult?> SearchAsync(
    string collection,
    float[] vector,
    int limit = 1)
    {
        var stopwatch = Stopwatch.StartNew();
        var request = new
        {
            vector = vector,
            limit = limit,
            with_payload = true
        };

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.PostAsJsonAsync(
                $"collections/{collection}/points/search",
                request
            );

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await ReadBodySnippetAsync(response);
                _logger.LogError(
                    "Qdrant search failed. Collection={Collection} Limit={Limit} StatusCode={StatusCode} ElapsedMs={ElapsedMs:0.000} Body={Body}",
                    collection,
                    limit,
                    (int)response.StatusCode,
                    stopwatch.Elapsed.TotalMilliseconds,
                    responseBody);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>();
            var result = json?.result?.FirstOrDefault();

            _logger.LogDebug(
                "Qdrant search completed. Collection={Collection} Limit={Limit} Found={Found} ElapsedMs={ElapsedMs:0.000}",
                collection,
                limit,
                result is not null,
                stopwatch.Elapsed.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Qdrant search exception. Collection={Collection} Limit={Limit} ElapsedMs={ElapsedMs:0.000}",
                collection,
                limit,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    private static async Task<string> ReadBodySnippetAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return "<empty>";

        const int maxLength = 600;
        body = body.Replace('\r', ' ').Replace('\n', ' ');
        return body.Length <= maxLength ? body : $"{body[..maxLength]}...";
    }
}
