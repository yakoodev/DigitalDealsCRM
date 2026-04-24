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
                _options.MissingTokenErrorCode,
                _options.MissingTokenErrorMessage);
        }

        var token = tokenValue.ToString();

        if (_options.ForbiddenTokens.Contains(token, StringComparer.Ordinal))
        {
            throw new ApiErrorException(
                StatusCodes.Status403Forbidden,
                _options.ForbiddenTokenErrorCode,
                _options.ForbiddenTokenErrorMessage);
        }

        if (!_options.AcceptedTokens.Contains(token, StringComparer.Ordinal))
        {
            throw new ApiErrorException(
                StatusCodes.Status401Unauthorized,
                _options.InvalidTokenErrorCode,
                _options.InvalidTokenErrorMessage);
        }

        await next(context);
    }
}
