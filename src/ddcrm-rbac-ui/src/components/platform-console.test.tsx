import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { PlatformConsole } from "./platform-console";
import type { Account, Project } from "@/generated/external-api";
import type { PlatformSession } from "@/lib/auth";
import * as apiClient from "@/lib/api-client";

vi.mock("@/lib/api-client", () => ({
  addProjectMemberRequest: vi.fn(),
  buildRouteKey: vi.fn((accountId: string) => `rk.${accountId.replaceAll("-", "")}`),
  changeProjectMemberRoleRequest: vi.fn(),
  changePlanRequest: vi.fn(),
  createAccountRequest: vi.fn(),
  createPaymentRequest: vi.fn(),
  createProjectRequest: vi.fn(),
  getMaskedProxyCredentialsRequest: vi.fn(),
  listAccountsRequest: vi.fn(),
  listProjectsRequest: vi.fn(),
  proxyAccountActionRequest: vi.fn(),
  purchaseAddonRequest: vi.fn(),
  revealProxyCredentialsRequest: vi.fn(),
  removeProjectMemberRequest: vi.fn(),
  updateProxyCredentialsRequest: vi.fn(),
}));

const defaultSession: PlatformSession = {
  token: "jwt-token",
  baseUrl: "http://localhost:5073",
  profile: {
    userId: "11111111-1111-1111-1111-111111111111",
    email: "owner@ddcrm.local",
    displayName: "Owner Demo",
    role: "owner",
    authMode: "manual",
    loggedInAt: "2026-04-25T00:00:00.000Z",
  },
};

const projectsFixture: Project[] = [
  { id: "project-alpha", name: "Alpha Project", status: "active" },
  { id: "project-beta", name: "Beta Project", status: "active" },
];

const accountsFixture: Account[] = [
  {
    id: "account-001",
    projectId: "project-alpha",
    platform: "funpay",
    displayName: "Main FunPay Account",
    businessStatus: "active",
    proxyConfigured: true,
    proxyHostMasked: "45.88.xxx.xxx",
    proxyLoginMasked: "user***",
  },
];

function renderConsole(session: PlatformSession = defaultSession) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
      mutations: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <PlatformConsole
        session={session}
        onSessionChange={vi.fn()}
        onLogout={vi.fn()}
      />
    </QueryClientProvider>,
  );
}

describe("PlatformConsole", () => {
  const listProjectsRequestMock = vi.mocked(apiClient.listProjectsRequest);
  const listAccountsRequestMock = vi.mocked(apiClient.listAccountsRequest);
  const createProjectRequestMock = vi.mocked(apiClient.createProjectRequest);
  const createAccountRequestMock = vi.mocked(apiClient.createAccountRequest);
  const getMaskedProxyCredentialsRequestMock = vi.mocked(
    apiClient.getMaskedProxyCredentialsRequest,
  );

  beforeEach(() => {
    listProjectsRequestMock.mockResolvedValue(projectsFixture);
    listAccountsRequestMock.mockResolvedValue(accountsFixture);
    createProjectRequestMock.mockResolvedValue({
      id: "project-gamma",
      name: "Gamma QA Project",
      status: "active",
    });
    createAccountRequestMock.mockResolvedValue({
      id: "account-created",
      projectId: "project-alpha",
      platform: "ggsell",
      displayName: "Created Account",
      businessStatus: "active",
    });
    getMaskedProxyCredentialsRequestMock.mockResolvedValue({
      configured: true,
      hostMasked: "45.88.xxx.xxx",
      loginMasked: "user***",
    });
  });

  it("показывает список проектов и позволяет открыть проект с вкладками", async () => {
    const user = userEvent.setup();

    renderConsole();

    expect(await screen.findByTestId("project-list")).toBeInTheDocument();
    expect(screen.getByTestId("project-item-project-alpha")).toHaveTextContent(
      "Alpha Project",
    );
    expect(screen.getByTestId("project-item-project-beta")).toHaveTextContent(
      "Beta Project",
    );

    await user.click(screen.getByTestId("project-item-project-beta"));

    await waitFor(() => {
      expect(screen.getByTestId("opened-project-name")).toHaveTextContent("Beta Project");
      expect(screen.getByTestId("project-tab-products")).toBeInTheDocument();
      expect(screen.getByTestId("project-tab-messages")).toBeInTheDocument();
      expect(screen.getByTestId("accounts-list-panel")).toBeInTheDocument();
    });
  });

  it("создаёт проект через форму", async () => {
    const user = userEvent.setup();

    renderConsole();

    const input = await screen.findByTestId("create-project-name-input");
    await user.clear(input);
    await user.type(input, "Новый проект для тестов");
    await user.click(screen.getByTestId("create-project-button"));

    await waitFor(() => {
      expect(createProjectRequestMock).toHaveBeenCalledWith(
        expect.objectContaining({
          token: defaultSession.token,
          baseUrl: defaultSession.baseUrl,
        }),
        "Новый проект для тестов",
      );
      expect(screen.getByTestId("platform-status-message")).toHaveTextContent(
        'Проект "Gamma QA Project" создан.',
      );
    });
  });

  it("добавляет аккаунт в активный проект", async () => {
    const user = userEvent.setup();
    listAccountsRequestMock.mockResolvedValue([]);

    renderConsole();

    expect(await screen.findByTestId("create-account-panel")).toBeInTheDocument();
    await user.clear(screen.getByTestId("create-account-platform-input"));
    await user.type(screen.getByTestId("create-account-platform-input"), "playerok");
    await user.clear(screen.getByTestId("create-account-display-name-input"));
    await user.type(screen.getByTestId("create-account-display-name-input"), "Playerok QA");
    await user.type(screen.getByTestId("create-account-proxy-host-input"), "45.88.208.237");
    await user.clear(screen.getByTestId("create-account-proxy-port-input"));
    await user.type(screen.getByTestId("create-account-proxy-port-input"), "1508");
    await user.type(screen.getByTestId("create-account-proxy-login-input"), "user305829");
    await user.type(screen.getByTestId("create-account-proxy-password-input"), "oksbuf");
    await user.click(screen.getByTestId("create-account-button"));

    await waitFor(() => {
      expect(createAccountRequestMock).toHaveBeenCalledWith(
        expect.objectContaining({
          token: defaultSession.token,
          baseUrl: defaultSession.baseUrl,
        }),
        "project-alpha",
        {
          platform: "playerok",
          displayName: "Playerok QA",
          proxyConfig: {
            host: "45.88.208.237",
            port: 1508,
            login: "user305829",
            password: "oksbuf",
          },
        },
      );
      expect(screen.getByTestId("platform-status-message")).toHaveTextContent(
        'Аккаунт "Created Account" добавлен в проект.',
      );
    });
  });
});
