import { describe, expect, it } from "vitest";
import {
  hasPermission,
  projectPermissions,
  projectRoles,
  type ProjectPermission,
  type ProjectRole,
} from "./rbac";

const allPermissions = Object.values(projectPermissions);

function expectRoleHasAllPermissions(role: ProjectRole) {
  for (const permission of allPermissions) {
    expect(hasPermission(role, permission)).toBe(true);
  }
}

describe("rbac matrix", () => {
  it("owner has all permissions", () => {
    expectRoleHasAllPermissions("owner");
  });

  it("admin has all permissions", () => {
    expectRoleHasAllPermissions("admin");
  });

  it("moderator has only allowed operational permissions", () => {
    const allowed: ProjectPermission[] = [
      projectPermissions.accountsView,
      projectPermissions.workersOperate,
      projectPermissions.modulesOperate,
    ];

    const denied: ProjectPermission[] = allPermissions.filter(
      (permission) => !allowed.includes(permission),
    );

    for (const permission of allowed) {
      expect(hasPermission("moderator", permission)).toBe(true);
    }

    for (const permission of denied) {
      expect(hasPermission("moderator", permission)).toBe(false);
    }
  });

  it("every role has a deterministic answer for every permission", () => {
    for (const role of projectRoles) {
      for (const permission of allPermissions) {
        expect(typeof hasPermission(role, permission)).toBe("boolean");
      }
    }
  });
});
