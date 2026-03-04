using Face.Api.Core.FacePipeline;
using Face.Api.Core.FacePipeline.Detection;
using Face.Api.Core.FacePipeline.Recognition;
using Face.Api.Core.FacePipeline.Alignment;
using Face.Api.Core.VectorDb;
using Face.Api.Exceptions;
using Face.Api.Options;
using Face.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using System.Globalization;

namespace Face.Api.Endpoints;

public sealed class FaceEndpointsLogContext { }

public static class FaceEndpoints
{
    public static void MapFaceEndpoints(this WebApplication app)
    {
       //app.MapPost("/test/detect", TestDetectHandler).WithName("TestDetect");
        app.MapPost("/face/search", FaceSearchHandler).WithName("FaceSearch");
        app.MapPost("/face/register", FaceRegisterHandler).WithName("FaceRegister");
    }

    private static async Task<IResult> TestDetectHandler(
        HttpRequest request,
        IFaceDetector detector,
        IArcFaceRecognizer arcFaceRecognizer,
        QdrantService qdrant,
        IRateLimitService rateLimit,
        IOptions<RateLimitOptions> rateLimitOptions,
        ILogger<FaceEndpointsLogContext> logger)
    {
        var clientId = GetClientId(request);
        var limits = rateLimitOptions.Value;
        var window = TimeSpan.FromSeconds(limits.WindowSeconds);
        var stopwatch = Stopwatch.StartNew();

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = request.HttpContext.TraceIdentifier,
            ["Operation"] = "test-detect",
            ["ClientId"] = clientId
        });

        logger.LogInformation("Test detect request started");

        if (!rateLimit.AllowRequest(clientId, "test-detect", limits.TestDetectMaxRequests, window))
            return TooManyRequests(
                request.HttpContext,
                logger,
                "test-detect",
                clientId,
                limits.TestDetectMaxRequests,
                window);

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

        stopwatch.Stop();
        logger.LogInformation("Test detect request completed in {ElapsedMs:0.000} ms", stopwatch.Elapsed.TotalMilliseconds);

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
        IRateLimitService rateLimit,
        IOptions<RateLimitOptions> rateLimitOptions,
        ILogger<FaceEndpointsLogContext> logger)
    {
        var clientId = GetClientId(request);
        var limits = rateLimitOptions.Value;
        var window = TimeSpan.FromSeconds(limits.WindowSeconds);
        var stopwatch = Stopwatch.StartNew();

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = request.HttpContext.TraceIdentifier,
            ["Operation"] = "face-search",
            ["ClientId"] = clientId
        });

        logger.LogInformation("Face search request started");

        if (!rateLimit.AllowRequest(clientId, "face-search", limits.SearchMaxRequests, window))
            return TooManyRequests(
                request.HttpContext,
                logger,
                "face-search",
                clientId,
                limits.SearchMaxRequests,
                window);

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
        {
            stopwatch.Stop();
            logger.LogInformation(
                "Face search completed with no match in {ElapsedMs:0.000} ms (ScoreMin={ScoreMin:0.0000})",
                stopwatch.Elapsed.TotalMilliseconds,
                scoreMin);

            return Results.Ok(new { found = false });
        }

        stopwatch.Stop();
        logger.LogInformation(
            "Face search completed with match in {ElapsedMs:0.000} ms (Score={Score:0.0000}, Id={MatchId})",
            stopwatch.Elapsed.TotalMilliseconds,
            match.score,
            match.id);

        return Results.Ok(new { found = true, score = match.score, id = match.id, payload = match.payload });
    }

    private static async Task<IResult> FaceRegisterHandler(
        HttpRequest request,
        IFaceDetector detector,
        IArcFaceRecognizer arcFaceRecognizer,
        QdrantService qdrant,
        IRateLimitService rateLimit,
        IOptions<RateLimitOptions> rateLimitOptions,
        ILogger<FaceEndpointsLogContext> logger)
    {
        var clientId = GetClientId(request);
        var limits = rateLimitOptions.Value;
        var window = TimeSpan.FromSeconds(limits.WindowSeconds);
        var stopwatch = Stopwatch.StartNew();

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = request.HttpContext.TraceIdentifier,
            ["Operation"] = "face-register",
            ["ClientId"] = clientId
        });

        logger.LogInformation("Face register request started");

        if (!rateLimit.AllowRequest(clientId, "face-register", limits.RegisterMaxRequests, window))
            return TooManyRequests(
                request.HttpContext,
                logger,
                "face-register",
                clientId,
                limits.RegisterMaxRequests,
                window);

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

        stopwatch.Stop();
        logger.LogInformation(
            "Face register request completed in {ElapsedMs:0.000} ms (EmployeeId={EmployeeId})",
            stopwatch.Elapsed.TotalMilliseconds,
            employeeId);

        return Results.Ok(new { success = true, employeeId = employeeId, name = name, vectorSize = normalized.Length });
    }

    private static string GetClientId(HttpRequest request)
    {
        var forwardedFor = request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static IResult TooManyRequests(
        HttpContext httpContext,
        ILogger logger,
        string scope,
        string clientId,
        int maxRequests,
        TimeSpan window)
    {
        var retryAfterSeconds = (int)Math.Ceiling(window.TotalSeconds);
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        logger.LogWarning(
            "Rate limit exceeded. Scope={Scope} ClientId={ClientId} MaxRequests={MaxRequests} WindowSeconds={WindowSeconds} RequestId={RequestId}",
            scope,
            clientId,
            maxRequests,
            (int)window.TotalSeconds,
            httpContext.TraceIdentifier);

        return Results.Json(
            new
            {
                error = "Too many requests",
                retryAfterSeconds,
                scope,
                requestId = httpContext.TraceIdentifier
            },
            statusCode: StatusCodes.Status429TooManyRequests
        );
    }
}
