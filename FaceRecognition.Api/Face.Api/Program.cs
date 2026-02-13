using Face.Api.Core.FacePipeline;
using Face.Api.Core.FacePipeline.Alignment;
using Face.Api.Core.FacePipeline.Alignment;
using Face.Api.Core.FacePipeline.Detection;
using Face.Api.Core.FacePipeline.Recognition;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// 🔎 DEBUG CONFIG (FIJO EN DISCO)
// =====================================================
const string FaceDebugDir = @"C:\temp\face_debug";
Directory.CreateDirectory(FaceDebugDir);

// =====================================================
// Services
// =====================================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 🔹 Face Detection (SCRFD)
builder.Services.AddSingleton<IFaceDetector>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ScrfdDetector>>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();

    var modelPath = Path.Combine(
        env.ContentRootPath,
        "Models",
        "scrfd.onnx"
    );

    return new ScrfdDetector(modelPath, logger);
});


builder.Services.AddSingleton<IArcFaceRecognizer, ArcFaceRecognizer>();


// (Opcional) Pipeline completo
builder.Services.AddSingleton<FacePipeline>();

var app = builder.Build();

// =====================================================
// Middleware
// =====================================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// =====================================================
// ENDPOINT
// =====================================================
app.MapPost("/test/detect", async (
    HttpRequest request,
    IFaceDetector detector,
    IArcFaceRecognizer arcFaceRecognizer
) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file == null || file.Length == 0)
        return Results.BadRequest("No image uploaded");

    // =====================================================
    // 01️⃣ LOAD ORIGINAL
    // =====================================================
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    var image = Image.Load<Rgb24>(ms.ToArray());
    image.Save(Path.Combine(FaceDebugDir, "01_original.jpg"));

    // =====================================================
    // 02️⃣ DETECT FACE (SCRFD)
    // =====================================================
    var detection = detector.Detect(image);

    // Debug: bbox + landmarks
    image.Clone(ctx =>
    {
        // bbox
        ctx.Draw(
            Color.Lime,
            3,
            new Rectangle(
                detection.BoundingBox.X,
                detection.BoundingBox.Y,
                detection.BoundingBox.Width,
                detection.BoundingBox.Height
            )
        );

        // landmarks
        foreach (var p in detection.Landmarks)
            ctx.Fill(Color.Red, new Rectangle((int)p.X - 2, (int)p.Y - 2, 4, 4));
    })
    .Save(Path.Combine(FaceDebugDir, "02_detected.jpg"));

    // =====================================================
    // 03️⃣ FACE ALIGN (ArcFace-style, FULL IMAGE)
    // =====================================================
    var aligned = FaceAligner.Align(
        image,                 // imagen completa
        detection.Landmarks    // landmarks absolutos
    );

    aligned.Save(Path.Combine(FaceDebugDir, "03_aligned.jpg"));

    // =====================================================
    // 04️⃣ ARC FACE PREPROCESS (112x112 → tensor)
    // =====================================================
    var arcTensor = ArcFacePreprocessor.ToTensor(aligned);

    // (opcional) debug visual: lo que ArcFace "ve"
    aligned.Save(Path.Combine(FaceDebugDir, "04_arcface_input.jpg"));

    // =====================================================
    // 05️⃣ INFER EMBEDDING
    // =====================================================
    var embedding = arcFaceRecognizer.ExtractEmbedding(arcTensor);

    // =====================================================
    // 06️⃣ NORMALIZE EMBEDDING
    // =====================================================
    var normalized = EmbeddingUtils.L2Normalize(embedding);

    // Debug numérico
    File.WriteAllText(
        Path.Combine(FaceDebugDir, "05_embedding.txt"),
        string.Join(",", normalized)
    );

    // =====================================================
    // RESPONSE
    // =====================================================
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
});


app.Run();
