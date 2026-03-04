using System.Diagnostics;

namespace Face.Api.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var requestId = context.TraceIdentifier;
        var clientIp = GetClientIp(context);

        context.Response.Headers["X-Request-ID"] = requestId;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = requestId,
            ["Method"] = request.Method,
            ["Path"] = request.Path.ToString(),
            ["ClientIp"] = clientIp
        });

        _logger.LogInformation("HTTP request started");

        var stopwatch = Stopwatch.StartNew();
        await _next(context);
        stopwatch.Stop();

        var statusCode = context.Response.StatusCode;
        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        if (statusCode >= 500)
        {
            _logger.LogError(
                "HTTP request finished with {StatusCode} in {ElapsedMs:0.000} ms",
                statusCode,
                elapsedMs);
            return;
        }

        if (statusCode == StatusCodes.Status429TooManyRequests)
        {
            _logger.LogWarning(
                "HTTP request rate-limited with {StatusCode} in {ElapsedMs:0.000} ms",
                statusCode,
                elapsedMs);
            return;
        }

        if (statusCode >= 400)
        {
            _logger.LogWarning(
                "HTTP request finished with {StatusCode} in {ElapsedMs:0.000} ms",
                statusCode,
                elapsedMs);
            return;
        }

        _logger.LogInformation(
            "HTTP request finished with {StatusCode} in {ElapsedMs:0.000} ms",
            statusCode,
            elapsedMs);
    }

    private static string GetClientIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
