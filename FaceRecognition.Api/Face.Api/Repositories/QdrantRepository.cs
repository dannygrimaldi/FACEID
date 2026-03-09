using Face.Api.Core.VectorDb;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Face.Api.Repositories;

public sealed class QdrantVectorPoint
{
    public required string Id { get; init; }
    public required float[] Vector { get; init; }
    public required Dictionary<string, object?> Payload { get; init; }
}

public sealed class QdrantStoredPoint
{
    public required string Id { get; init; }
    public required float[] Vector { get; init; }
    public Dictionary<string, object>? Payload { get; init; }
}

public interface IQdrantRepository
{
    Task UpsertAsync(string collection, QdrantVectorPoint point, CancellationToken cancellationToken = default);
    Task UpsertBatchAsync(string collection, IReadOnlyCollection<QdrantVectorPoint> points, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QdrantSearchResult>> SearchAsync(string collection, float[] vector, int topK, CancellationToken cancellationToken = default);
    Task<QdrantStoredPoint?> GetByIdAsync(string collection, string id, CancellationToken cancellationToken = default);
}

public class QdrantRepository : IQdrantRepository
{
    private readonly HttpClient _http;
    private readonly ILogger<QdrantRepository> _logger;

    public QdrantRepository(HttpClient http, ILogger<QdrantRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task UpsertAsync(string collection, QdrantVectorPoint point, CancellationToken cancellationToken = default) =>
        UpsertBatchAsync(collection, new[] { point }, cancellationToken);

    public async Task UpsertBatchAsync(
        string collection,
        IReadOnlyCollection<QdrantVectorPoint> points,
        CancellationToken cancellationToken = default)
    {
        if (points.Count == 0)
            return;

        var request = new
        {
            points = points.Select(p => new
            {
                id = p.Id,
                vector = p.Vector,
                payload = p.Payload
            })
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await _http.PutAsJsonAsync(
            $"collections/{collection}/points",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBodySnippetAsync(response, cancellationToken);
            _logger.LogError(
                "Qdrant upsert batch failed. Collection={Collection} Count={Count} StatusCode={StatusCode} Body={Body}",
                collection,
                points.Count,
                (int)response.StatusCode,
                body);
        }

        response.EnsureSuccessStatusCode();

        _logger.LogDebug(
            "Qdrant upsert batch completed. Collection={Collection} Count={Count} ElapsedMs={ElapsedMs:0.000}",
            collection,
            points.Count,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    public async Task<IReadOnlyList<QdrantSearchResult>> SearchAsync(
        string collection,
        float[] vector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            vector,
            limit = topK,
            with_payload = true
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await _http.PostAsJsonAsync(
            $"collections/{collection}/points/search",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBodySnippetAsync(response, cancellationToken);
            _logger.LogError(
                "Qdrant search failed. Collection={Collection} TopK={TopK} StatusCode={StatusCode} Body={Body}",
                collection,
                topK,
                (int)response.StatusCode,
                body);
        }

        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(cancellationToken: cancellationToken);
        var results = parsed?.result ?? new List<QdrantSearchResult>();

        _logger.LogDebug(
            "Qdrant search completed. Collection={Collection} TopK={TopK} Returned={Returned} ElapsedMs={ElapsedMs:0.000}",
            collection,
            topK,
            results.Count,
            stopwatch.Elapsed.TotalMilliseconds);

        return results;
    }

    public async Task<QdrantStoredPoint?> GetByIdAsync(
        string collection,
        string id,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            ids = new[] { id },
            with_payload = true,
            with_vector = true
        };

        var response = await _http.PostAsJsonAsync(
            $"collections/{collection}/points",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBodySnippetAsync(response, cancellationToken);
            _logger.LogError(
                "Qdrant get-by-id failed. Collection={Collection} Id={Id} StatusCode={StatusCode} Body={Body}",
                collection,
                id,
                (int)response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!TryGetFirstPoint(doc.RootElement, out var pointElement))
            return null;

        var vector = ReadVector(pointElement);
        if (vector is null || vector.Length == 0)
            return null;

        var payload = ReadPayload(pointElement);
        var pointId = ReadId(pointElement) ?? id;

        return new QdrantStoredPoint
        {
            Id = pointId,
            Vector = vector,
            Payload = payload
        };
    }

    private static bool TryGetFirstPoint(JsonElement root, out JsonElement point)
    {
        point = default;

        if (!root.TryGetProperty("result", out var result))
            return false;

        if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
        {
            point = result[0];
            return true;
        }

        if (result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("points", out var points)
            && points.ValueKind == JsonValueKind.Array
            && points.GetArrayLength() > 0)
        {
            point = points[0];
            return true;
        }

        return false;
    }

    private static float[]? ReadVector(JsonElement pointElement)
    {
        if (!pointElement.TryGetProperty("vector", out var vectorElement))
            return null;

        if (vectorElement.ValueKind != JsonValueKind.Array)
            return null;

        var vector = new float[vectorElement.GetArrayLength()];
        var index = 0;

        foreach (var item in vectorElement.EnumerateArray())
            vector[index++] = item.GetSingle();

        return vector;
    }

    private static Dictionary<string, object>? ReadPayload(JsonElement pointElement)
    {
        if (!pointElement.TryGetProperty("payload", out var payloadElement))
            return null;

        return JsonSerializer.Deserialize<Dictionary<string, object>>(payloadElement.GetRawText());
    }

    private static string? ReadId(JsonElement pointElement)
    {
        if (!pointElement.TryGetProperty("id", out var idElement))
            return null;

        return idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Number => idElement.GetRawText(),
            _ => null
        };
    }

    private static async Task<string> ReadBodySnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return "<empty>";

        const int maxLength = 600;
        body = body.Replace('\r', ' ').Replace('\n', ' ');
        return body.Length <= maxLength ? body : $"{body[..maxLength]}...";
    }
}
