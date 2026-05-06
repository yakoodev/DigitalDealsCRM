using DDCRM.AccountsManager.Api.RouteRegistry;
using DDCRM.AccountsManager.Api.Worker;
using DDCRM.Shared.Errors;
using Microsoft.AspNetCore.Http;

namespace DDCRM.AccountsManager.Api.Tests.Infrastructure;

public sealed class RecordingWorkerControlClient : IWorkerControlClient
{
    private bool _failNextApply;

    public List<WorkerControlApplyCall> ApplyCalls { get; } = [];

    public List<WorkerControlMarketplaceAuthApplyCall> MarketplaceAuthApplyCalls { get; } = [];

    public List<WorkerControlMailConfigApplyCall> MailConfigApplyCalls { get; } = [];

    public Task ApplyProxyCredentialsAsync(
        WorkerBindingDto workerBinding,
        Guid accountId,
        Dictionary<string, object?> proxyConfig,
        string idempotencyKey,
        string? baseUrlTemplateOverride,
        CancellationToken cancellationToken)
    {
        if (_failNextApply)
        {
            _failNextApply = false;
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Simulated worker control failure.");
        }

        ApplyCalls.Add(new WorkerControlApplyCall(
            workerBinding,
            accountId,
            new Dictionary<string, object?>(proxyConfig, StringComparer.Ordinal),
            idempotencyKey,
            baseUrlTemplateOverride));

        return Task.CompletedTask;
    }

    public Task ApplyMarketplaceAuthAsync(
        WorkerBindingDto workerBinding,
        Guid accountId,
        MarketplaceAuthPayload marketplaceAuth,
        string idempotencyKey,
        string? baseUrlTemplateOverride,
        CancellationToken cancellationToken)
    {
        if (_failNextApply)
        {
            _failNextApply = false;
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Simulated worker control failure.");
        }

        MarketplaceAuthApplyCalls.Add(new WorkerControlMarketplaceAuthApplyCall(
            workerBinding,
            accountId,
            new MarketplaceAuthPayload(
                marketplaceAuth.Scheme,
                new Dictionary<string, string>(marketplaceAuth.Credentials, StringComparer.Ordinal)),
            idempotencyKey,
            baseUrlTemplateOverride));

        return Task.CompletedTask;
    }

    public Task ApplyMailConfigAsync(
        WorkerBindingDto workerBinding,
        Guid projectId,
        Guid accountId,
        MailConfigPayload mailConfig,
        string idempotencyKey,
        string? baseUrlTemplateOverride,
        CancellationToken cancellationToken)
    {
        if (_failNextApply)
        {
            _failNextApply = false;
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Simulated worker control failure.");
        }

        MailConfigApplyCalls.Add(new WorkerControlMailConfigApplyCall(
            workerBinding,
            projectId,
            accountId,
            new MailConfigPayload(
                mailConfig.Enabled,
                mailConfig.ImapHost,
                mailConfig.ImapPort,
                mailConfig.ImapSecurity,
                mailConfig.ImapUsername,
                mailConfig.ImapPassword,
                mailConfig.Mailbox,
                mailConfig.SearchFrom,
                mailConfig.SearchSubject),
            idempotencyKey,
            baseUrlTemplateOverride));

        return Task.CompletedTask;
    }

    public void FailNextApplyRequest() => _failNextApply = true;

    public void Reset()
    {
        _failNextApply = false;
        ApplyCalls.Clear();
        MarketplaceAuthApplyCalls.Clear();
        MailConfigApplyCalls.Clear();
    }
}

public sealed record WorkerControlApplyCall(
    WorkerBindingDto WorkerBinding,
    Guid AccountId,
    IReadOnlyDictionary<string, object?> ProxyConfig,
    string IdempotencyKey,
    string? BaseUrlTemplateOverride);

public sealed record WorkerControlMarketplaceAuthApplyCall(
    WorkerBindingDto WorkerBinding,
    Guid AccountId,
    MarketplaceAuthPayload MarketplaceAuth,
    string IdempotencyKey,
    string? BaseUrlTemplateOverride);

public sealed record WorkerControlMailConfigApplyCall(
    WorkerBindingDto WorkerBinding,
    Guid ProjectId,
    Guid AccountId,
    MailConfigPayload MailConfig,
    string IdempotencyKey,
    string? BaseUrlTemplateOverride);
