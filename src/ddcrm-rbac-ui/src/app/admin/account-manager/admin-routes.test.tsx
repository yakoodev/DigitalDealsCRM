import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AccountManagerAdminOverviewPage from "@/app/admin/account-manager/page";
import AccountManagerIntegrationsPage from "@/app/admin/account-manager/integrations/page";
import AccountManagerServersPage from "@/app/admin/account-manager/servers/page";
import AccountManagerTemplatesPage from "@/app/admin/account-manager/templates/page";
import {
  checkAdminTelegramConnectivityRequest,
  listProjectsRequest,
  listAdminProjectIntegrationGrantsRequest,
  listAdminTelegramProxyProfilesRequest,
  listAdminAccountTypesRequest,
  listAdminWorkerServersRequest,
  revokeAdminProjectIntegrationGrantRequest,
  sendAdminTelegramTestMessageRequest,
  triggerAdminIntegrationRuntimeRequest,
  upsertAdminProjectIntegrationGrantRequest,
  upsertAdminAccountTypeRequest,
  upsertAdminTelegramProxyProfileRequest,
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
      authMode: "manual" as const,
      loggedInAt: new Date().toISOString(),
      systemPermissions: ["system.accountManager.manage", "system.integrations.manage"],
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
    listProjectsRequest: vi.fn(),
    listAdminAccountTypesRequest: vi.fn(),
    upsertAdminAccountTypeRequest: vi.fn(),
    listAdminProjectIntegrationGrantsRequest: vi.fn(),
    upsertAdminProjectIntegrationGrantRequest: vi.fn(),
    revokeAdminProjectIntegrationGrantRequest: vi.fn(),
    triggerAdminIntegrationRuntimeRequest: vi.fn(),
    listAdminTelegramProxyProfilesRequest: vi.fn(),
    upsertAdminTelegramProxyProfileRequest: vi.fn(),
    sendAdminTelegramTestMessageRequest: vi.fn(),
    checkAdminTelegramConnectivityRequest: vi.fn(),
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
        authMode: "manual",
        loggedInAt: new Date().toISOString(),
        systemPermissions: ["system.accountManager.manage", "system.integrations.manage"],
        isSystemAdmin: true,
      },
    };

    vi.mocked(listAdminWorkerServersRequest).mockReset();
    vi.mocked(upsertAdminWorkerServerRequest).mockReset();
    vi.mocked(listProjectsRequest).mockReset();
    vi.mocked(listAdminAccountTypesRequest).mockReset();
    vi.mocked(upsertAdminAccountTypeRequest).mockReset();
    vi.mocked(listAdminProjectIntegrationGrantsRequest).mockReset();
    vi.mocked(upsertAdminProjectIntegrationGrantRequest).mockReset();
    vi.mocked(revokeAdminProjectIntegrationGrantRequest).mockReset();
    vi.mocked(triggerAdminIntegrationRuntimeRequest).mockReset();
    vi.mocked(listAdminTelegramProxyProfilesRequest).mockReset();
    vi.mocked(upsertAdminTelegramProxyProfileRequest).mockReset();
    vi.mocked(sendAdminTelegramTestMessageRequest).mockReset();
    vi.mocked(checkAdminTelegramConnectivityRequest).mockReset();

    vi.mocked(listProjectsRequest).mockResolvedValue([]);
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
    vi.mocked(listAdminProjectIntegrationGrantsRequest).mockResolvedValue([
      {
        integrationKey: "funpaystat",
        integrationType: "service",
        status: "active",
        scopes: ["read", "jobs"],
        grantedAtUtc: new Date().toISOString(),
        credentialStatus: null,
        credentialMasked: null,
      },
    ]);
    vi.mocked(upsertAdminProjectIntegrationGrantRequest).mockResolvedValue({
      integrationKey: "steam-accounts-manager",
      integrationType: "worker",
      status: "active",
      scopes: ["read", "jobs"],
      grantedAtUtc: new Date().toISOString(),
      credentialStatus: null,
      credentialMasked: null,
    });
    vi.mocked(revokeAdminProjectIntegrationGrantRequest).mockResolvedValue({
      requestId: "req",
      status: "completed",
    });
    vi.mocked(triggerAdminIntegrationRuntimeRequest).mockResolvedValue({
      requestId: "req",
      status: "queued",
    });
    vi.mocked(listAdminTelegramProxyProfilesRequest).mockResolvedValue([
      {
        id: "11111111-1111-1111-1111-111111111111",
        name: "tg-proxy",
        proxyType: "http",
        host: "127.0.0.1",
        port: 8080,
        isActive: true,
        hasCredentials: true,
        updatedAtUtc: new Date().toISOString(),
      },
    ]);
    vi.mocked(upsertAdminTelegramProxyProfileRequest).mockResolvedValue({
      id: "11111111-1111-1111-1111-111111111111",
      name: "tg-proxy",
      proxyType: "http",
      host: "127.0.0.1",
      port: 8080,
      isActive: true,
      hasCredentials: true,
      updatedAtUtc: new Date().toISOString(),
    });
    vi.mocked(checkAdminTelegramConnectivityRequest).mockResolvedValue({
      status: "ok",
      effectivePath: "proxy",
      proxyAttempted: true,
      proxySucceeded: true,
      directAttempted: false,
      directSucceeded: false,
      botId: "123456789",
      username: "ddcrm_test_bot",
      firstName: "DDCRM Test Bot",
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

    renderWithQuery(<AccountManagerIntegrationsPage />);
    expect(
      screen.getByText("Недостаточно системных прав для управления integration bus."),
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

  it("integrations page выполняет grant/proxy операции", async () => {
    renderWithQuery(<AccountManagerIntegrationsPage />);

    await userEvent.type(screen.getByPlaceholderText("uuid проекта"), "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    await waitFor(() => {
      expect(listAdminProjectIntegrationGrantsRequest).toHaveBeenCalled();
      expect(listAdminTelegramProxyProfilesRequest).toHaveBeenCalled();
    });

    const steamPresetCard = screen.getByText("Steam Runtime", { selector: "strong" }).closest("li");
    expect(steamPresetCard).not.toBeNull();
    await userEvent.click(within(steamPresetCard!).getByRole("button", { name: "Заполнить форму" }));
    expect(screen.getByPlaceholderText("steam-accounts-manager")).toHaveValue("steam-accounts-manager");
    expect(screen.getByPlaceholderText("read,jobs")).toHaveValue("read,jobs");

    await userEvent.click(screen.getByRole("button", { name: "Выдать/обновить" }));
    await waitFor(() => {
      expect(upsertAdminProjectIntegrationGrantRequest).toHaveBeenCalled();
    });

    await userEvent.click(screen.getByRole("button", { name: "Сохранить proxy" }));
    await waitFor(() => {
      expect(upsertAdminTelegramProxyProfileRequest).toHaveBeenCalled();
    });

    await userEvent.type(screen.getByPlaceholderText("например: 123456789"), "123456789");
    await userEvent.click(screen.getByRole("button", { name: "Отправить тест в Telegram" }));
    await waitFor(() => {
      expect(sendAdminTelegramTestMessageRequest).toHaveBeenCalled();
    });

    await userEvent.click(screen.getByRole("button", { name: "Проверить Telegram API (getMe)" }));
    await waitFor(() => {
      expect(checkAdminTelegramConnectivityRequest).toHaveBeenCalled();
    });
  });

  it("integrations page показывает ошибку при отсутствии system.integrations.manage", async () => {
    sessionState.value = {
      ...sessionState.value,
      profile: {
        ...sessionState.value.profile,
        systemPermissions: ["system.accountManager.manage"],
        isSystemAdmin: true,
      },
    };

    renderWithQuery(<AccountManagerIntegrationsPage />);
    expect(screen.getByText("403 · Missing integrations permission")).toBeInTheDocument();
    expect(screen.getByText(/system\.integrations\.manage/)).toBeInTheDocument();
  });
});
