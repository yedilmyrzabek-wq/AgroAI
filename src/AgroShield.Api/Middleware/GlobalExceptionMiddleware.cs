using AgroShield.Domain.Exceptions;
using System.Text.Json;

namespace AgroShield.Api.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new { error = ErrorCode(ex), message = ex.Message, details = (string?)null },
                JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new { error = "internal_error", message = "An unexpected error occurred", details = (string?)null },
                JsonOpts);
        }
    }

    private static string ErrorCode(AppException ex) => ex switch
    {
        NotFoundException       => "not_found",
        Domain.Exceptions.ValidationException  => "validation_error",
        ForbiddenException      => "forbidden",
        ExternalServiceException => "service_unavailable",
        _                       => "internal_error"
    };
}
