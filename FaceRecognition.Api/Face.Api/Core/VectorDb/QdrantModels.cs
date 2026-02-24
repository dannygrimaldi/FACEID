namespace Face.Api.Core.VectorDb;

public class QdrantPoint
{
    public string id { get; set; } = default!;
    public float[] vector { get; set; } = default!;
    public object payload { get; set; } = default!;
}

public class QdrantUpsertRequest
{
    public List<QdrantPoint> points { get; set; } = new();
}

public class QdrantSearchResponse
{
    public List<QdrantSearchResult>? result { get; set; }
}

public class QdrantSearchResult
{
    public string id { get; set; } = "";
    public float score { get; set; }
    public Dictionary<string, object>? payload { get; set; }
}
