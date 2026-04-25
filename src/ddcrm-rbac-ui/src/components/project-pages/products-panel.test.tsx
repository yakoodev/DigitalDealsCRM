import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, waitFor } from "@testing-library/react";
import { describe, expect, it, beforeEach, vi } from "vitest";
import { ProjectProductsPanel } from "@/components/project-pages/products-panel";
import { runAccountActionRequest } from "@/lib/api-client";

const accountState = {
  selectedAccountId: "acc-1",
};

vi.mock("@/hooks/use-project-accounts", () => ({
  useProjectAccounts: () => ({
    accounts: [
      {
        id: "acc-1",
        projectId: "project-1",
        platform: "funpay",
        displayName: "Account 1",
        businessStatus: "active",
      },
      {
        id: "acc-2",
        projectId: "project-1",
        platform: "ggsell",
        displayName: "Account 2",
        businessStatus: "active",
      },
    ],
    selectedAccountId: accountState.selectedAccountId,
    selectedAccount: null,
    isLoading: false,
    error: null,
    queryKey: ["accounts", "http://localhost:5073", "token", "project-1"] as const,
    setSelectedAccountId: vi.fn(),
  }),
}));

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>(
    "@/lib/api-client",
  );
  return {
    ...actual,
    runAccountActionRequest: vi.fn(),
  };
});

function renderPanel() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectProductsPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
      />
    </QueryClientProvider>,
  );
}

describe("ProjectProductsPanel", () => {
  beforeEach(() => {
    accountState.selectedAccountId = "acc-1";
    vi.mocked(runAccountActionRequest).mockReset();
    vi.mocked(runAccountActionRequest).mockResolvedValue({
      items: [],
    });
  });

  it("автоматически загружает products.list при открытии и при смене аккаунта", async () => {
    const view = renderPanel();

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "products.list",
        { limit: 100 },
      );
    });

    accountState.selectedAccountId = "acc-2";
    view.rerender(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false } },
          })
        }
      >
        <ProjectProductsPanel
          apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
          projectId="project-1"
        />
      </QueryClientProvider>,
    );

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-2",
        "products.list",
        { limit: 100 },
      );
    });
  });
});
