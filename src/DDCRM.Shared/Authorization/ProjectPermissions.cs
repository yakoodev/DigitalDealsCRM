namespace DDCRM.Shared.Authorization;

public static class ProjectPermissions
{
    public const string ProjectMembersInvite = "project.members.invite";
    public const string ProjectMembersRemove = "project.members.remove";
    public const string ProjectRolesChange = "project.roles.change";
    public const string ProjectAccountsLifecycleManage = "project.accounts.lifecycle.manage";
    public const string ProjectAccountsView = "project.accounts.view";
    public const string ProjectWorkersOperate = "project.workers.operate";
    public const string ProjectAccountsProxyCredentialsReveal = "project.accounts.proxyCredentials.reveal";
    public const string ProjectAccountsProxyCredentialsUpdate = "project.accounts.proxyCredentials.update";
    public const string ProjectBillingView = "project.billing.view";
    public const string ProjectBillingChangePlan = "project.billing.changePlan";
    public const string ProjectModulesOperate = "project.modules.operate";
}
