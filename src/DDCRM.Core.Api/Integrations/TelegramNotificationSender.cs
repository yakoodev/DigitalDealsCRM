using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.Integrations;

public sealed class TelegramNotificationSender(
    IOptions<TelegramNotificationOptions> options,
    ILogger<TelegramNotificationSender> logger)
{
    private readonly TelegramNotificationOptions _options = options.Value;

    public async Task<TelegramBotIdentity> GetMeAsync(
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        var result = await GetMeWithDiagnosticsAsync(proxy, cancellationToken);
        if (result.Identity is not null)
        {
            return result.Identity;
        }

        throw new InvalidOperationException(result.ReasonCode ?? "telegram_connectivity_failed");
    }

    public async Task<TelegramConnectivityResult> GetMeWithDiagnosticsAsync(
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (proxy is null)
        {
            var direct = await TryGetMeAsync(proxy: null, cancellationToken);
            if (direct.Identity is not null)
            {
                return new TelegramConnectivityResult(
                    Status: "ok",
                    ProxyAttempted: false,
                    ProxySucceeded: false,
                    DirectAttempted: true,
                    DirectSucceeded: true,
                    EffectivePath: "direct",
                    ReasonCode: null,
                    ProxyError: null,
                    DirectError: null,
                    Identity: direct.Identity);
            }

            throw new InvalidOperationException(
                $"direct_egress_blocked: {direct.ErrorMessage ?? "unknown telegram connectivity error"}");
        }

        var proxyAttempt = await TryGetMeAsync(proxy, cancellationToken);
        if (proxyAttempt.Identity is not null)
        {
            return new TelegramConnectivityResult(
                Status: "ok",
                ProxyAttempted: true,
                ProxySucceeded: true,
                DirectAttempted: false,
                DirectSucceeded: false,
                EffectivePath: "proxy",
                ReasonCode: null,
                ProxyError: null,
                DirectError: null,
                Identity: proxyAttempt.Identity);
        }

        if (IsTelegramApiRejection(proxyAttempt.Error))
        {
            throw new InvalidOperationException(
                $"telegram_api_rejected: {proxyAttempt.ErrorMessage ?? "telegram api rejected getMe request"}");
        }

        if (!_options.FallbackToDirectOnProxyFailure || !IsProxyTransportFailure(proxyAttempt.Error))
        {
            throw new InvalidOperationException(
                $"proxy_upstream_blocked: {proxyAttempt.ErrorMessage ?? "proxy telegram connectivity failed"}");
        }

        var directAttempt = await TryGetMeAsync(proxy: null, cancellationToken);
        if (directAttempt.Identity is not null)
        {
            return new TelegramConnectivityResult(
                Status: "ok",
                ProxyAttempted: true,
                ProxySucceeded: false,
                DirectAttempted: true,
                DirectSucceeded: true,
                EffectivePath: "direct",
                ReasonCode: "proxy_upstream_blocked",
                ProxyError: proxyAttempt.ErrorMessage,
                DirectError: null,
                Identity: directAttempt.Identity);
        }

        throw new InvalidOperationException(
            $"both_failed: proxy={proxyAttempt.ErrorMessage ?? "n/a"}; direct={directAttempt.ErrorMessage ?? "n/a"}");
    }

    public async Task SendMessageAsync(
        string chatId,
        string message,
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        var result = await SendMessageWithDiagnosticsAsync(chatId, message, proxy, cancellationToken);
        if (result.Status == "ok")
        {
            return;
        }

        throw new InvalidOperationException($"{result.ReasonCode}: {result.ErrorMessage}");
    }

    public async Task<TelegramSendResult> SendMessageWithDiagnosticsAsync(
        string chatId,
        string message,
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (proxy is null)
        {
            var direct = await TrySendAsync(chatId, message, proxy: null, cancellationToken);
            if (direct.IsSuccess)
            {
                return new TelegramSendResult(
                    Status: "ok",
                    EffectivePath: "direct",
                    ReasonCode: null,
                    ProxyAttempted: false,
                    ProxySucceeded: false,
                    DirectAttempted: true,
                    DirectSucceeded: true,
                    ProxyError: null,
                    DirectError: null,
                    ErrorMessage: null);
            }

            return new TelegramSendResult(
                Status: "error",
                EffectivePath: null,
                ReasonCode: "direct_egress_blocked",
                ProxyAttempted: false,
                ProxySucceeded: false,
                DirectAttempted: true,
                DirectSucceeded: false,
                ProxyError: null,
                DirectError: direct.ErrorMessage,
                ErrorMessage: direct.ErrorMessage ?? "Telegram direct send failed.");
        }

        var proxyAttempt = await TrySendAsync(chatId, message, proxy, cancellationToken);
        if (proxyAttempt.IsSuccess)
        {
            return new TelegramSendResult(
                Status: "ok",
                EffectivePath: "proxy",
                ReasonCode: null,
                ProxyAttempted: true,
                ProxySucceeded: true,
                DirectAttempted: false,
                DirectSucceeded: false,
                ProxyError: null,
                DirectError: null,
                ErrorMessage: null);
        }

        if (IsTelegramApiRejection(proxyAttempt.Error))
        {
            return new TelegramSendResult(
                Status: "error",
                EffectivePath: null,
                ReasonCode: "telegram_api_rejected",
                ProxyAttempted: true,
                ProxySucceeded: false,
                DirectAttempted: false,
                DirectSucceeded: false,
                ProxyError: proxyAttempt.ErrorMessage,
                DirectError: null,
                ErrorMessage: proxyAttempt.ErrorMessage ?? "Telegram API rejected sendMessage.");
        }

        if (!_options.FallbackToDirectOnProxyFailure || !IsProxyTransportFailure(proxyAttempt.Error))
        {
            return new TelegramSendResult(
                Status: "error",
                EffectivePath: null,
                ReasonCode: "proxy_upstream_blocked",
                ProxyAttempted: true,
                ProxySucceeded: false,
                DirectAttempted: false,
                DirectSucceeded: false,
                ProxyError: proxyAttempt.ErrorMessage,
                DirectError: null,
                ErrorMessage: proxyAttempt.ErrorMessage ?? "Telegram proxy send failed.");
        }

        var directAttempt = await TrySendAsync(chatId, message, proxy: null, cancellationToken);
        if (directAttempt.IsSuccess)
        {
            return new TelegramSendResult(
                Status: "ok",
                EffectivePath: "direct",
                ReasonCode: "proxy_upstream_blocked",
                ProxyAttempted: true,
                ProxySucceeded: false,
                DirectAttempted: true,
                DirectSucceeded: true,
                ProxyError: proxyAttempt.ErrorMessage,
                DirectError: null,
                ErrorMessage: null);
        }

        return new TelegramSendResult(
            Status: "error",
            EffectivePath: null,
            ReasonCode: "both_failed",
            ProxyAttempted: true,
            ProxySucceeded: false,
            DirectAttempted: true,
            DirectSucceeded: false,
            ProxyError: proxyAttempt.ErrorMessage,
            DirectError: directAttempt.ErrorMessage,
            ErrorMessage: $"proxy={proxyAttempt.ErrorMessage ?? "n/a"}; direct={directAttempt.ErrorMessage ?? "n/a"}");
    }

    private async Task<TryGetMeResult> TryGetMeAsync(
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        try
        {
            var identity = await ExecuteGetMeAsync(proxy, cancellationToken);
            return new TryGetMeResult(identity, null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Telegram getMe failed. viaProxy={ViaProxy}",
                proxy is not null);
            return new TryGetMeResult(null, exception);
        }
    }

    private async Task<TrySendResult> TrySendAsync(
        string chatId,
        string message,
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteSendAsync(chatId, message, proxy, cancellationToken);
            return new TrySendResult(true, null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Telegram send failed for chat {ChatId}. viaProxy={ViaProxy}",
                chatId,
                proxy is not null);
            return new TrySendResult(false, exception);
        }
    }

    private async Task<TelegramBotIdentity> ExecuteGetMeAsync(
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        using var handler = BuildHandler(proxy);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(_options.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.RequestTimeoutSeconds)),
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/bot{_options.BotToken}/getMe");
        var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Telegram getMe failed: {(int)response.StatusCode}; body={TrimError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            throw new InvalidOperationException("Telegram getMe returned ok=false.");
        }

        if (!document.RootElement.TryGetProperty("result", out var resultElement))
        {
            throw new InvalidOperationException("Telegram getMe response missing result.");
        }

        if (!resultElement.TryGetProperty("id", out var idElement))
        {
            throw new InvalidOperationException("Telegram getMe response missing bot id.");
        }

        var username = resultElement.TryGetProperty("username", out var usernameElement)
            ? usernameElement.GetString()
            : null;
        var firstName = resultElement.TryGetProperty("first_name", out var firstNameElement)
            ? firstNameElement.GetString()
            : null;

        return new TelegramBotIdentity(idElement.ToString(), username, firstName);
    }

    private async Task ExecuteSendAsync(
        string chatId,
        string message,
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        using var handler = BuildHandler(proxy);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(_options.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.RequestTimeoutSeconds)),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/bot{_options.BotToken}/sendMessage")
        {
            Content = JsonContent.Create(new
            {
                chat_id = chatId,
                text = message,
                disable_web_page_preview = true,
            }),
        };

        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Telegram send failed: {(int)response.StatusCode}; body={TrimError(body)}");
    }

    private void EnsureConfigured()
    {
        if (_options.Enabled && !string.IsNullOrWhiteSpace(_options.BotToken))
        {
            return;
        }

        throw new InvalidOperationException("Telegram notifications disabled or bot token missing.");
    }

    private static bool IsProxyTransportFailure(Exception? exception)
    {
        if (exception is null)
        {
            return false;
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
        {
            return true;
        }

        if (exception is HttpRequestException)
        {
            var message = exception.Message.ToLowerInvariant();
            return message.Contains("proxy", StringComparison.Ordinal)
                   || message.Contains("connect", StringComparison.Ordinal)
                   || message.Contains("tunnel", StringComparison.Ordinal)
                   || message.Contains("502", StringComparison.Ordinal)
                   || message.Contains("timed out", StringComparison.Ordinal);
        }

        return IsProxyTransportFailure(exception.InnerException);
    }

    private static bool IsTelegramApiRejection(Exception? exception)
    {
        if (exception is null)
        {
            return false;
        }

        if (exception is InvalidOperationException)
        {
            var message = exception.Message;
            return message.Contains("Telegram send failed:", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("Telegram getMe failed:", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("returned ok=false", StringComparison.OrdinalIgnoreCase);
        }

        return IsTelegramApiRejection(exception.InnerException);
    }

    private static HttpClientHandler BuildHandler(TelegramProxyRuntimeConfig? proxy)
    {
        var handler = new HttpClientHandler();
        if (proxy is null)
        {
            return handler;
        }

        var webProxy = new WebProxy(proxy.Uri);
        if (!string.IsNullOrWhiteSpace(proxy.Login))
        {
            webProxy.Credentials = new NetworkCredential(proxy.Login, proxy.Password);
        }

        handler.Proxy = webProxy;
        handler.UseProxy = true;
        return handler;
    }

    private static string? TrimError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }

    private sealed record TryGetMeResult(TelegramBotIdentity? Identity, Exception? Error)
    {
        public string? ErrorMessage => Error?.Message;
    }

    private sealed record TrySendResult(bool IsSuccess, Exception? Error)
    {
        public string? ErrorMessage => Error?.Message;
    }
}

public sealed record TelegramProxyRuntimeConfig(
    Uri Uri,
    string? Login,
    string? Password);

public sealed record TelegramBotIdentity(
    string BotId,
    string? Username,
    string? FirstName);

public sealed record TelegramConnectivityResult(
    string Status,
    bool ProxyAttempted,
    bool ProxySucceeded,
    bool DirectAttempted,
    bool DirectSucceeded,
    string EffectivePath,
    string? ReasonCode,
    string? ProxyError,
    string? DirectError,
    TelegramBotIdentity? Identity);

public sealed record TelegramSendResult(
    string Status,
    string? EffectivePath,
    string? ReasonCode,
    bool ProxyAttempted,
    bool ProxySucceeded,
    bool DirectAttempted,
    bool DirectSucceeded,
    string? ProxyError,
    string? DirectError,
    string? ErrorMessage);
