using DDCRM.Core.Api.AccountsManager;

namespace DDCRM.Core.Api.Integrations;

internal sealed record IntegrationWorkerRuntimeConfiguration(
    ProxyConfigPayload ProxyConfig,
    AccountsManagerMailConfig? MailConfig);
