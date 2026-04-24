using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DDCRM.Shared.Auth;

public sealed class ServiceTokenAuthMiddleware(
    RequestDelegate next,
    IOptions<ServiceTokenAuthOptions> options)
{
    private readonly ServiceTokenAuthOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderNames.ServiceToken, out var tokenValue)
            || string.IsNullOrWhiteSpace(tokenValue))
        {
            throw new ApiErrorException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.Unauthorized,
                "Заголовок X-Service-Token обязателен.");
        }

        var token = tokenValue.ToString();

        if (_options.ForbiddenTokens.Contains(token, StringComparer.Ordinal))
        {
            throw new ApiErrorException(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Токен не может использоваться в internal-контуре.");
        }

        if (!_options.AcceptedTokens.Contains(token, StringComparer.Ordinal))
        {
            throw new ApiErrorException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.Unauthorized,
                "Невалидный service-auth токен.");
        }

        await next(context);
    }
}
