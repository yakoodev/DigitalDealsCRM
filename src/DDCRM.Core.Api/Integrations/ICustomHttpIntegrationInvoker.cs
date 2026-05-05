namespace DDCRM.Core.Api.Integrations;

public interface ICustomHttpIntegrationInvoker
{
    Task<CustomHttpInvokeResult> InvokeAsync(
        Guid projectId,
        Guid integrationId,
        CustomHttpInvokeRequest request,
        CancellationToken cancellationToken);
}

public sealed record CustomHttpInvokeRequest(
    string Method,
    string? RelativePath,
    IReadOnlyDictionary<string, string>? Headers,
    string? BodyJson);

public sealed record CustomHttpInvokeResult(
    int StatusCode,
    string? Body,
    IReadOnlyDictionary<string, string> Headers,
    string Endpoint);
