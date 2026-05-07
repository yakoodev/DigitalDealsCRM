import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectOffersPanel } from "@/components/project-pages/project-offers-panel";
import { listOffersRequest } from "@/lib/api-client";

vi.mock("@/hooks/use-project-accounts", () => ({
  useProjectAccounts: () => ({
    accounts: [],
    selectedAccountId: "",
    selectedAccount: null,
    isLoading: false,
    error: null,
    queryKey: ["accounts"] as const,
    setSelectedAccountId: vi.fn(),
  }),
}));

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>("@/lib/api-client");
  return {
    ...actual,
    listOffersRequest: vi.fn(),
    createOfferRequest: vi.fn(),
    replaceOfferVariantsRequest: vi.fn(),
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

function renderPanel() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectOffersPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        currentRole="moderator"
      />
    </QueryClientProvider>,
  );
}

describe("ProjectOffersPanel permissions", () => {
  beforeEach(() => {
    vi.mocked(listOffersRequest).mockReset();
    vi.mocked(listOffersRequest).mockResolvedValue([]);
  });

  it("показывает guard для moderator", () => {
    renderPanel();

    expect(screen.getByTestId("project-offers-panel-no-access")).toBeInTheDocument();
    expect(screen.getByText(/project.offers.manage/)).toBeInTheDocument();
  });

  it("во вкладке flow использует ReactFlow canvas для owner", async () => {
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

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <ProjectOffersPanel
          apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
          projectId="project-1"
          currentRole="owner"
        />
      </QueryClientProvider>,
    );

    await waitFor(() => {
      expect(listOffersRequest).toHaveBeenCalled();
    });

    fireEvent.click(screen.getByRole("button", { name: "Flow editor" }));

    expect(await screen.findByText("Workflow canvas")).toBeInTheDocument();
    expect(screen.getByText("Node palette")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Сохранить draft/ })).toBeInTheDocument();
  });
});
