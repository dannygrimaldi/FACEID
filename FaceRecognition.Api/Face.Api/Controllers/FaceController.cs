using Face.Api.Exceptions;
using Face.Api.Models;
using Face.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Face.Api.Controllers;

public class FaceController
{
    private readonly IFaceRecognitionService _faceRecognitionService;
    private readonly ILogger<FaceController> _logger;

    public FaceController(
        IFaceRecognitionService faceRecognitionService,
        ILogger<FaceController> logger)
    {
        _faceRecognitionService = faceRecognitionService;
        _logger = logger;
    }

    public async Task<IResult> RegisterAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("FaceController register request received. ContentType={ContentType}", request.ContentType);

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                throw new InvalidImageException("No image uploaded");

            var employeeId = form["employeeId"].ToString();
            if (string.IsNullOrWhiteSpace(employeeId))
                employeeId = form["person_id"].ToString();

            var name = form["name"].ToString();
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);

            var result = await _faceRecognitionService.RegisterLegacyRgbAsync(
                employeeId,
                string.IsNullOrWhiteSpace(name) ? null : name,
                stream.ToArray(),
                cancellationToken);

            return Results.Ok(result);
        }

        var pairRequest = await request.ReadFromJsonAsync<FaceRegisterPairRequest>(cancellationToken);
        if (pairRequest is null)
            throw new InvalidImageException("Invalid register request body");

        var response = await _faceRecognitionService.RegisterRgbIrPairsAsync(pairRequest, cancellationToken);
        return Results.Ok(response);
    }

    public async Task<IResult> SearchAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("FaceController search request received. ContentType={ContentType}", request.ContentType);

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                throw new InvalidImageException("No image uploaded");

            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);

            var response = await _faceRecognitionService.SearchLegacyRgbAsync(stream.ToArray(), cancellationToken);
            return Results.Ok(response);
        }

        var payload = await request.ReadFromJsonAsync<FaceSearchRequest>(cancellationToken);
        if (payload is null)
            throw new InvalidImageException("Invalid search request body");

        var result = await _faceRecognitionService.SearchRgbIrAsync(payload, cancellationToken);
        return Results.Ok(result);
    }
}
