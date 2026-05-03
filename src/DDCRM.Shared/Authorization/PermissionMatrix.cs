namespace DDCRM.Shared.Authorization;

public static class PermissionMatrix
{
    private static readonly Dictionary<string, HashSet<string>> Map = new(StringComparer.Ordinal)
    {
        [ProjectPermissions.ProjectMembersInvite] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectMembersRemove] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectRolesChange] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectAccountsLifecycleManage] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectAccountsView] = [ProjectRoles.Owner, ProjectRoles.Admin, ProjectRoles.Moderator],
        [ProjectPermissions.ProjectWorkersOperate] = [ProjectRoles.Owner, ProjectRoles.Admin, ProjectRoles.Moderator],
        [ProjectPermissions.ProjectAccountsProxyCredentialsReveal] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectAccountsProxyCredentialsUpdate] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectBillingView] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectBillingChangePlan] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectModulesOperate] = [ProjectRoles.Owner, ProjectRoles.Admin, ProjectRoles.Moderator],
        [ProjectPermissions.ProjectIntegrationsUse] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectOffersManage] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectWorkflowsManage] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectWorkflowsRun] = [ProjectRoles.Owner, ProjectRoles.Admin],
        [ProjectPermissions.ProjectIntegrationsCustomManage] = [ProjectRoles.Owner, ProjectRoles.Admin],
    };

    public static bool HasPermission(string role, string permission)
    {
        if (!Map.TryGetValue(permission, out var allowedRoles))
        {
            return false;
        }

        return allowedRoles.Contains(role);
    }
}
