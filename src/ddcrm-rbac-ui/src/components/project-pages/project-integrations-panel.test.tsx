import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectIntegrationsPanel } from "@/components/project-pages/project-integrations-panel";
import {
  listProjectCustomHttpIntegrationsRequest,
  listProjectIntegrationsStatusRequest,
} from "@/lib/api-client";

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>("@/lib/api-client");
  return {
    ...actual,
    createProjectCustomHttpIntegrationRequest: vi.fn(),
    createTelegramLinkCodeRequest: vi.fn(),
    deleteProjectCustomHttpIntegrationRequest: vi.fn(),
    invokeProjectIntegrationActionRequest: vi.fn(),
    listProjectCustomHttpIntegrationsRequest: vi.fn(),
    listProjectIntegrationsStatusRequest: vi.fn(),
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

    vi.mocked(listProjectIntegrationsStatusRequest).mockResolvedValue({
      items: [],
      telegram: { groupChats: 0, userDmChats: 0 },
    });
    vi.mocked(listProjectCustomHttpIntegrationsRequest).mockResolvedValue([]);
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
          integrationKey: "custom-http",
          integrationType: "custom",
          status: "active",
          scopes: ["use"],
          credentialStatus: null,
          credentialMasked: null,
          runtimeStatus: null,
          runtimeAccountId: null,
          runtimeLastError: null,
        },
      ],
      telegram: { groupChats: 0, userDmChats: 0 },
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
