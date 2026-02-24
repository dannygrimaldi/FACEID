using System.Net.Http.Json;

namespace Face.Api.Core.VectorDb;

public class QdrantService
{
    private readonly HttpClient _http;

    public QdrantService(HttpClient http)
    {
        _http = http;
    }

    public async Task UpsertAsync(
        string collection,
        string id,
        float[] vector,
        object payload)
    {
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

        var response = await _http.PutAsJsonAsync(
            $"collections/{collection}/points",
            request
        );

        response.EnsureSuccessStatusCode();
    }

    public async Task<QdrantSearchResult?> SearchAsync(
    string collection,
    float[] vector,
    int limit = 1)
    {
        var request = new
        {
            vector = vector,
            limit = limit,
            with_payload = true
        };

        var response = await _http.PostAsJsonAsync(
            $"collections/{collection}/points/search",
            request
        );

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>();

        return json?.result?.FirstOrDefault();
    }
}
