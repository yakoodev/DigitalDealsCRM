using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DDCRM.Core.Persistence;
using DDCRM.Shared.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Core.Api.Integrations;

public sealed class CustomHttpIntegrationInvoker(
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto)
    : ICustomHttpIntegrationInvoker
{
    public async Task<CustomHttpInvokeResult> InvokeAsync(
        Guid projectId,
        Guid integrationId,
        CustomHttpInvokeRequest request,
        CancellationToken cancellationToken)
    {
        var integration = await dbContext.ProjectCustomHttpIntegrations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == integrationId && x.ProjectId == projectId,
                cancellationToken);

        if (integration is null)
        {
            throw new ApiErrorException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "Кастомная интеграция не найдена.");
        }

        if (!string.Equals(integration.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "Кастомная интеграция не активна.");
        }

        var allowlist = await dbContext.AdminCustomHttpAllowlist
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.HostPattern)
            .ToListAsync(cancellationToken);

        if (allowlist.Count == 0)
        {
            throw new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "Allowlist пуст. Вызовы custom HTTP запрещены.");
        }

        var endpoint = BuildEndpointUri(integration.BaseUrl, request.RelativePath);
        await ValidateTargetUriAsync(endpoint, allowlist, cancellationToken);

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
        };
        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        var method = ResolveMethod(request.Method);
        using var httpRequest = new HttpRequestMessage(method, endpoint);
        var token = crypto.Decrypt(integration.BearerTokenCiphertext);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var headers = MergeHeaders(integration.DefaultHeadersJson, request.Headers);
        foreach (var header in headers)
        {
            if (!httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                httpRequest.Content ??= new StringContent(string.Empty, Encoding.UTF8, "application/json");
                _ = httpRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.BodyJson))
        {
            httpRequest.Content = new StringContent(request.BodyJson, Encoding.UTF8, "application/json");
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
        {
            var redirectTarget = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(endpoint, response.Headers.Location);
            await ValidateTargetUriAsync(redirectTarget, allowlist, cancellationToken);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (responseBody.Length > 20_000)
        {
            responseBody = responseBody[..20_000];
        }

        var responseHeaders = response.Headers
            .Concat(response.Content.Headers)
            .ToDictionary(
                pair => pair.Key,
                pair => string.Join(",", pair.Value),
                StringComparer.OrdinalIgnoreCase);

        return new CustomHttpInvokeResult(
            (int)response.StatusCode,
            responseBody,
            responseHeaders,
            endpoint.ToString());
    }

    public static async Task ValidateTargetUriAsync(
        Uri endpoint,
        IReadOnlyCollection<string> allowlistPatterns,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Разрешены только HTTPS endpoint-ы для custom HTTP интеграций.");
        }

        var host = endpoint.Host.Trim().ToLowerInvariant();
        if (host.Length == 0)
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Некорректный host в endpoint custom HTTP.");
        }

        if (!allowlistPatterns.Any(pattern => HostMatchesPattern(host, pattern)))
        {
            throw new ApiErrorException(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Endpoint не входит в allowlist custom HTTP.");
        }

        if (IsLocalHostName(host))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Loopback/local endpoint запрещён политикой SSRF hardening.");
        }

        if (IPAddress.TryParse(host, out var ipAddress))
        {
            if (IsPrivateOrLocalAddress(ipAddress))
            {
                throw new ApiErrorException(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.ValidationError,
                    "Private/local target запрещён политикой SSRF hardening.");
            }

            return;
        }

        IPAddress[] resolved;
        try
        {
            resolved = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Не удалось разрешить host endpoint custom HTTP.");
        }

        if (resolved.Length == 0)
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Host endpoint custom HTTP не разрешается в IP.");
        }

        if (resolved.Any(IsPrivateOrLocalAddress))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Endpoint резолвится в private/local сеть и запрещён политикой SSRF hardening.");
        }
    }

    public static bool HostMatchesPattern(string host, string pattern)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var normalizedHost = host.Trim().ToLowerInvariant();
        var normalizedPattern = pattern.Trim().ToLowerInvariant();

        if (normalizedPattern.StartsWith("*."))
        {
            var suffix = normalizedPattern[2..];
            return normalizedHost == suffix || normalizedHost.EndsWith($".{suffix}", StringComparison.Ordinal);
        }

        return normalizedHost == normalizedPattern;
    }

    private static HttpMethod ResolveMethod(string method)
    {
        var normalized = string.IsNullOrWhiteSpace(method)
            ? "POST"
            : method.Trim().ToUpperInvariant();
        return normalized switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            _ => throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "method для custom HTTP должен быть GET/POST/PUT/PATCH/DELETE."),
        };
    }

    private static Uri BuildEndpointUri(string baseUrl, string? relativePath)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var baseUri))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "BaseUrl кастомной интеграции некорректен.");
        }

        var nextPath = relativePath?.Trim();
        if (string.IsNullOrWhiteSpace(nextPath))
        {
            return baseUri;
        }

        if (Uri.TryCreate(nextPath, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        return new Uri(baseUri, nextPath);
    }

    private static Dictionary<string, string> MergeHeaders(
        string? defaultHeadersJson,
        IReadOnlyDictionary<string, string>? requestHeaders)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(defaultHeadersJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(defaultHeadersJson);
                if (parsed is not null)
                {
                    foreach (var pair in parsed)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                        {
                            merged[pair.Key.Trim()] = pair.Value.Trim();
                        }
                    }
                }
            }
            catch
            {
                // ignore malformed defaults in runtime and keep request-level headers.
            }
        }

        if (requestHeaders is null)
        {
            return merged;
        }

        foreach (var pair in requestHeaders)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            merged[pair.Key.Trim()] = pair.Value.Trim();
        }

        return merged;
    }

    private static bool IsLocalHostName(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateOrLocalAddress(IPAddress ipAddress)
    {
        if (IPAddress.IsLoopback(ipAddress))
        {
            return true;
        }

        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6Multicast || ipAddress.IsIPv6SiteLocal)
            {
                return true;
            }

            var bytes = ipAddress.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }

        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return true;
        }

        var bytesV4 = ipAddress.GetAddressBytes();
        var first = bytesV4[0];
        var second = bytesV4[1];

        return first == 10
               || first == 127
               || (first == 169 && second == 254)
               || (first == 172 && second is >= 16 and <= 31)
               || (first == 192 && second == 168)
               || (first == 100 && second is >= 64 and <= 127)
               || first == 0;
    }
}
