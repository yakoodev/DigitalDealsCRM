import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AccountManagerAdminOverviewPage from "@/app/admin/account-manager/page";
import AccountManagerServersPage from "@/app/admin/account-manager/servers/page";
import AccountManagerTemplatesPage from "@/app/admin/account-manager/templates/page";
import {
  listAdminAccountTypesRequest,
  listAdminWorkerServersRequest,
  upsertAdminAccountTypeRequest,
  upsertAdminWorkerServerRequest,
} from "@/lib/api-client";

const logoutSpy = vi.fn();

const sessionState = {
  value: {
    token: "token",
    baseUrl: "http://localhost:5073",
    profile: {
      userId: "11111111-1111-1111-1111-111111111111",
      email: "owner@ddcrm.local",
      displayName: "Owner Demo",
      role: "owner" as const,
      authMode: "demo" as const,
      loggedInAt: new Date().toISOString(),
      systemPermissions: ["system.accountManager.manage"],
      isSystemAdmin: true,
    },
  },
};

vi.mock("@/components/layout/admin-layout", () => ({
  AdminLayout: ({ children }: { children: ReactNode }) => (
    <main data-testid="admin-layout">{children}</main>
  ),
}));

vi.mock("@/lib/use-session-guard", () => ({
  useSessionGuard: () => ({
    session: sessionState.value,
    logout: logoutSpy,
  }),
}));

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>("@/lib/api-client");
  return {
    ...actual,
    listAdminWorkerServersRequest: vi.fn(),
    upsertAdminWorkerServerRequest: vi.fn(),
    listAdminAccountTypesRequest: vi.fn(),
    upsertAdminAccountTypeRequest: vi.fn(),
  };
});

function renderWithQuery(ui: ReactNode) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("admin account-manager routes", () => {
  beforeEach(() => {
    logoutSpy.mockReset();
    sessionState.value = {
      token: "token",
      baseUrl: "http://localhost:5073",
      profile: {
        userId: "11111111-1111-1111-1111-111111111111",
        email: "owner@ddcrm.local",
        displayName: "Owner Demo",
        role: "owner",
        authMode: "demo",
        loggedInAt: new Date().toISOString(),
        systemPermissions: ["system.accountManager.manage"],
        isSystemAdmin: true,
      },
    };

    vi.mocked(listAdminWorkerServersRequest).mockReset();
    vi.mocked(upsertAdminWorkerServerRequest).mockReset();
    vi.mocked(listAdminAccountTypesRequest).mockReset();
    vi.mocked(upsertAdminAccountTypeRequest).mockReset();

    vi.mocked(listAdminWorkerServersRequest).mockResolvedValue([
      {
        serverId: "srv-default",
        baseUrlTemplate: "http://{workerId}:{workerPort}",
        status: "active",
        health: "healthy",
        capacity: 100,
        currentLoad: 2,
        dockerHost: "unix:///var/run/docker.sock",
        dockerNetwork: "ddcrm_ddcrm",
        registry: {
          enabled: true,
          host: "ghcr.io",
          username: "demo-user",
          hasToken: true,
          tokenUpdatedAtUtc: new Date().toISOString(),
        },
        metadata: {},
      },
    ]);
    vi.mocked(upsertAdminWorkerServerRequest).mockResolvedValue({
      serverId: "srv-default",
      baseUrlTemplate: "http://{workerId}:{workerPort}",
      status: "active",
      health: "healthy",
      capacity: 100,
      currentLoad: 2,
      dockerHost: "unix:///var/run/docker.sock",
      dockerNetwork: "ddcrm_ddcrm",
      registry: {
        enabled: true,
        host: "ghcr.io",
        username: "demo-user",
        hasToken: true,
        tokenUpdatedAtUtc: new Date().toISOString(),
      },
      metadata: {},
    });

    vi.mocked(listAdminAccountTypesRequest).mockResolvedValue([
      {
        accountTypeId: "test-worker.funpay",
        platform: "funpay",
        displayName: "Тестовый worker: FunPay",
        workerProfileId: "test-worker",
        enabled: true,
        sortOrder: 10,
        formFields: [],
        runtime: {
          autospawnEnabled: true,
          workerImage: "ddcrm/worker-api:local",
          workerPathPrefix: "/internal/v2/worker",
          healthPath: "/health",
          containerPort: 8080,
          environmentVariables: {
            TEST_WORKER_PROVIDER: "funpay",
          },
        },
      },
    ]);
    vi.mocked(upsertAdminAccountTypeRequest).mockResolvedValue({
      accountTypeId: "test-worker.funpay",
      platform: "funpay",
      displayName: "Тестовый worker: FunPay",
      workerProfileId: "test-worker",
      enabled: true,
      sortOrder: 10,
      formFields: [],
      runtime: {
        autospawnEnabled: true,
        workerImage: "ddcrm/worker-api:local",
        workerPathPrefix: "/internal/v2/worker",
        healthPath: "/health",
        containerPort: 8080,
        environmentVariables: {
          TEST_WORKER_PROVIDER: "funpay",
        },
      },
    });
  });

  it("без system-claim админ-роуты показывают 403", () => {
    sessionState.value = {
      ...sessionState.value,
      profile: {
        ...sessionState.value.profile,
        systemPermissions: [],
        isSystemAdmin: false,
      },
    };

    render(<AccountManagerAdminOverviewPage />);
    expect(screen.getByText("403 · System admin required")).toBeInTheDocument();

    renderWithQuery(<AccountManagerServersPage />);
    expect(
      screen.getByText("Недостаточно системных прав для управления worker servers."),
    ).toBeInTheDocument();

    renderWithQuery(<AccountManagerTemplatesPage />);
    expect(
      screen.getByText("Недостаточно системных прав для управления platform templates."),
    ).toBeInTheDocument();
  });

  it("servers page загружает registry и отправляет upsert", async () => {
    renderWithQuery(<AccountManagerServersPage />);

    await waitFor(() => {
      expect(listAdminWorkerServersRequest).toHaveBeenCalledWith({
        token: "token",
        baseUrl: "http://localhost:5073",
      });
    });
    await waitFor(() => {
      expect(screen.queryByText("Загрузка...")).not.toBeInTheDocument();
    });

    await userEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    await waitFor(() => {
      expect(upsertAdminWorkerServerRequest).toHaveBeenCalled();
    });
  });

  it("templates page загружает catalog и отправляет upsert", async () => {
    renderWithQuery(<AccountManagerTemplatesPage />);

    await waitFor(() => {
      expect(listAdminAccountTypesRequest).toHaveBeenCalledWith({
        token: "token",
        baseUrl: "http://localhost:5073",
      });
    });
    await waitFor(() => {
      expect(screen.queryByText("Загрузка...")).not.toBeInTheDocument();
    });

    await userEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    await waitFor(() => {
      expect(upsertAdminAccountTypeRequest).toHaveBeenCalled();
    });
  });
});
