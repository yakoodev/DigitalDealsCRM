import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ProjectOffersPanel } from "@/components/project-pages/project-offers-panel";

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
  it("показывает guard для moderator", () => {
    renderPanel();

    expect(screen.getByTestId("project-offers-panel-no-access")).toBeInTheDocument();
    expect(screen.getByText(/project.offers.manage/)).toBeInTheDocument();
  });
});
