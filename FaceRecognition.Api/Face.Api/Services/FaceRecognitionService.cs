using Face.Api.Core.VectorDb;
using Face.Api.Exceptions;
using Face.Api.Models;
using Face.Api.Options;
using Face.Api.Repositories;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Face.Api.Services;

internal sealed class FaceSearchCandidate
{
    public required string PersonId { get; init; }
    public required float ScoreRgb { get; init; }
    public required float ScoreIr { get; init; }
    public required float ScoreFinal { get; init; }
    public required string MatchSource { get; init; }
    public Dictionary<string, object>? Payload { get; init; }
}

public interface IFaceRecognitionService
{
    Task<FaceLegacyRegisterResponse> RegisterLegacyRgbAsync(string employeeId, string? name, byte[] rgbImageBytes, CancellationToken cancellationToken = default);
    Task<FaceLegacySearchResponse> SearchLegacyRgbAsync(byte[] rgbImageBytes, CancellationToken cancellationToken = default);
    Task<FaceRegisterPairResponse> RegisterRgbIrPairsAsync(FaceRegisterPairRequest request, CancellationToken cancellationToken = default);
    Task<FaceSearchPairResponse> SearchRgbIrAsync(FaceSearchRequest request, CancellationToken cancellationToken = default);
}

public class FaceRecognitionService : IFaceRecognitionService
{
    private const string RgbSuperType = "rgb_super";
    private const string IrSuperType = "ir_super";
    private const string RgbTemplateType = "rgb_template";
    private const string IrTemplateType = "ir_template";

    private readonly IEmbeddingService _embeddingService;
    private readonly IQdrantRepository _qdrantRepository;
    private readonly FaceRecognitionOptions _options;
    private readonly ILogger<FaceRecognitionService> _logger;

    public FaceRecognitionService(
        IEmbeddingService embeddingService,
        IQdrantRepository qdrantRepository,
        IOptions<FaceRecognitionOptions> options,
        ILogger<FaceRecognitionService> logger)
    {
        _embeddingService = embeddingService;
        _qdrantRepository = qdrantRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FaceLegacyRegisterResponse> RegisterLegacyRgbAsync(
        string employeeId,
        string? name,
        byte[] rgbImageBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            throw new InvalidImageException("employeeId required");

        var embedding = await _embeddingService.ExtractFromBytesAsync(rgbImageBytes, "rgb", cancellationToken);
        var personId = employeeId.Trim();
        var superPointId = BuildPointId(personId, RgbSuperType);

        var payload = new Dictionary<string, object?>
        {
            ["person_id"] = personId,
            ["employeeId"] = personId,
            ["name"] = name ?? string.Empty,
            ["type"] = RgbSuperType,
            ["modality"] = "rgb",
            ["source"] = "legacy_rgb",
            ["updated_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        await _qdrantRepository.UpsertAsync(
            _options.CollectionName,
            new QdrantVectorPoint
            {
                Id = superPointId,
                Vector = embedding.Vector,
                Payload = payload
            },
            cancellationToken);

        _logger.LogInformation(
            "Legacy RGB registration completed. PersonId={PersonId} DetectionScore={DetectionScore:0.0000}",
            personId,
            embedding.DetectionScore);

        return new FaceLegacyRegisterResponse
        {
            Success = true,
            EmployeeId = personId,
            Name = name,
            VectorSize = embedding.Vector.Length
        };
    }

    public async Task<FaceRegisterPairResponse> RegisterRgbIrPairsAsync(
        FaceRegisterPairRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PersonId))
            throw new InvalidImageException("person_id required");

        if (request.Pairs is null || request.Pairs.Count == 0)
            throw new InvalidImageException("pairs required");

        var personId = request.PersonId.Trim();
        var rgbEmbeddings = new List<(float[] Vector, float Quality)>();
        var irEmbeddings = new List<(float[] Vector, float Quality)>();

        foreach (var pair in request.Pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(pair.RgbImage))
                throw new InvalidImageException("rgb_image required for all pairs");

            if (string.IsNullOrWhiteSpace(pair.IrImage))
                throw new InvalidImageException("ir_image required for all pairs");

            var rgb = await _embeddingService.ExtractFromBase64Async(pair.RgbImage, "rgb", cancellationToken);
            var ir = await _embeddingService.ExtractFromBase64Async(pair.IrImage, "ir", cancellationToken);

            rgbEmbeddings.Add((rgb.Vector, rgb.DetectionScore));
            irEmbeddings.Add((ir.Vector, ir.DetectionScore));
        }

        var rgbSuper = _embeddingService.BuildSuperEmbedding(rgbEmbeddings.Select(x => x.Vector).ToList());
        var irSuper = _embeddingService.BuildSuperEmbedding(irEmbeddings.Select(x => x.Vector).ToList());
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var points = new List<QdrantVectorPoint>
        {
            new()
            {
                Id = BuildPointId(personId, RgbSuperType),
                Vector = rgbSuper,
                Payload = BuildPayload(personId, request.Name, RgbSuperType, now)
            },
            new()
            {
                Id = BuildPointId(personId, IrSuperType),
                Vector = irSuper,
                Payload = BuildPayload(personId, request.Name, IrSuperType, now)
            }
        };

        points.AddRange(BuildTemplatePoints(personId, request.Name, rgbEmbeddings, RgbTemplateType, now));
        points.AddRange(BuildTemplatePoints(personId, request.Name, irEmbeddings, IrTemplateType, now));

        await _qdrantRepository.UpsertBatchAsync(_options.CollectionName, points, cancellationToken);

        _logger.LogInformation(
            "RGB+IR registration completed. PersonId={PersonId} Pairs={Pairs} TemplatesSaved={TemplatesSaved}",
            personId,
            request.Pairs.Count,
            Math.Max(0, points.Count - 2));

        return new FaceRegisterPairResponse
        {
            Success = true,
            PersonId = personId,
            PairsProcessed = request.Pairs.Count,
            RgbSuperVectorSize = rgbSuper.Length,
            IrSuperVectorSize = irSuper.Length,
            TemplatesSaved = Math.Max(0, points.Count - 2)
        };
    }

    public async Task<FaceLegacySearchResponse> SearchLegacyRgbAsync(
        byte[] rgbImageBytes,
        CancellationToken cancellationToken = default)
    {
        var rgbLive = await _embeddingService.ExtractFromBytesAsync(rgbImageBytes, "rgb", cancellationToken);
        var results = await _qdrantRepository.SearchAsync(
            _options.CollectionName,
            rgbLive.Vector,
            _options.SearchTopK,
            cancellationToken);

        var best = results
            .Where(IsRgbCompatibleResult)
            .OrderByDescending(r => r.score)
            .FirstOrDefault();

        if (best is null || best.score < _options.MatchScoreMin)
        {
            _logger.LogInformation(
                "Legacy RGB search completed with no match. ScoreMin={ScoreMin:0.0000}",
                _options.MatchScoreMin);

            return new FaceLegacySearchResponse
            {
                Found = false
            };
        }

        var personId = ResolvePersonId(best);
        if (!string.IsNullOrWhiteSpace(personId) && best.score >= _options.AutoUpdateThreshold)
            await TryAutoUpdateSuperEmbeddingAsync(personId!, RgbSuperType, rgbLive.Vector, cancellationToken);

        _logger.LogInformation(
            "Recognition audit timestamp={Timestamp} person_id_detected={PersonIdDetected} score_rgb={ScoreRgb:0.0000} score_ir={ScoreIr:0.0000} score_final={ScoreFinal:0.0000}",
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            personId ?? "<unknown>",
            best.score,
            0f,
            best.score);

        return new FaceLegacySearchResponse
        {
            Found = true,
            Score = best.score,
            Id = best.id,
            Payload = best.payload
        };
    }

    public async Task<FaceSearchPairResponse> SearchRgbIrAsync(
        FaceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var hasRgb = !string.IsNullOrWhiteSpace(request.RgbImage);
        var hasIr = !string.IsNullOrWhiteSpace(request.IrImage);

        if (!hasRgb && !hasIr)
            throw new InvalidImageException("At least one of rgb_image or ir_image is required");

        ExtractedEmbedding? rgbLive = null;
        ExtractedEmbedding? irLive = null;

        if (hasRgb)
            rgbLive = await _embeddingService.ExtractFromBase64Async(request.RgbImage!, "rgb", cancellationToken);

        if (hasIr)
            irLive = await _embeddingService.ExtractFromBase64Async(request.IrImage!, "ir", cancellationToken);

        var rgbSearchTask = rgbLive is null
            ? Task.FromResult<IReadOnlyList<QdrantSearchResult>>(Array.Empty<QdrantSearchResult>())
            : _qdrantRepository.SearchAsync(_options.CollectionName, rgbLive.Vector, _options.SearchTopK, cancellationToken);

        var irSearchTask = irLive is null
            ? Task.FromResult<IReadOnlyList<QdrantSearchResult>>(Array.Empty<QdrantSearchResult>())
            : _qdrantRepository.SearchAsync(_options.CollectionName, irLive.Vector, _options.SearchTopK, cancellationToken);

        await Task.WhenAll(rgbSearchTask, irSearchTask);

        var candidates = MergeCandidates(rgbSearchTask.Result, irSearchTask.Result);

        if (candidates.Count == 0 || candidates[0].ScoreFinal < _options.MatchScoreMin)
        {
            _logger.LogInformation(
                "Recognition audit timestamp={Timestamp} person_id_detected={PersonIdDetected} score_rgb={ScoreRgb:0.0000} score_ir={ScoreIr:0.0000} score_final={ScoreFinal:0.0000}",
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                "<none>",
                candidates.FirstOrDefault()?.ScoreRgb ?? 0f,
                candidates.FirstOrDefault()?.ScoreIr ?? 0f,
                candidates.FirstOrDefault()?.ScoreFinal ?? 0f);

            return new FaceSearchPairResponse
            {
                Found = false,
                ScoreRgb = candidates.FirstOrDefault()?.ScoreRgb ?? 0f,
                ScoreIr = candidates.FirstOrDefault()?.ScoreIr ?? 0f,
                ScoreFinal = candidates.FirstOrDefault()?.ScoreFinal ?? 0f,
                Candidates = candidates
                    .Take(_options.SearchTopK)
                    .Select(ToCandidateResponse)
                    .ToList()
            };
        }

        var best = candidates[0];

        if (best.ScoreFinal >= _options.AutoUpdateThreshold)
        {
            if (rgbLive is not null && best.ScoreRgb > 0)
                await TryAutoUpdateSuperEmbeddingAsync(best.PersonId, RgbSuperType, rgbLive.Vector, cancellationToken);

            if (irLive is not null && best.ScoreIr > 0)
                await TryAutoUpdateSuperEmbeddingAsync(best.PersonId, IrSuperType, irLive.Vector, cancellationToken);
        }

        _logger.LogInformation(
            "Recognition audit timestamp={Timestamp} person_id_detected={PersonIdDetected} score_rgb={ScoreRgb:0.0000} score_ir={ScoreIr:0.0000} score_final={ScoreFinal:0.0000}",
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            best.PersonId,
            best.ScoreRgb,
            best.ScoreIr,
            best.ScoreFinal);

        return new FaceSearchPairResponse
        {
            Found = true,
            PersonId = best.PersonId,
            ScoreRgb = best.ScoreRgb,
            ScoreIr = best.ScoreIr,
            ScoreFinal = best.ScoreFinal,
            MatchSource = best.MatchSource,
            Candidates = candidates
                .Take(_options.SearchTopK)
                .Select(ToCandidateResponse)
                .ToList()
        };
    }

    private async Task TryAutoUpdateSuperEmbeddingAsync(
        string personId,
        string type,
        float[] liveEmbedding,
        CancellationToken cancellationToken)
    {
        var pointId = BuildPointId(personId, type);
        var existing = await _qdrantRepository.GetByIdAsync(_options.CollectionName, pointId, cancellationToken);

        if (existing is null || existing.Vector.Length == 0)
            return;

        var updated = _embeddingService.BlendAndNormalize(
            existing.Vector,
            liveEmbedding,
            _options.AutoUpdateLiveWeight);

        var payload = existing.Payload?
            .ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)
            ?? new Dictionary<string, object?>();

        payload["person_id"] = personId;
        payload["type"] = type;
        payload["updated_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        payload["auto_updated"] = true;

        await _qdrantRepository.UpsertAsync(
            _options.CollectionName,
            new QdrantVectorPoint
            {
                Id = pointId,
                Vector = updated,
                Payload = payload
            },
            cancellationToken);

        _logger.LogInformation(
            "Template auto-updated. PersonId={PersonId} Type={Type} LiveWeight={LiveWeight:0.00}",
            personId,
            type,
            _options.AutoUpdateLiveWeight);
    }

    private List<FaceSearchCandidate> MergeCandidates(
        IReadOnlyList<QdrantSearchResult> rgbResults,
        IReadOnlyList<QdrantSearchResult> irResults)
    {
        var map = new Dictionary<string, (float ScoreRgb, float ScoreIr, Dictionary<string, object>? Payload)>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in rgbResults.Where(IsRgbCompatibleResult))
        {
            var personId = ResolvePersonId(result);
            if (string.IsNullOrWhiteSpace(personId))
                continue;

            if (!map.TryGetValue(personId, out var existing))
                map[personId] = (result.score, 0f, result.payload);
            else
                map[personId] = (Math.Max(existing.ScoreRgb, result.score), existing.ScoreIr, existing.Payload ?? result.payload);
        }

        foreach (var result in irResults.Where(IsIrCompatibleResult))
        {
            var personId = ResolvePersonId(result);
            if (string.IsNullOrWhiteSpace(personId))
                continue;

            if (!map.TryGetValue(personId, out var existing))
                map[personId] = (0f, result.score, result.payload);
            else
                map[personId] = (existing.ScoreRgb, Math.Max(existing.ScoreIr, result.score), existing.Payload ?? result.payload);
        }

        return map
            .Select(kvp =>
            {
                var scoreRgb = kvp.Value.ScoreRgb;
                var scoreIr = kvp.Value.ScoreIr;
                var scoreFinal = Math.Max(scoreRgb, scoreIr);

                return new FaceSearchCandidate
                {
                    PersonId = kvp.Key,
                    ScoreRgb = scoreRgb,
                    ScoreIr = scoreIr,
                    ScoreFinal = scoreFinal,
                    MatchSource = scoreRgb >= scoreIr ? "rgb" : "ir",
                    Payload = kvp.Value.Payload
                };
            })
            .OrderByDescending(c => c.ScoreFinal)
            .Take(_options.SearchTopK)
            .ToList();
    }

    private static bool IsRgbCompatibleResult(QdrantSearchResult result)
    {
        var type = ReadPayloadString(result.payload, "type");
        if (string.IsNullOrWhiteSpace(type))
            return true;

        return !type.StartsWith("ir_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIrCompatibleResult(QdrantSearchResult result)
    {
        var type = ReadPayloadString(result.payload, "type");
        if (string.IsNullOrWhiteSpace(type))
            return false;

        return type.StartsWith("ir_", StringComparison.OrdinalIgnoreCase);
    }

    private static FaceSearchCandidateResponse ToCandidateResponse(FaceSearchCandidate candidate) =>
        new()
        {
            PersonId = candidate.PersonId,
            ScoreRgb = candidate.ScoreRgb,
            ScoreIr = candidate.ScoreIr,
            ScoreFinal = candidate.ScoreFinal,
            MatchSource = candidate.MatchSource
        };

    private static string? ResolvePersonId(QdrantSearchResult result)
    {
        return ReadPayloadString(result.payload, "person_id")
            ?? ReadPayloadString(result.payload, "employeeId")
            ?? ReadPayloadString(result.payload, "employee_id");
    }

    private static string? ReadPayloadString(Dictionary<string, object>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
            return null;

        if (value is string rawString)
            return rawString;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => element.ToString()
            };
        }

        return value.ToString();
    }

    private string BuildPointId(string personId, string type) =>
        BuildPointIdFromKey(BuildPointKey(personId, type));

    private static string SanitizePersonId(string personId) =>
        personId.Trim().Replace(" ", "_", StringComparison.Ordinal);

    private static string BuildPointKey(string personId, string type) =>
        $"{SanitizePersonId(personId)}:{type}";

    private static string BuildPointIdFromKey(string key)
    {
        // Qdrant (newer versions) accepts point IDs as uint or UUID only.
        // Use deterministic UUID so the same person/type always maps to the same point.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, guidBytes.Length);
        return new Guid(guidBytes).ToString();
    }

    private static Dictionary<string, object?> BuildPayload(
        string personId,
        string? name,
        string type,
        string timestamp) =>
        new()
        {
            ["person_id"] = personId,
            ["name"] = name ?? string.Empty,
            ["type"] = type,
            ["modality"] = type.StartsWith("ir_", StringComparison.OrdinalIgnoreCase) ? "ir" : "rgb",
            ["updated_at"] = timestamp
        };

    private IEnumerable<QdrantVectorPoint> BuildTemplatePoints(
        string personId,
        string? name,
        IReadOnlyList<(float[] Vector, float Quality)> embeddings,
        string type,
        string timestamp)
    {
        if (_options.TemplatesPerModality <= 0 || embeddings.Count == 0)
            return Array.Empty<QdrantVectorPoint>();

        return embeddings
            .OrderByDescending(x => x.Quality)
            .Take(_options.TemplatesPerModality)
            .Select((entry, index) => new QdrantVectorPoint
            {
                Id = BuildPointIdFromKey($"{BuildPointKey(personId, type)}:{index + 1}"),
                Vector = entry.Vector,
                Payload = new Dictionary<string, object?>
                {
                    ["person_id"] = personId,
                    ["name"] = name ?? string.Empty,
                    ["type"] = type,
                    ["quality"] = entry.Quality,
                    ["template_rank"] = index + 1,
                    ["updated_at"] = timestamp
                }
            })
            .ToList();
    }
}
