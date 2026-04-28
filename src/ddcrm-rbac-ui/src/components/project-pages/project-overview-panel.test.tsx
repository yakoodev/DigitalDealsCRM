import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectOverviewPanel } from "@/components/project-pages/project-overview-panel";
import { runAccountActionRequest } from "@/lib/api-client";

vi.mock("@/hooks/use-project-accounts", () => ({
  useProjectAccounts: () => ({
    accounts: [
      {
        id: "acc-1",
        projectId: "project-1",
        platform: "funpay",
        displayName: "Account 1",
        businessStatus: "active",
        proxyConfigured: true,
      },
    ],
    selectedAccountId: "acc-1",
    selectedAccount: {
      id: "acc-1",
      projectId: "project-1",
      platform: "funpay",
      displayName: "Account 1",
      businessStatus: "active",
      proxyConfigured: true,
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
      <ProjectOverviewPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
      />
    </QueryClientProvider>,
  );
}

describe("ProjectOverviewPanel", () => {
  beforeEach(() => {
    vi.mocked(runAccountActionRequest).mockReset();
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, _accountId, action) => {
        if (action === "products.list") {
          return {
            items: [
              { id: "prod-1", title: "Product 1" },
              { id: "prod-2", title: "Product 2" },
            ],
          };
        }

        if (action === "conversations.list") {
          return {
            items: [{ id: "conv-1", unreadCount: 2 }],
          };
        }

        return {};
      },
    );
  });

  it("агрегирует KPI по товарам и перепискам из worker-операций", async () => {
    renderPanel();

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "products.list",
        { limit: 100 },
      );
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "conversations.list",
        { limit: 100 },
      );
    });

    expect(await screen.findByText("Товары: 2")).toBeInTheDocument();
    expect(screen.getByText("Переписки: 1")).toBeInTheDocument();
    expect(screen.getByText("Непрочитанные: 2")).toBeInTheDocument();
  });

  it("показывает warning при частичном сбое worker-данных", async () => {
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, _accountId, action) => {
        if (action === "products.list") {
          throw new Error("UNAUTHORIZED: Сессия истекла или JWT невалиден.");
        }

        if (action === "conversations.list") {
          return {
            items: [{ id: "conv-1", unreadCount: 1 }],
          };
        }

        return {};
      },
    );

    renderPanel();

    expect(await screen.findByText("Часть воркеров ответила с ошибкой")).toBeInTheDocument();
    expect(
      screen.getByText("UNAUTHORIZED: Сессия истекла или JWT невалиден."),
    ).toBeInTheDocument();
    expect(screen.getByText("Переписки: 1")).toBeInTheDocument();
  });

  it("кнопка обновления переопрашивает worker-операции", async () => {
    renderPanel();

    await waitFor(() => {
      expect(vi.mocked(runAccountActionRequest).mock.calls.length).toBeGreaterThanOrEqual(2);
    });

    const initialCalls = vi.mocked(runAccountActionRequest).mock.calls.length;
    await userEvent.click(screen.getByRole("button", { name: "Обновить данные" }));

    await waitFor(() => {
      expect(vi.mocked(runAccountActionRequest).mock.calls.length).toBeGreaterThan(initialCalls);
    });
  });
});
