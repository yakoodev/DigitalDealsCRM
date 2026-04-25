import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, beforeEach, vi } from "vitest";
import { ProjectMessagesPanel } from "@/components/project-pages/messages-panel";
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

        if (action === "conversations.messages.list") {
          return {
            items: [
              {
                direction: "in",
                text: "Привет!",
              },
            ],
          };
        }

        if (action === "conversations.messages.send") {
          return { status: "ok" };
        }

        return {};
      },
    );
  });

  it("автозагружает список переписок по всем аккаунтам и не грузит историю до выбора переписки", async () => {
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

    expect(runAccountActionRequest).not.toHaveBeenCalledWith(
      { baseUrl: "http://localhost:5073", token: "token" },
      "acc-1",
      "conversations.messages.list",
      expect.anything(),
    );

    await userEvent.click(
      await screen.findByRole("button", {
        name: /Support chat/i,
      }),
    );

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "conversations.messages.list",
        {
          conversationId: "conv-1",
          limit: 200,
        },
      );
    });
  });

  it("отправляет сообщение и инвалидацией перезагружает чат и список переписок", async () => {
    renderPanel();

    await userEvent.click(
      await screen.findByRole("button", {
        name: /Support chat/i,
      }),
    );
    await screen.findByText("Привет!");

    await userEvent.type(screen.getByPlaceholderText("Введите сообщение"), "Тест");
    await userEvent.click(screen.getByRole("button", { name: "Отправить сообщение" }));

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "conversations.messages.send",
        {
          conversationId: "conv-1",
          text: "Тест",
        },
      );
    });

    await waitFor(() => {
      const conversationsCalls = vi
        .mocked(runAccountActionRequest)
        .mock.calls.filter(([, , action]) => action === "conversations.list").length;
      const messagesCalls = vi
        .mocked(runAccountActionRequest)
        .mock.calls.filter(([, , action]) => action === "conversations.messages.list")
        .length;

      expect(conversationsCalls).toBeGreaterThan(1);
      expect(messagesCalls).toBeGreaterThan(1);
    });
  });
});
