using Face.Api.Core.FacePipeline;
using Face.Api.Core.FacePipeline.Alignment;
using Face.Api.Core.FacePipeline.Alignment;
using Face.Api.Core.FacePipeline.Detection;
using Face.Api.Core.FacePipeline.Recognition;
using Face.Api.Core.VectorDb;
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


// 0 Pipeline completo
builder.Services.AddSingleton<FacePipeline>();

builder.Services.AddHttpClient<QdrantService>(c =>
{
    c.BaseAddress = new Uri("http://100.0.1.59:6333/");
});

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

    // =====================================================
    // 01️⃣ LOAD ORIGINAL
    // =====================================================
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    var image = Image.Load<Rgb24>(ms.ToArray());
    //image.Save(Path.Combine(FaceDebugDir, "01_original.png"));

    // =====================================================
    // 02️⃣ DETECT FACE (SCRFD)
    // =====================================================
    var detection = detector.Detect(image);

    // Debug: bbox + landmarks
    /*image.Clone(ctx =>
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
    .Save(Path.Combine(FaceDebugDir, "02_detected.png"));*/

    // =====================================================
    // 03️⃣ FACE ALIGN (ArcFace-style, FULL IMAGE)
    // =====================================================
    var aligned = FaceAligner.Align(
        image,                 // imagen completa
        detection.Landmarks    // landmarks absolutos
    );

    //aligned.Save(Path.Combine(FaceDebugDir, "03_aligned.png"));

    // =====================================================
    // 04️⃣ ARC FACE PREPROCESS (112x112 → tensor)
    // =====================================================
    var arcTensor = ArcFacePreprocessor.ToTensor(aligned);

    // (opcional) debug visual: lo que ArcFace "ve"
    //aligned.Save(Path.Combine(FaceDebugDir, "04_arcface_input.png"));

    // =====================================================
    // 05️⃣ INFER EMBEDDING
  /*  var debugImage = new Image<Rgb24>(112, 112);
    for (int y = 0; y < 112; y++)
    {
        for (int x = 0; x < 112; x++)
        {
            // Desnormalizar: asumiendo que la normalización es (p-127.5)/128
            float r = arcTensor[0, 0, y, x] * 128f + 127.5f;
            float g = arcTensor[0, 1, y, x] * 128f + 127.5f;
            float b = arcTensor[0, 2, y, x] * 128f + 127.5f;
            debugImage[x, y] = new Rgb24(
                (byte)Math.Clamp(r, 0, 255),
                (byte)Math.Clamp(g, 0, 255),
                (byte)Math.Clamp(b, 0, 255)
            );
        }
    }
    debugImage.Save(Path.Combine(FaceDebugDir, "model_input.png"));*/
    // =====================================================
    var embedding = arcFaceRecognizer.ExtractEmbedding(arcTensor);

    // =====================================================
    // 06️⃣ NORMALIZE EMBEDDING
    // =====================================================
    var normalized = EmbeddingUtils.L2Normalize(embedding);

   /* // Debug numérico
    File.WriteAllText(
        Path.Combine(FaceDebugDir, "05_embedding.txt"),
        string.Join(",", normalized)
    );
*/

    // =====================================================
    // 07️⃣ SAVE TO QDRANT
    // =====================================================

    await qdrant.UpsertAsync(
        collection: "faces",
        id: Guid.NewGuid().ToString(),
        vector: normalized,
        payload: new
        {
            employeeId = 789,
            name = "Zendaya",
            created = DateTime.UtcNow
        }
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


const float FACE_MATCH_THRESHOLD = 0.60f;
const string COLLECTION_NAME = "faces";

app.MapPost("/face/search", async (
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
    // 1️⃣ LOAD IMAGE
    // ===============================
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    var image = Image.Load<Rgb24>(ms.ToArray());
    //image.Save(Path.Combine(FaceDebugDir, "search_01_original.png"));

    // ===============================
    // 2️⃣ DETECT
    // ===============================
    var detection = detector.Detect(image);

   /* image.Clone(ctx =>
    {
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

        foreach (var p in detection.Landmarks)
            ctx.Fill(Color.Red, new Rectangle((int)p.X - 2, (int)p.Y - 2, 4, 4));
    })
    .Save(Path.Combine(FaceDebugDir, "search_02_detect.png"));*/

    // ===============================
    // 3️⃣ ALIGN
    // ===============================
    var aligned = FaceAligner.Align(
        image,
        detection.Landmarks
    );

    //aligned.Save(Path.Combine(FaceDebugDir, "search_03_aligned.png"));

    // ===============================
    // 4️⃣ ARC PREPROCESS
    // ===============================
    var tensor = ArcFacePreprocessor.ToTensor(aligned);

    // ===============================
    // 5️⃣ EMBEDDING
    // ===============================
   /* var debugImage = new Image<Rgb24>(112, 112);
    for (int y = 0; y < 112; y++)
    {
        for (int x = 0; x < 112; x++)
        {
            // Desnormalizar: asumiendo que la normalización es (p-127.5)/128
            float r = tensor[0, 0, y, x] * 128f + 127.5f;
            float g = tensor[0, 1, y, x] * 128f + 127.5f;
            float b = tensor[0, 2, y, x] * 128f + 127.5f;
            debugImage[x, y] = new Rgb24(
                (byte)Math.Clamp(r, 0, 255),
                (byte)Math.Clamp(g, 0, 255),
                (byte)Math.Clamp(b, 0, 255)
            );
        }
    }
    debugImage.Save(Path.Combine(FaceDebugDir, "model_input.png"));*/
    var embedding = arcFaceRecognizer.ExtractEmbedding(tensor);

    var normalized = EmbeddingUtils.L2Normalize(embedding);

    File.WriteAllText(
        Path.Combine(FaceDebugDir, "search_04_embedding.txt"),
        string.Join(",", normalized)
    );

    // ===============================
    // 6️⃣ QDRANT SEARCH
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

app.MapPost("/face/register", async (
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

    var employeeId = form["employeeId"].ToString();
    var name = form["name"].ToString();

    if (file == null || file.Length == 0)
        return Results.BadRequest("No image uploaded");

    if (string.IsNullOrWhiteSpace(employeeId))
        return Results.BadRequest("employeeId required");

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
/*var debugImage = new Image<Rgb24>(112, 112);
for (int y = 0; y < 112; y++)
{
    for (int x = 0; x < 112; x++)
    {
        // Desnormalizar: asumiendo que la normalización es (p-127.5)/128
        float r = tensor[0, 0, y, x] * 128f + 127.5f;
        float g = tensor[0, 1, y, x] * 128f + 127.5f;
        float b = tensor[0, 2, y, x] * 128f + 127.5f;
        debugImage[x, y] = new Rgb24(
            (byte)Math.Clamp(r, 0, 255),
            (byte)Math.Clamp(g, 0, 255),
            (byte)Math.Clamp(b, 0, 255)
        );
    }
}
debugImage.Save(Path.Combine(FaceDebugDir, "model_input.png"));*/
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
});




app.Run();
