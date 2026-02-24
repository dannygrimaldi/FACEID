using Microsoft.Extensions.Options;
using Face.Api.Core.FacePipeline;
using Face.Api.Core.FacePipeline.Alignment;
using Face.Api.Core.FacePipeline.Detection;
using Face.Api.Core.FacePipeline.Recognition;
using Face.Api.Core.VectorDb;
using Face.Api.Exceptions;
using Face.Api.Middleware;
using Face.Api.Options;
using Face.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Face.Api.Endpoints;




var builder = WebApplication.CreateBuilder(args);

// =====================================================
// Services
// =====================================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configurar límites de request
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
});

// Configurar Qdrant options
builder.Services.AddOptions<QdrantOptions>()
    .BindConfiguration("Qdrant")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Health checks
builder.Services.AddHealthChecks();

// Rate limiting básico
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IRateLimitService, InMemoryRateLimitService>();

// HttpClient factory para Qdrant
builder.Services.AddHttpClient<QdrantService>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.DefaultRequestHeaders.Add("api-key", options.Key);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Face Detection (SCRFD)
builder.Services.AddSingleton<IFaceDetector>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ScrfdDetector>>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();

    var modelPath = Path.Combine(env.ContentRootPath, "Models", "scrfd.onnx");

    return new ScrfdDetector(modelPath, logger);
});

builder.Services.AddSingleton<IArcFaceRecognizer, ArcFaceRecognizer>();

// Pipeline completo
builder.Services.AddSingleton<FacePipeline>();


var app = builder.Build();

// =====================================================
// Middleware
// =====================================================
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Health check endpoint
app.MapHealthChecks("/health");

// Error endpoint for production
app.MapGet("/error", () => Results.Problem("An error occurred", statusCode: 500))
    .ExcludeFromDescription();

// =====================================================
// ENDPOINT
// =====================================================

// Mapear endpoints en módulo separado
app.MapFaceEndpoints();

/*app.MapPost("/face/search", async (
    HttpRequest request,
    IFaceDetector detector,
    IArcFaceRecognizer arcFaceRecognizer,
    QdrantService qdrant
) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file == null || file.Length == 0)
        return Results.BadRequest("No image uploaded");

    // ===============================
    // LOAD IMAGE
    // ===============================
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    var image = Image.Load<Rgb24>(ms.ToArray());

    // ===============================
    // DETECT
    // ===============================
    var detection = detector.Detect(image);

    // ===============================
    // ALIGN
    // ===============================
    var aligned = FaceAligner.Align(
        image,
        detection.Landmarks
    );

    // ===============================
    // EMBEDDING
    // ===============================
    var tensor = ArcFacePreprocessor.ToTensor(aligned);

    var embedding = arcFaceRecognizer.ExtractEmbedding(tensor);

    var normalized = EmbeddingUtils.L2Normalize(embedding);

    // ===============================
    // SEARCH QDRANT
    // ===============================
    var match = await qdrant.SearchAsync(
        "faces",
        normalized,
        1
    );

    if (match == null)
        return Results.Ok(new
        {
            found = false
        });

    return Results.Ok(new
    {
        found = true,
        score = match.score,
        id = match.id,
        payload = match.payload
    });
});
*/


/*
app.MapPost("/face/search", async (
    HttpRequest request,
    IFaceDetector detector,
    IArcFaceRecognizer arcFaceRecognizer,
    QdrantService qdrant,
    IConfiguration config,
    IRateLimitService rateLimit
) =>
{
    // Rate limiting básico
    var clientId = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!rateLimit.AllowRequest(clientId, 10, TimeSpan.FromMinutes(1)))
    {
        return Results.StatusCode(429);
    }

    if (!request.HasFormContentType)
        throw new InvalidImageException("Expected multipart/form-data");

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file == null || file.Length == 0)
        throw new InvalidImageException("No image uploaded");

    // ===============================
    // 1️⃣ LOAD IMAGE
    // ===============================
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    using var image = Image.Load<Rgb24>(ms.ToArray());

    // ===============================
    // 2️⃣ DETECT
    // ===============================
    var detection = detector.Detect(image);

    if (detection.Score < 0.5f)
        throw new FaceNotDetectedException();

    // ===============================
    // 3️⃣ ALIGN
    // ===============================
    using var aligned = FaceAligner.Align(image, detection.Landmarks);

    // ===============================
    // 4️⃣ ARC PREPROCESS
    // ===============================
    var tensor = ArcFacePreprocessor.ToTensor(aligned);

    // ===============================
    // 5️⃣ EMBEDDING
    // ===============================
    var embedding = arcFaceRecognizer.ExtractEmbedding(tensor);

    var normalized = EmbeddingUtils.L2Normalize(embedding);

    // ===============================
    // 6️⃣ QDRANT SEARCH
    // ===============================
    var match = await qdrant.SearchAsync("faces", normalized, 1);

    double scoreMin = config.GetValue<double>("ScoreMin");

    if (match == null || match.score < scoreMin)
    {
        return Results.Ok(new { found = false });
    }

    return Results.Ok(new
    {
        found = true,
        score = match.score,
        id = match.id,
        payload = match.payload
    });
});*/
/*
app.MapPost("/face/register", async (
    HttpRequest request,
    IFaceDetector detector,
    IArcFaceRecognizer arcFaceRecognizer,
    QdrantService qdrant,
    IRateLimitService rateLimit
) =>
{
    // Rate limiting básico
    var clientId = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!rateLimit.AllowRequest(clientId, 5, TimeSpan.FromMinutes(1)))
    {
        return Results.StatusCode(429);
    }

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

    // ===============================
    // LOAD IMAGE
    // ===============================
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    using var image = Image.Load<Rgb24>(ms.ToArray());

    // ===============================
    // DETECT
    // ===============================
    var detection = detector.Detect(image);

    if (detection.Score < 0.5f)
        throw new FaceNotDetectedException();

    // ===============================
    // ALIGN
    // ===============================
    using var aligned = FaceAligner.Align(image, detection.Landmarks);

    // ===============================
    // EMBEDDING
    // ===============================
    var tensor = ArcFacePreprocessor.ToTensor(aligned);

    var embedding = arcFaceRecognizer.ExtractEmbedding(tensor);

    var normalized = EmbeddingUtils.L2Normalize(embedding);

    // ===============================
    // SAVE TO QDRANT
    // ===============================
    var payload = new
    {
        employeeId = employeeId,
        name = name
    };

    await qdrant.UpsertAsync(
        collection: "faces",
        id: Guid.NewGuid().ToString(),
        vector: normalized,
        payload: payload
    );

    return Results.Ok(new
    {
        success = true,
        employeeId = employeeId,
        name = name,
        vectorSize = normalized.Length
    });
});*/




app.Run();