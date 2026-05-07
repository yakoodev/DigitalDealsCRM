import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectMessagesPanel } from "@/components/project-pages/messages-panel";
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
      {
        id: "acc-2",
        projectId: "project-1",
        platform: "ggsell",
        displayName: "Account 2",
        businessStatus: "active",
      },
    ],
    selectedAccountId: "acc-1",
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
      mutations: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectMessagesPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
      />
    </QueryClientProvider>,
  );
}

describe("ProjectMessagesPanel", () => {
  beforeEach(() => {
    openModalMock.mockReset();
    vi.mocked(runAccountActionRequest).mockReset();
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, accountId, action) => {
        if (action === "conversations.list") {
          return {
            items: [
              {
                conversationId: accountId === "acc-2" ? "conv-2" : "conv-1",
                title: accountId === "acc-2" ? "Second marketplace chat" : "Support chat",
                preview: "Последнее сообщение",
              },
            ],
          };
        }

        return {};
      },
    );
  });

  it("автозагружает список переписок по всем аккаунтам и не грузит историю чата в обзорной странице", async () => {
    renderPanel();

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "conversations.list",
        { limit: 100 },
      );
    });

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-2",
        "conversations.list",
        { limit: 100 },
      );
    });

    const actions = vi.mocked(runAccountActionRequest).mock.calls.map((call) => call[2]);
    expect(actions).not.toContain("conversations.messages.list");
    expect(actions).not.toContain("conversations.messages.send");
  });

  it("строит route-bound открытие чата с accountId и conversationId", async () => {
    renderPanel();

    const button = await screen.findByTestId("open-thread-acc-1-conv-1");
    await userEvent.click(button);
    expect(openModalMock).toHaveBeenCalledWith("thread", {
      accountId: "acc-1",
      conversationId: "conv-1",
    });
  });

  it("оставляет список переписок доступным при частичном падении воркеров", async () => {
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, accountId, action) => {
        if (action !== "conversations.list") {
          return {};
        }

        if (accountId === "acc-2") {
          throw new Error("WORKER_UNAVAILABLE: gateway timeout");
        }

        return {
          items: [
            {
              conversationId: "conv-1",
              title: "Support chat",
              preview: "Последнее сообщение",
            },
          ],
        };
      },
    );

    renderPanel();

    expect(await screen.findByText("Support chat")).toBeInTheDocument();
    expect(await screen.findByText("Часть воркеров недоступна")).toBeInTheDocument();
    expect(await screen.findByText("WORKER_UNAVAILABLE: gateway timeout")).toBeInTheDocument();
  });

  it("использует peerName как заголовок диалога вместо Без названия", async () => {
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, _accountId, action) => {
        if (action !== "conversations.list") {
          return {};
        }

        return {
          items: [
            {
              conversationId: "conv-peer",
              peerName: "ayder211",
              lastMessagePreview: "Добрый вечер",
            },
          ],
        };
      },
    );

    renderPanel();

    expect((await screen.findAllByText("ayder211")).length).toBeGreaterThan(0);
    expect(screen.queryByText("Без названия")).not.toBeInTheDocument();
  });
});
