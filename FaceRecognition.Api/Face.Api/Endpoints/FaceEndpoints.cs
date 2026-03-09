using Face.Api.Controllers;
using Face.Api.Options;
using Face.Api.Services;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Face.Api.Endpoints;

public sealed class FaceEndpointsLogContext { }

public static class FaceEndpoints
{
    public static void MapFaceEndpoints(this WebApplication app)
    {
        app.MapPost("/face/search", FaceSearchHandler).WithName("FaceSearch");
        app.MapPost("/face/register", FaceRegisterHandler).WithName("FaceRegister");
    }

    private static Task<IResult> FaceSearchHandler(
        HttpRequest request,
        FaceController controller,
        IRateLimitService rateLimit,
        IOptions<RateLimitOptions> rateLimitOptions,
        ILogger<FaceEndpointsLogContext> logger,
        CancellationToken cancellationToken)
    {
        return ExecuteWithRateLimitAsync(
            request,
            controller.SearchAsync,
            rateLimit,
            rateLimitOptions,
            logger,
            "face-search",
            limits => limits.SearchMaxRequests,
            cancellationToken);
    }

    private static Task<IResult> FaceRegisterHandler(
        HttpRequest request,
        FaceController controller,
        IRateLimitService rateLimit,
        IOptions<RateLimitOptions> rateLimitOptions,
        ILogger<FaceEndpointsLogContext> logger,
        CancellationToken cancellationToken)
    {
        return ExecuteWithRateLimitAsync(
            request,
            controller.RegisterAsync,
            rateLimit,
            rateLimitOptions,
            logger,
            "face-register",
            limits => limits.RegisterMaxRequests,
            cancellationToken);
    }

    private static async Task<IResult> ExecuteWithRateLimitAsync(
        HttpRequest request,
        Func<HttpRequest, CancellationToken, Task<IResult>> action,
        IRateLimitService rateLimit,
        IOptions<RateLimitOptions> rateLimitOptions,
        ILogger logger,
        string scope,
        Func<RateLimitOptions, int> maxRequestsSelector,
        CancellationToken cancellationToken)
    {
        var limits = rateLimitOptions.Value;
        var clientId = GetClientId(request);
        var window = TimeSpan.FromSeconds(limits.WindowSeconds);
        var maxRequests = maxRequestsSelector(limits);

        if (!rateLimit.AllowRequest(clientId, scope, maxRequests, window))
            return TooManyRequests(request.HttpContext, logger, scope, clientId, maxRequests, window);

        return await action(request, cancellationToken);
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
