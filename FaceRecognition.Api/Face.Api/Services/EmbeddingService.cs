using Face.Api.Core.FacePipeline.Alignment;
using Face.Api.Core.FacePipeline.Detection;
using Face.Api.Core.FacePipeline.Recognition;
using Face.Api.Exceptions;
using Face.Api.Options;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Face.Api.Services;

public sealed class ExtractedEmbedding
{
    public required float[] Vector { get; init; }
    public required float DetectionScore { get; init; }
}

public interface IEmbeddingService
{
    Task<ExtractedEmbedding> ExtractFromBytesAsync(byte[] imageBytes, string channel, CancellationToken cancellationToken = default);
    Task<ExtractedEmbedding> ExtractFromBase64Async(string imageBase64, string channel, CancellationToken cancellationToken = default);
    float[] BuildSuperEmbedding(IReadOnlyList<float[]> embeddings);
    float[] BlendAndNormalize(float[] currentSuperEmbedding, float[] liveEmbedding, float liveWeight);
}

public class EmbeddingService : IEmbeddingService
{
    private readonly IFaceDetector _detector;
    private readonly IArcFaceRecognizer _recognizer;
    private readonly FaceRecognitionOptions _options;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IFaceDetector detector,
        IArcFaceRecognizer recognizer,
        IOptions<FaceRecognitionOptions> options,
        ILogger<EmbeddingService> logger)
    {
        _detector = detector;
        _recognizer = recognizer;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ExtractedEmbedding> ExtractFromBase64Async(
        string imageBase64,
        string channel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            throw new InvalidImageException($"{channel} image is required");

        var bytes = DecodeBase64Image(imageBase64);
        return ExtractFromBytesAsync(bytes, channel, cancellationToken);
    }

    public Task<ExtractedEmbedding> ExtractFromBytesAsync(
        byte[] imageBytes,
        string channel,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (imageBytes.Length == 0)
            throw new InvalidImageException($"{channel} image is empty");

        using var image = Image.Load<Rgb24>(imageBytes);
        var detection = _detector.Detect(image);

        if (detection.Score < _options.DetectionScoreMin)
            throw new FaceNotDetectedException();

        using var aligned = FaceAligner.Align(image, detection.Landmarks);
        var tensor = ArcFacePreprocessor.ToTensor(aligned);
        var embedding = _recognizer.ExtractEmbedding(tensor);
        var normalized = EmbeddingUtils.L2Normalize((float[])embedding.Clone());

        _logger.LogDebug(
            "Embedding generated. Channel={Channel} DetectionScore={DetectionScore:0.0000} VectorSize={VectorSize}",
            channel,
            detection.Score,
            normalized.Length);

        return Task.FromResult(new ExtractedEmbedding
        {
            Vector = normalized,
            DetectionScore = detection.Score
        });
    }

    public float[] BuildSuperEmbedding(IReadOnlyList<float[]> embeddings)
    {
        if (embeddings.Count == 0)
            throw new InvalidImageException("At least one embedding is required");

        var size = embeddings[0].Length;
        var accumulator = new float[size];

        foreach (var vector in embeddings)
        {
            if (vector.Length != size)
                throw new InvalidOperationException("Embedding vector sizes do not match");

            for (var i = 0; i < size; i++)
                accumulator[i] += vector[i];
        }

        for (var i = 0; i < size; i++)
            accumulator[i] /= embeddings.Count;

        return EmbeddingUtils.L2Normalize(accumulator);
    }

    public float[] BlendAndNormalize(float[] currentSuperEmbedding, float[] liveEmbedding, float liveWeight)
    {
        if (currentSuperEmbedding.Length != liveEmbedding.Length)
            throw new InvalidOperationException("Embedding vectors must have the same size to be blended");

        var blended = new float[currentSuperEmbedding.Length];
        var currentWeight = 1f - liveWeight;

        for (var i = 0; i < blended.Length; i++)
            blended[i] = (currentSuperEmbedding[i] * currentWeight) + (liveEmbedding[i] * liveWeight);

        return EmbeddingUtils.L2Normalize(blended);
    }

    private static byte[] DecodeBase64Image(string imageBase64)
    {
        var encoded = imageBase64.Trim();
        var commaIndex = encoded.IndexOf(',');

        if (commaIndex >= 0)
            encoded = encoded[(commaIndex + 1)..];

        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException ex)
        {
            throw new InvalidImageException($"Invalid base64 image format: {ex.Message}");
        }
    }
}
