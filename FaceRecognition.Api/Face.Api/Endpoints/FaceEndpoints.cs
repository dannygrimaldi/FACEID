using Face.Api.Core.FacePipeline;
using Face.Api.Core.FacePipeline.Detection;
using Face.Api.Core.FacePipeline.Recognition;
using Face.Api.Core.FacePipeline.Alignment;
using Face.Api.Core.VectorDb;
using Face.Api.Exceptions;
using Face.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Face.Api.Endpoints;

public static class FaceEndpoints
{
    public static void MapFaceEndpoints(this WebApplication app)
    {
        app.MapPost("/test/detect", TestDetectHandler).WithName("TestDetect");
        app.MapPost("/face/search", FaceSearchHandler).WithName("FaceSearch");
        app.MapPost("/face/register", FaceRegisterHandler).WithName("FaceRegister");
    }

    private static async Task<IResult> TestDetectHandler(
        HttpRequest request,
        IFaceDetector detector,
        IArcFaceRecognizer arcFaceRecognizer,
        QdrantService qdrant,
        IRateLimitService rateLimit)
    {
        var clientId = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimit.AllowRequest(clientId, 10, TimeSpan.FromMinutes(1)))
            return Results.StatusCode(429);

        if (!request.HasFormContentType)
            throw new InvalidImageException("Expected multipart/form-data");

        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();

        if (file == null || file.Length == 0)
            throw new InvalidImageException("No image uploaded");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        using var image = Image.Load<Rgb24>(ms.ToArray());

        var detection = detector.Detect(image);
        if (detection.Score < 0.5f)
            throw new FaceNotDetectedException();

        using var aligned = FaceAligner.Align(image, detection.Landmarks);
        var arcTensor = ArcFacePreprocessor.ToTensor(aligned);
        var embedding = arcFaceRecognizer.ExtractEmbedding(arcTensor);
        var normalized = EmbeddingUtils.L2Normalize(embedding);

        await qdrant.UpsertAsync(
            collection: "faces",
            id: Guid.NewGuid().ToString(),
            vector: normalized,
            payload: new { employeeId = 789, name = "Zendaya", created = DateTime.UtcNow }
        );

        return Results.Ok(new
        {
            boundingBox = new
            {
                x = detection.BoundingBox.X,
                y = detection.BoundingBox.Y,
                width = detection.BoundingBox.Width,
                height = detection.BoundingBox.Height
            },
            score = detection.Score,
            landmarks = detection.Landmarks.Select(p => new { p.X, p.Y }),
            embeddingLength = normalized.Length
        });
    }

    private static async Task<IResult> FaceSearchHandler(
        HttpRequest request,
        IFaceDetector detector,
        IArcFaceRecognizer arcFaceRecognizer,
        QdrantService qdrant,
        IConfiguration config,
        IRateLimitService rateLimit)
    {
        var clientId = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimit.AllowRequest(clientId, 10, TimeSpan.FromMinutes(1)))
            return Results.StatusCode(429);

        if (!request.HasFormContentType)
            throw new InvalidImageException("Expected multipart/form-data");

        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();

        if (file == null || file.Length == 0)
            throw new InvalidImageException("No image uploaded");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        using var image = Image.Load<Rgb24>(ms.ToArray());

        var detection = detector.Detect(image);
        if (detection.Score < 0.5f)
            throw new FaceNotDetectedException();

        using var aligned = FaceAligner.Align(image, detection.Landmarks);
        var tensor = ArcFacePreprocessor.ToTensor(aligned);
        var embedding = arcFaceRecognizer.ExtractEmbedding(tensor);
        var normalized = EmbeddingUtils.L2Normalize(embedding);

        var match = await qdrant.SearchAsync("faces", normalized, 1);
        double scoreMin = config.GetValue<double>("ScoreMin");

        if (match == null || match.score < scoreMin)
            return Results.Ok(new { found = false });

        return Results.Ok(new { found = true, score = match.score, id = match.id, payload = match.payload });
    }

    private static async Task<IResult> FaceRegisterHandler(
        HttpRequest request,
        IFaceDetector detector,
        IArcFaceRecognizer arcFaceRecognizer,
        QdrantService qdrant,
        IRateLimitService rateLimit)
    {
        var clientId = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimit.AllowRequest(clientId, 5, TimeSpan.FromMinutes(1)))
            return Results.StatusCode(429);

        if (!request.HasFormContentType)
            throw new InvalidImageException("Expected multipart/form-data");

        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        var employeeId = form["employeeId"].ToString();
        var name = form["name"].ToString();

        if (file == null || file.Length == 0)
            throw new InvalidImageException("No image uploaded");

        if (string.IsNullOrWhiteSpace(employeeId))
            throw new InvalidImageException("employeeId required");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        using var image = Image.Load<Rgb24>(ms.ToArray());

        var detection = detector.Detect(image);
        if (detection.Score < 0.5f)
            throw new FaceNotDetectedException();

        using var aligned = FaceAligner.Align(image, detection.Landmarks);
        var tensor = ArcFacePreprocessor.ToTensor(aligned);
        var embedding = arcFaceRecognizer.ExtractEmbedding(tensor);
        var normalized = EmbeddingUtils.L2Normalize(embedding);

        var payload = new { employeeId = employeeId, name = name };

        await qdrant.UpsertAsync(
            collection: "faces",
            id: Guid.NewGuid().ToString(),
            vector: normalized,
            payload: payload
        );

        return Results.Ok(new { success = true, employeeId = employeeId, name = name, vectorSize = normalized.Length });
    }
}