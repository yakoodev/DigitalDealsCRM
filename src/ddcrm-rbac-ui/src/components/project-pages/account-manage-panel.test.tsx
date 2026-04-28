import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectAccountManagePanel } from "@/components/project-pages/account-manage-panel";
import {
  deleteAccountRequest,
  getMaskedProxyCredentialsRequest,
  revealProxyCredentialsRequest,
  updateAccountRequest,
  updateProxyCredentialsRequest,
} from "@/lib/api-client";

const pushSpy = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: pushSpy,
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
    getMaskedProxyCredentialsRequest: vi.fn(),
    updateAccountRequest: vi.fn(),
    updateProxyCredentialsRequest: vi.fn(),
    revealProxyCredentialsRequest: vi.fn(),
    deleteAccountRequest: vi.fn(),
  };
});

function renderPanel(activeRole: "owner" | "admin" | "moderator" = "owner") {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectAccountManagePanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        accountId="acc-1"
        activeRole={activeRole}
      />
    </QueryClientProvider>,
  );
}

describe("ProjectAccountManagePanel", () => {
  beforeEach(() => {
    pushSpy.mockReset();
    vi.mocked(getMaskedProxyCredentialsRequest).mockReset();
    vi.mocked(updateAccountRequest).mockReset();
    vi.mocked(updateProxyCredentialsRequest).mockReset();
    vi.mocked(revealProxyCredentialsRequest).mockReset();
    vi.mocked(deleteAccountRequest).mockReset();

    vi.mocked(getMaskedProxyCredentialsRequest).mockResolvedValue({
      configured: true,
      hostMasked: "***.***.***.237",
      loginMasked: "u*****9",
    });
    vi.mocked(updateAccountRequest).mockResolvedValue({
      id: "acc-1",
      projectId: "project-1",
      platform: "funpay",
      displayName: "Updated account",
      businessStatus: "paused",
    });
    vi.mocked(updateProxyCredentialsRequest).mockResolvedValue({
      requestId: "req-1",
      status: "completed",
    });
    vi.mocked(revealProxyCredentialsRequest).mockResolvedValue({
      host: "45.88.208.237",
      port: 1508,
      login: "user305829",
      password: "oksbuf",
    });
    vi.mocked(deleteAccountRequest).mockResolvedValue({
      requestId: "req-2",
      status: "completed",
    });
  });

  it("обновляет displayName/businessStatus выбранного аккаунта", async () => {
    renderPanel();

    const displayNameInput = await screen.findByPlaceholderText("Название аккаунта");
    await userEvent.clear(displayNameInput);
    await userEvent.type(displayNameInput, "Updated account");
    await userEvent.selectOptions(
      screen.getByRole("combobox", { name: "Business status" }),
      "paused",
    );
    await userEvent.click(screen.getByRole("button", { name: "Сохранить параметры аккаунта" }));

    await waitFor(() => {
      expect(updateAccountRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "project-1",
        "acc-1",
        {
          displayName: "Updated account",
          businessStatus: "paused",
        },
      );
    });
  });

  it("обновляет proxy credentials и умеет reveal", async () => {
    renderPanel();

    await userEvent.type(screen.getByPlaceholderText("45.88.208.237"), "45.88.208.237");
    await userEvent.clear(screen.getByPlaceholderText("1508"));
    await userEvent.type(screen.getByPlaceholderText("1508"), "1508");
    await userEvent.type(screen.getByPlaceholderText("login"), "user305829");
    await userEvent.type(screen.getByPlaceholderText("password"), "oksbuf");
    await userEvent.click(screen.getByRole("button", { name: "Обновить proxy credentials" }));

    await waitFor(() => {
      expect(updateProxyCredentialsRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "project-1",
        "acc-1",
        {
          reason: "manual update from UI",
          proxyConfig: {
            host: "45.88.208.237",
            port: 1508,
            login: "user305829",
            password: "oksbuf",
          },
        },
      );
    });

    await userEvent.click(screen.getByRole("button", { name: "Показать полные credentials" }));

    await waitFor(() => {
      expect(revealProxyCredentialsRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "project-1",
        "acc-1",
        { reason: "manual diagnostics in UI" },
      );
    });
  });

  it("удаляет аккаунт после подтверждения", async () => {
    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);
    renderPanel();

    await userEvent.click(screen.getByRole("button", { name: "Удалить аккаунт" }));

    await waitFor(() => {
      expect(deleteAccountRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "project-1",
        "acc-1",
      );
    });

    confirmSpy.mockRestore();
  });

  it("для moderator показывает forbidden state и не даёт lifecycle-операций", () => {
    renderPanel("moderator");

    expect(
      screen.getByText("Недостаточно прав для изменения или удаления аккаунта."),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Удалить аккаунт" }),
    ).not.toBeInTheDocument();
  });
});
