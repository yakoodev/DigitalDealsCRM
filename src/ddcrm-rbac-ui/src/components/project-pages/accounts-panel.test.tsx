import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectAccountsPanel } from "@/components/project-pages/accounts-panel";
import { runAccountActionRequest } from "@/lib/api-client";

const openModalMock = vi.fn();

vi.mock("@/hooks/use-route-modal", () => ({
  useRouteModal: () => ({
    modal: null,
    accountId: "",
    productId: "",
    conversationId: "",
    openModal: openModalMock,
    closeModal: vi.fn(),
  }),
}));

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
    ],
    selectedAccountId: "acc-1",
    selectedAccount: {
      id: "acc-1",
      projectId: "project-1",
      platform: "funpay",
      displayName: "Account 1",
      businessStatus: "active",
    },
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

function renderPanel(activeRole: "owner" | "admin" | "moderator" = "owner") {
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
      <ProjectAccountsPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        activeRole={activeRole}
      />
    </QueryClientProvider>,
  );
}

describe("ProjectAccountsPanel", () => {
  beforeEach(() => {
    openModalMock.mockReset();
    vi.mocked(runAccountActionRequest).mockReset();
    vi.mocked(runAccountActionRequest).mockResolvedValue({
      accountId: "acc-1",
      service: "funpay",
      nickname: "seller-main",
      status: "active",
    });
  });

  it("автоматически запрашивает account.info и умеет ручной refresh", async () => {
    renderPanel();

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "account.info",
        {},
      );
    });

    await userEvent.click(screen.getByRole("button", { name: "Обновить" }));

    await waitFor(() => {
      const accountInfoCalls = vi
        .mocked(runAccountActionRequest)
        .mock.calls.filter(([, , action]) => action === "account.info").length;
      expect(accountInfoCalls).toBeGreaterThan(1);
    });
  });

  it("показывает текст ошибки account.info для выбранного аккаунта", async () => {
    vi.mocked(runAccountActionRequest).mockRejectedValueOnce(
      new Error("UNAUTHORIZED: Сессия истекла или JWT невалиден."),
    );

    renderPanel();

    const errors = await screen.findAllByText(
      "UNAUTHORIZED: Сессия истекла или JWT невалиден.",
    );
    expect(errors.length).toBeGreaterThan(0);
  });

  it("для moderator скрывает lifecycle-операции аккаунта", async () => {
    renderPanel("moderator");

    expect(
      await screen.findByText("`moderator` работает только в режиме просмотра без lifecycle-операций."),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Управлять выбранным аккаунтом" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Добавить аккаунт" })).not.toBeInTheDocument();
  });
});
