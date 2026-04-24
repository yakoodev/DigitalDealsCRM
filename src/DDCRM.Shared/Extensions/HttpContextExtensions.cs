using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.AspNetCore.Http;

namespace DDCRM.Shared.Extensions;

public static class HttpContextExtensions
{
    public static string GetOrCreateRequestId(this HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(HeaderNames.RequestId, out var value) && value is string requestId && !string.IsNullOrWhiteSpace(requestId))
        {
            return requestId;
        }

        requestId = Guid.NewGuid().ToString("N");
        httpContext.Items[HeaderNames.RequestId] = requestId;
        return requestId;
    }

    public static string RequireIdempotencyKey(this HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(HeaderNames.IdempotencyKey, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Header Idempotency-Key обязателен для mutating-операции.");
        }

        return rawValue.ToString();
    }
}
