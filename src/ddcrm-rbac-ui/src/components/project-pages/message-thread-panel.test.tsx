import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectMessageThreadPanel } from "@/components/project-pages/message-thread-panel";
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

function renderPanel(accountId = "acc-1", conversationId = "conv-1") {
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
      <ProjectMessageThreadPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        accountId={accountId}
        conversationId={conversationId}
      />
    </QueryClientProvider>,
  );
}

describe("ProjectMessageThreadPanel", () => {
  beforeEach(() => {
    vi.mocked(runAccountActionRequest).mockReset();
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, _accountId, action) => {
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
          return {
            status: "ok",
          };
        }

        return {};
      },
    );
  });

  it("автозагружает историю чата для выбранной переписки", async () => {
    renderPanel();

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

    expect(await screen.findByText("Привет!")).toBeInTheDocument();
  });

  it("отправляет сообщение и инвалидацией перезагружает историю", async () => {
    renderPanel();
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
      const messageListCalls = vi
        .mocked(runAccountActionRequest)
        .mock.calls.filter(([, , action]) => action === "conversations.messages.list").length;
      expect(messageListCalls).toBeGreaterThan(1);
    });
  });

  it("показывает понятную ошибку если не переданы параметры переписки", () => {
    renderPanel("", "");

    expect(screen.getByText("Не указан `accountId` или `conversationId`.")).toBeInTheDocument();
    expect(runAccountActionRequest).not.toHaveBeenCalled();
  });

  it("при одинаковых timestamp рендерит новые сообщения снизу по messageId", async () => {
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, _accountId, action) => {
        if (action === "conversations.messages.list") {
          return {
            items: [
              { messageId: "200", createdAt: "2026-05-07T19:48:54.577782Z", direction: "in", text: "новое сообщение" },
              { messageId: "100", createdAt: "2026-05-07T19:48:54.577782Z", direction: "in", text: "старое сообщение" },
            ],
          };
        }

        if (action === "conversations.messages.send") {
          return { status: "ok" };
        }

        return {};
      },
    );

    renderPanel();
    await screen.findByText("старое сообщение");

    const messageRows = Array.from(document.querySelectorAll(".chat-list .chat-item p"))
      .map((node) => node.textContent?.trim());

    expect(messageRows).toEqual([
      "старое сообщение",
      "новое сообщение",
    ]);
  });
});
