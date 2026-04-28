using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.Integrations;

public sealed class TelegramNotificationSender(
    IOptions<TelegramNotificationOptions> options,
    ILogger<TelegramNotificationSender> logger)
{
    private readonly TelegramNotificationOptions _options = options.Value;

    public async Task SendMessageAsync(
        string chatId,
        string message,
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotToken))
        {
            throw new InvalidOperationException("Telegram notifications disabled or bot token missing.");
        }

        using var handler = BuildHandler(proxy);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(_options.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(25),
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
        logger.LogWarning(
            "Telegram send failed for chat {ChatId}. Status={StatusCode}; Body={Body}",
            chatId,
            (int)response.StatusCode,
            body);
        throw new InvalidOperationException($"Telegram send failed: {(int)response.StatusCode}");
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
}

public sealed record TelegramProxyRuntimeConfig(
    Uri Uri,
    string? Login,
    string? Password);
