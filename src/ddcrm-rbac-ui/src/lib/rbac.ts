export const projectRoles = ["owner", "admin", "moderator"] as const;

export type ProjectRole = (typeof projectRoles)[number];

export const projectPermissions = {
  membersInvite: "project.members.invite",
  membersRemove: "project.members.remove",
  rolesChange: "project.roles.change",
  accountsLifecycleManage: "project.accounts.lifecycle.manage",
  accountsView: "project.accounts.view",
  workersOperate: "project.workers.operate",
  proxyCredentialsReveal: "project.accounts.proxyCredentials.reveal",
  proxyCredentialsUpdate: "project.accounts.proxyCredentials.update",
  billingView: "project.billing.view",
  billingChangePlan: "project.billing.changePlan",
  modulesOperate: "project.modules.operate",
} as const;

export type ProjectPermission =
  (typeof projectPermissions)[keyof typeof projectPermissions];

const permissionMatrix: Record<ProjectRole, ReadonlySet<ProjectPermission>> = {
  owner: new Set<ProjectPermission>([
    projectPermissions.membersInvite,
    projectPermissions.membersRemove,
    projectPermissions.rolesChange,
    projectPermissions.accountsLifecycleManage,
    projectPermissions.accountsView,
    projectPermissions.workersOperate,
    projectPermissions.proxyCredentialsReveal,
    projectPermissions.proxyCredentialsUpdate,
    projectPermissions.billingView,
    projectPermissions.billingChangePlan,
    projectPermissions.modulesOperate,
  ]),
  admin: new Set<ProjectPermission>([
    projectPermissions.membersInvite,
    projectPermissions.membersRemove,
    projectPermissions.rolesChange,
    projectPermissions.accountsLifecycleManage,
    projectPermissions.accountsView,
    projectPermissions.workersOperate,
    projectPermissions.proxyCredentialsReveal,
    projectPermissions.proxyCredentialsUpdate,
    projectPermissions.billingView,
    projectPermissions.billingChangePlan,
    projectPermissions.modulesOperate,
  ]),
  moderator: new Set<ProjectPermission>([
    projectPermissions.accountsView,
    projectPermissions.workersOperate,
    projectPermissions.modulesOperate,
  ]),
};

export function hasPermission(role: ProjectRole, permission: ProjectPermission) {
  return permissionMatrix[role].has(permission);
}
