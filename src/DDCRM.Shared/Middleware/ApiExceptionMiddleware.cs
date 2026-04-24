using System.Text.Json;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DDCRM.Shared.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiErrorException exception)
        {
            await WriteErrorAsync(context, exception.StatusCode, exception.ErrorCode, exception.Message, exception.Details);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception");
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Внутренняя ошибка сервиса.",
                null);
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string errorCode, string message, object? details)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var payload = new ErrorResponse(errorCode, message, context.GetOrCreateRequestId(), details);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
