using DDCRM.Shared.Constants;
using DDCRM.Shared.Extensions;
using Microsoft.AspNetCore.Http;

namespace DDCRM.Shared.Middleware;

public sealed class RequestIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.Request.Headers.TryGetValue(HeaderNames.RequestId, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[HeaderNames.RequestId] = requestId;
        context.TraceIdentifier = requestId;
        context.Response.Headers[HeaderNames.RequestId] = requestId;

        await next(context);
    }
}
