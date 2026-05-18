import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectSteamPanel } from "@/components/project-pages/project-steam-panel";
import {
  createProjectIntegrationInstanceUiSessionRequest,
  listProjectIntegrationInstancesRequest,
  listProjectIntegrationsStatusRequest,
} from "@/lib/api-client";

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>("@/lib/api-client");
  return {
    ...actual,
    createProjectIntegrationInstanceUiSessionRequest: vi.fn(),
    listProjectIntegrationInstancesRequest: vi.fn(),
    listProjectIntegrationsStatusRequest: vi.fn(),
  };
});

function renderPanel() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectSteamPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
      />
    </QueryClientProvider>,
  );
}

describe("ProjectSteamPanel", () => {
  beforeEach(() => {
    vi.mocked(listProjectIntegrationsStatusRequest).mockReset();
    vi.mocked(listProjectIntegrationInstancesRequest).mockReset();
    vi.mocked(createProjectIntegrationInstanceUiSessionRequest).mockReset();

    vi.mocked(listProjectIntegrationsStatusRequest).mockResolvedValue({
      items: [
        {
          integrationKey: "steam-accounts-manager",
          integrationType: "worker",
          status: "active",
          scopes: ["read", "jobs"],
          maxInstances: 1,
          credentialStatus: null,
          credentialMasked: null,
          runtimeStatus: "active",
          runtimeAccountId: "00000000-0000-0000-0000-000000000111",
          runtimeLastError: null,
        },
      ],
      telegram: { groupChats: 0, userDmChats: 0 },
    });

    vi.mocked(listProjectIntegrationInstancesRequest).mockResolvedValue({
      integrationKey: "steam-accounts-manager",
      maxInstances: 1,
      items: [
        {
          instanceId: "11111111-1111-1111-1111-111111111111",
          integrationKey: "steam-accounts-manager",
          displayName: "steam-default",
          isDefault: true,
          runtimeAccountId: "00000000-0000-0000-0000-000000000111",
          runtimeStatus: "active",
          runtimeLastError: null,
          configurationUpdatedAtUtc: null,
          provisionedAtUtc: null,
          deprovisionedAtUtc: null,
          createdAtUtc: new Date().toISOString(),
          updatedAtUtc: new Date().toISOString(),
        },
      ],
    });

    vi.mocked(createProjectIntegrationInstanceUiSessionRequest).mockResolvedValue({
      token: "token",
      expiresAtUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
      iframeUrl: "/projects/project-1/integrations/steam-accounts-manager/11111111-1111-1111-1111-111111111111?uiToken=token",
    });
  });

  it("загружает iframe-сессию и рендерит embedded UI", async () => {
    renderPanel();

    await waitFor(() => {
      expect(createProjectIntegrationInstanceUiSessionRequest).toHaveBeenCalled();
    });

    const iframe = await screen.findByTitle("Steam integration UI");
    expect(iframe).toHaveAttribute(
      "src",
      "http://localhost:5073/projects/project-1/integrations/steam-accounts-manager/11111111-1111-1111-1111-111111111111?uiToken=token",
    );
  });

  it("показывает подсказку, если grant не найден", async () => {
    vi.mocked(listProjectIntegrationsStatusRequest).mockResolvedValue({
      items: [],
      telegram: { groupChats: 0, userDmChats: 0 },
    });
    vi.mocked(listProjectIntegrationInstancesRequest).mockResolvedValue({
      integrationKey: "steam-accounts-manager",
      maxInstances: 1,
      items: [],
    });

    renderPanel();

    await waitFor(() => {
      expect(screen.getByText(/Для проекта не найден grant/i)).toBeInTheDocument();
    });
  });
});
