import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectIntegrationsPanel } from "@/components/project-pages/project-integrations-panel";
import {
  listProjectCustomHttpIntegrationsRequest,
  listProjectIntegrationInstancesRequest,
  listProjectIntegrationsStatusRequest,
} from "@/lib/api-client";

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
  }),
}));

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>("@/lib/api-client");
  return {
    ...actual,
    createProjectCustomHttpIntegrationRequest: vi.fn(),
    createTelegramLinkCodeRequest: vi.fn(),
    deleteProjectCustomHttpIntegrationRequest: vi.fn(),
    deleteProjectIntegrationInstanceRequest: vi.fn(),
    invokeProjectIntegrationActionRequest: vi.fn(),
    listProjectCustomHttpIntegrationsRequest: vi.fn(),
    listProjectIntegrationInstancesRequest: vi.fn(),
    listProjectIntegrationsStatusRequest: vi.fn(),
    createProjectIntegrationInstanceRequest: vi.fn(),
    triggerProjectIntegrationInstanceRuntimeRequest: vi.fn(),
    updateProjectIntegrationInstanceRequest: vi.fn(),
    testProjectCustomHttpIntegrationRequest: vi.fn(),
    triggerProjectIntegrationRuntimeRequest: vi.fn(),
    updateProjectCustomHttpIntegrationRequest: vi.fn(),
  };
});

function renderPanel(role: "owner" | "admin" | "moderator") {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectIntegrationsPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        currentRole={role}
      />
    </QueryClientProvider>,
  );
}

describe("ProjectIntegrationsPanel custom HTTP", () => {
  beforeEach(() => {
    vi.mocked(listProjectIntegrationsStatusRequest).mockReset();
    vi.mocked(listProjectCustomHttpIntegrationsRequest).mockReset();
    vi.mocked(listProjectIntegrationInstancesRequest).mockReset();

    vi.mocked(listProjectIntegrationsStatusRequest).mockResolvedValue({
      items: [],
      telegram: { groupChats: 0, userDmChats: 0 },
    });
    vi.mocked(listProjectCustomHttpIntegrationsRequest).mockResolvedValue([]);
    vi.mocked(listProjectIntegrationInstancesRequest).mockResolvedValue({
      integrationKey: "steam-accounts-manager",
      maxInstances: 1,
      items: [],
    });
  });

  it("скрывает custom-http управление без permission", async () => {
    renderPanel("moderator");

    await waitFor(() => {
      expect(screen.getByText(/Недостаточно прав: требуется permission/)).toBeInTheDocument();
    });
  });

  it("показывает custom-http список при активном grant", async () => {
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
          runtimeAccountId: "00000000-0000-0000-0000-000000000000",
          runtimeLastError: null,
        },
        {
          integrationKey: "custom-http",
          integrationType: "custom",
          status: "active",
          scopes: ["use"],
          maxInstances: 1,
          credentialStatus: null,
          credentialMasked: null,
          runtimeStatus: null,
          runtimeAccountId: null,
          runtimeLastError: null,
        },
      ],
      telegram: { groupChats: 0, userDmChats: 0 },
    });
    vi.mocked(listProjectIntegrationInstancesRequest).mockResolvedValue({
      integrationKey: "steam-accounts-manager",
      maxInstances: 1,
      items: [],
    });

    vi.mocked(listProjectCustomHttpIntegrationsRequest).mockResolvedValue([
      {
        id: "integration-1",
        name: "Fulfillment API",
        baseUrl: "https://example.com",
        status: "active",
        bearerTokenMasked: "***2345",
        lastTestedAtUtc: null,
        updatedAtUtc: new Date().toISOString(),
      },
    ]);

    renderPanel("owner");

    await waitFor(() => {
      expect(screen.getByText("Fulfillment API")).toBeInTheDocument();
    });
  });
});
