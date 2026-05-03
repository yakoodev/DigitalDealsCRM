import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectWorkflowsPanel } from "@/components/project-pages/project-workflows-panel";
import {
  getOfferWorkflowDraftRequest,
  listOfferWorkflowExecutionsRequest,
  listOffersRequest,
  publishOfferWorkflowRequest,
  saveOfferWorkflowDraftRequest,
} from "@/lib/api-client";

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>("@/lib/api-client");
  return {
    ...actual,
    getOfferWorkflowDraftRequest: vi.fn(),
    listOfferWorkflowExecutionsRequest: vi.fn(),
    listOffersRequest: vi.fn(),
    publishOfferWorkflowRequest: vi.fn(),
    saveOfferWorkflowDraftRequest: vi.fn(),
  };
});

if (typeof window !== "undefined" && typeof window.ResizeObserver === "undefined") {
  class ResizeObserverMock {
    observe() {}

    disconnect() {}

    unobserve() {}
  }

  Object.defineProperty(window, "ResizeObserver", {
    configurable: true,
    writable: true,
    value: ResizeObserverMock,
  });
}

function renderPanel(role: "owner" | "admin" | "moderator" = "owner") {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectWorkflowsPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        currentRole={role}
      />
    </QueryClientProvider>,
  );
}

describe("ProjectWorkflowsPanel", () => {
  beforeEach(() => {
    vi.mocked(listOffersRequest).mockReset();
    vi.mocked(getOfferWorkflowDraftRequest).mockReset();
    vi.mocked(listOfferWorkflowExecutionsRequest).mockReset();
    vi.mocked(saveOfferWorkflowDraftRequest).mockReset();
    vi.mocked(publishOfferWorkflowRequest).mockReset();

    vi.mocked(listOffersRequest).mockResolvedValue([
      {
        id: "offer-1",
        name: "Offer 1",
        description: null,
        status: "active",
        minPrice: 100,
        maxPrice: 120,
        averagePrice: 110,
        currencies: ["RUB"],
        variantCount: 2,
        variants: [],
        createdAtUtc: new Date().toISOString(),
        updatedAtUtc: new Date().toISOString(),
      },
    ]);

    vi.mocked(getOfferWorkflowDraftRequest).mockResolvedValue({
      draft: {
        version: "v1",
        maxSteps: 100,
        maxDurationSeconds: 120,
        maxRetries: 3,
        nodes: [
          {
            id: "purchase-start",
            type: "PurchaseStart",
            name: "Покупка",
            config: {},
            ui: {
              position: {
                x: 40,
                y: 80,
              },
            },
          },
          {
            id: "cond-1",
            type: "Condition",
            name: "Проверка платформы",
            config: {
              field: "platform",
              equals: "steam",
            },
            ui: {
              position: {
                x: 120,
                y: 80,
              },
            },
          },
          {
            id: "end-1",
            type: "End",
            config: {},
            ui: {
              position: {
                x: 400,
                y: 220,
              },
            },
          },
        ],
        edges: [
          {
            id: "edge-1",
            source: "purchase-start",
            sourceHandle: "out-flow",
            target: "cond-1",
            targetHandle: "in-flow",
          },
          {
            id: "edge-2",
            source: "cond-1",
            sourceHandle: "out-next",
            target: "end-1",
            targetHandle: "in-flow",
          },
        ],
        ui: {
          viewport: {
            x: -50,
            y: 20,
            zoom: 1.1,
          },
          entryNodeId: "purchase-start",
        },
      },
      status: "draft",
      publishedVersion: 0,
      publishedAtUtc: null,
    });

    vi.mocked(listOfferWorkflowExecutionsRequest).mockResolvedValue([
      {
        id: "exec-1",
        sourceOrderId: "order-1",
        workflowVersion: 1,
        status: "completed",
        startedAtUtc: new Date().toISOString(),
        finishedAtUtc: new Date().toISOString(),
        lastError: null,
        steps: [
          {
            nodeId: "cond-1",
            nodeType: "Condition",
            stepIndex: 1,
            status: "completed",
            startedAtUtc: new Date().toISOString(),
            finishedAtUtc: new Date().toISOString(),
            outputJson: "{}",
            error: null,
          },
        ],
      },
    ]);

    vi.mocked(saveOfferWorkflowDraftRequest).mockResolvedValue({
      draft: {
        version: "v1",
        maxSteps: 100,
        maxDurationSeconds: 120,
        maxRetries: 3,
        nodes: [],
        edges: [],
      },
      status: "draft",
      publishedVersion: 0,
      publishedAtUtc: null,
    });

    vi.mocked(publishOfferWorkflowRequest).mockResolvedValue({
      draft: {
        version: "v1",
        maxSteps: 100,
        maxDurationSeconds: 120,
        maxRetries: 3,
        nodes: [],
        edges: [],
      },
      status: "published",
      publishedVersion: 1,
      publishedAtUtc: new Date().toISOString(),
    });
  });

  it("показывает guard для moderator", () => {
    renderPanel("moderator");

    expect(screen.getByTestId("project-workflows-panel-no-access")).toBeInTheDocument();
    expect(screen.getByText(/project.workflows.manage/)).toBeInTheDocument();
  });

  it("typed-редактор обновляет config и save отправляет node/draft ui layout", async () => {
    renderPanel("owner");

    await waitFor(() => {
      expect(screen.getByText("Workflow canvas")).toBeInTheDocument();
      expect(screen.getByText("Проверка платформы")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText("Проверка платформы"));
    const fieldInput = screen.getByPlaceholderText("platform");
    fireEvent.change(fieldInput, { target: { value: "buyerSegment" } });

    const equalsInput = screen.getByPlaceholderText("steam");
    fireEvent.change(equalsInput, { target: { value: "vip" } });

    fireEvent.click(screen.getByRole("button", { name: "Применить поля" }));
    fireEvent.click(screen.getByRole("button", { name: /Сохранить draft/ }));

    await waitFor(() => {
      expect(saveOfferWorkflowDraftRequest).toHaveBeenCalledTimes(1);
    });

    const payload = vi.mocked(saveOfferWorkflowDraftRequest).mock.calls[0]?.[3];
    expect(payload).toBeTruthy();
    const conditionNode = payload?.nodes?.find((node) => node.id === "cond-1");
    expect(conditionNode?.config).toMatchObject({
      field: "buyerSegment",
      equals: "vip",
    });
    expect(conditionNode?.ui?.position).toMatchObject({
      x: 120,
      y: 80,
    });
    expect(payload?.ui?.viewport).toMatchObject({
      x: -50,
      y: 20,
      zoom: 1.1,
    });
    expect(payload?.ui?.entryNodeId).toBe("purchase-start");
  });

  it("drawer истории запусков открывается и рендерит шаги execution", async () => {
    renderPanel("owner");

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "История запусков" })).toBeEnabled();
    });

    fireEvent.click(screen.getByRole("button", { name: "История запусков" }));

    await waitFor(() => {
      expect(document.querySelector(".workflow-history-layer")).not.toBeNull();
      expect(screen.getByText(/order: order-1/)).toBeInTheDocument();
      expect(screen.getByText(/1. Condition/)).toBeInTheDocument();
    });
  });
});
