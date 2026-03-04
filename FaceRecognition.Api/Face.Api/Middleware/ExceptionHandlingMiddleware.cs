using Face.Api.Exceptions;
using System.Net;

namespace Face.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            var request = context.Request;
            var requestId = context.TraceIdentifier;
            var clientIp = GetClientIp(context);

            _logger.LogWarning(
                ex,
                "Domain exception handled. Type={ExceptionType} Method={Method} Path={Path} RequestId={RequestId} ClientIp={ClientIp}",
                ex.GetType().Name,
                request.Method,
                request.Path.ToString(),
                requestId,
                clientIp);

            await HandleDomainExceptionAsync(context, ex);
        }
        catch (Exception ex)
        {
            var request = context.Request;
            var requestId = context.TraceIdentifier;
            var clientIp = GetClientIp(context);

            _logger.LogError(
                ex,
                "Unhandled exception. Method={Method} Path={Path} RequestId={RequestId} ClientIp={ClientIp}",
                request.Method,
                request.Path.ToString(),
                requestId,
                clientIp);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleDomainExceptionAsync(HttpContext context, DomainException exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

        var result = new
        {
            error = exception.Message,
            type = exception.GetType().Name,
            requestId = context.TraceIdentifier
        };

        await context.Response.WriteAsJsonAsync(result);
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var result = new
        {
            error = "An internal server error occurred",
            type = "InternalServerError",
            requestId = context.TraceIdentifier
        };

        await context.Response.WriteAsJsonAsync(result);
    }

    private static string GetClientIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
