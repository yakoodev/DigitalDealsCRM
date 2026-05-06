import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectAccountCreatePanel } from "@/components/project-pages/account-create-panel";
import {
  createAccountRequest,
  listProjectAccountTypesRequest,
} from "@/lib/api-client";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: pushMock,
  }),
}));

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>(
    "@/lib/api-client",
  );
  return {
    ...actual,
    listProjectAccountTypesRequest: vi.fn(),
    createAccountRequest: vi.fn(),
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
      <ProjectAccountCreatePanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        activeRole={activeRole}
      />
    </QueryClientProvider>,
  );
}

describe("ProjectAccountCreatePanel", () => {
  beforeEach(() => {
    pushMock.mockReset();
    vi.mocked(listProjectAccountTypesRequest).mockReset();
    vi.mocked(createAccountRequest).mockReset();

    vi.mocked(listProjectAccountTypesRequest).mockResolvedValue([
      {
        accountTypeId: "test-worker.funpay",
        platform: "funpay",
        displayName: "Тестовый worker: FunPay",
        description: "Единственный доступный тип аккаунта на текущем этапе.",
        workerProfileId: "test-worker",
        enabled: true,
        sortOrder: 10,
        formFields: [
          {
            key: "displayName",
            label: "Название аккаунта",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "FunPay Test Account",
            defaultValue: "FunPay Test Account",
          },
          {
            key: "proxyHost",
            label: "Proxy host",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "45.88.208.237",
            defaultValue: "",
          },
          {
            key: "proxyPort",
            label: "Proxy port",
            inputType: "number",
            required: true,
            secret: false,
            placeholder: "1508",
            defaultValue: "1508",
          },
          {
            key: "proxyLogin",
            label: "Proxy login",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "user305829",
            defaultValue: "",
          },
          {
            key: "proxyPassword",
            label: "Proxy password",
            inputType: "password",
            required: true,
            secret: true,
            placeholder: "Введите пароль",
            defaultValue: "",
          },
          {
            key: "funpayGoldenKey",
            label: "FunPay golden_key",
            inputType: "password",
            required: true,
            secret: true,
            placeholder: "Введите golden_key аккаунта FunPay",
            defaultValue: "",
          },
          {
            key: "funpayUserAgent",
            label: "FunPay user agent",
            inputType: "text",
            required: false,
            secret: false,
            placeholder: "Опционально: браузерный User-Agent",
            defaultValue: "",
          },
        ],
      },
    ]);

    vi.mocked(createAccountRequest).mockResolvedValue({
      id: "acc-created",
      projectId: "project-1",
      platform: "funpay",
      displayName: "QA FunPay",
      businessStatus: "active",
      proxyConfigured: true,
      proxyHostMasked: "45***37",
      proxyLoginMasked: "us***29",
    });
  });

  it("загружает catalog и создаёт аккаунт через выбранный accountTypeId", async () => {
    renderPanel();

    const titles = await screen.findAllByText("Тестовый worker: FunPay");
    expect(titles.length).toBeGreaterThan(0);

    await userEvent.clear(screen.getByLabelText(/Название аккаунта/i));
    await userEvent.type(screen.getByLabelText(/Название аккаунта/i), "QA FunPay");
    await userEvent.type(screen.getByLabelText(/Proxy host/i), "45.88.208.237");
    await userEvent.type(screen.getByLabelText(/Proxy login/i), "user305829");
    await userEvent.type(screen.getByLabelText(/Proxy password/i), "oksbuf");
    await userEvent.type(screen.getByLabelText(/FunPay golden_key/i), "golden-key-123");

    await userEvent.click(
      screen.getByRole("button", { name: "Добавить аккаунт в проект" }),
    );

    await waitFor(() => {
      expect(createAccountRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "project-1",
        {
          accountTypeId: "test-worker.funpay",
          platform: "funpay",
          displayName: "QA FunPay",
          proxyConfig: {
            host: "45.88.208.237",
            port: 1508,
            login: "user305829",
            password: "oksbuf",
          },
          marketplaceAuth: {
            scheme: "golden_key",
            credentials: {
              golden_key: "golden-key-123",
            },
          },
        },
      );
    });

    await waitFor(() => {
      expect(pushMock).toHaveBeenCalledWith("/projects/project-1/accounts");
    });
  });

  it("для playerok формирует marketplaceAuth по scheme=tokens", async () => {
    vi.mocked(listProjectAccountTypesRequest).mockResolvedValueOnce([
      {
        accountTypeId: "test-worker.playerok",
        platform: "playerok",
        displayName: "Тестовый worker: Playerok",
        description: "Playerok template",
        workerProfileId: "test-worker",
        enabled: true,
        sortOrder: 20,
        formFields: [
          {
            key: "displayName",
            label: "Название аккаунта",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "Playerok Test Account",
            defaultValue: "Playerok Test Account",
          },
          {
            key: "proxyHost",
            label: "Proxy host",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "45.88.208.237",
            defaultValue: "",
          },
          {
            key: "proxyPort",
            label: "Proxy port",
            inputType: "number",
            required: true,
            secret: false,
            placeholder: "1508",
            defaultValue: "1508",
          },
          {
            key: "proxyLogin",
            label: "Proxy login",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "user305829",
            defaultValue: "",
          },
          {
            key: "proxyPassword",
            label: "Proxy password",
            inputType: "password",
            required: true,
            secret: true,
            placeholder: "Введите пароль",
            defaultValue: "",
          },
          {
            key: "playerokAuthScheme",
            label: "Playerok auth scheme",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "tokens или cookies",
            defaultValue: "tokens",
          },
          {
            key: "playerokToken",
            label: "Playerok token",
            inputType: "password",
            required: false,
            secret: true,
            placeholder: "Обязательно для scheme=tokens",
            defaultValue: "",
          },
          {
            key: "playerokDdg5",
            label: "Playerok ddg5",
            inputType: "password",
            required: false,
            secret: true,
            placeholder: "Cookie __ddg5_ (обязательно для scheme=tokens)",
            defaultValue: "",
          },
          {
            key: "playerokCookies",
            label: "Playerok cookies",
            inputType: "password",
            required: false,
            secret: true,
            placeholder: "Обязательно для scheme=cookies",
            defaultValue: "",
          },
          {
            key: "playerokUserAgent",
            label: "Playerok user agent",
            inputType: "text",
            required: false,
            secret: false,
            placeholder: "Опционально: браузерный User-Agent",
            defaultValue: "",
          },
        ],
      },
    ]);

    renderPanel();

    const titles = await screen.findAllByText("Тестовый worker: Playerok");
    expect(titles.length).toBeGreaterThan(0);

    await userEvent.clear(screen.getByLabelText(/Название аккаунта/i));
    await userEvent.type(screen.getByLabelText(/Название аккаунта/i), "QA Playerok");
    await userEvent.type(screen.getByLabelText(/Proxy host/i), "45.88.208.238");
    await userEvent.type(screen.getByLabelText(/Proxy login/i), "user-playerok");
    await userEvent.type(screen.getByLabelText(/Proxy password/i), "proxy-secret");
    await userEvent.type(screen.getByLabelText(/Playerok token/i), "token-value");
    await userEvent.type(screen.getByLabelText(/Playerok ddg5/i), "ddg5-value");

    await userEvent.click(
      screen.getByRole("button", { name: "Добавить аккаунт в проект" }),
    );

    await waitFor(() => {
      expect(createAccountRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "project-1",
        {
          accountTypeId: "test-worker.playerok",
          platform: "playerok",
          displayName: "QA Playerok",
          proxyConfig: {
            host: "45.88.208.238",
            port: 1508,
            login: "user-playerok",
            password: "proxy-secret",
          },
          marketplaceAuth: {
            scheme: "tokens",
            credentials: {
              token: "token-value",
              ddg5: "ddg5-value",
            },
          },
        },
      );
    });
  });

  it("для steam формирует mailConfig (IMAP)", async () => {
    vi.mocked(listProjectAccountTypesRequest).mockResolvedValueOnce([
      {
        accountTypeId: "test-worker.steam",
        platform: "steam",
        displayName: "Тестовый worker: Steam",
        description: "Steam template",
        workerProfileId: "test-worker",
        enabled: true,
        sortOrder: 30,
        formFields: [
          {
            key: "displayName",
            label: "Название аккаунта",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "Steam Test Account",
            defaultValue: "Steam Test Account",
          },
          {
            key: "proxyHost",
            label: "Proxy host",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "45.88.208.237",
            defaultValue: "",
          },
          {
            key: "proxyPort",
            label: "Proxy port",
            inputType: "number",
            required: true,
            secret: false,
            placeholder: "1508",
            defaultValue: "1508",
          },
          {
            key: "proxyLogin",
            label: "Proxy login",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "user305829",
            defaultValue: "",
          },
          {
            key: "proxyPassword",
            label: "Proxy password",
            inputType: "password",
            required: true,
            secret: true,
            placeholder: "Введите пароль",
            defaultValue: "",
          },
          {
            key: "imapHost",
            label: "IMAP host",
            inputType: "text",
            required: false,
            secret: false,
            placeholder: "imap.mail.local",
            defaultValue: "",
          },
          {
            key: "imapPort",
            label: "IMAP port",
            inputType: "number",
            required: false,
            secret: false,
            placeholder: "993",
            defaultValue: "993",
          },
          {
            key: "imapSecurity",
            label: "IMAP security",
            inputType: "text",
            required: false,
            secret: false,
            placeholder: "ssl",
            defaultValue: "ssl",
          },
          {
            key: "imapUsername",
            label: "IMAP username",
            inputType: "text",
            required: false,
            secret: false,
            placeholder: "steam@mail.local",
            defaultValue: "",
          },
          {
            key: "imapPassword",
            label: "IMAP password",
            inputType: "password",
            required: false,
            secret: true,
            placeholder: "mail secret",
            defaultValue: "",
          },
          {
            key: "imapMailbox",
            label: "IMAP mailbox",
            inputType: "text",
            required: false,
            secret: false,
            placeholder: "INBOX",
            defaultValue: "INBOX",
          },
          {
            key: "imapSearchFrom",
            label: "IMAP searchFrom",
            inputType: "text",
            required: false,
            secret: false,
            placeholder: "noreply@steampowered.com",
            defaultValue: "",
          },
          {
            key: "imapSearchSubject",
            label: "IMAP searchSubject",
            inputType: "text",
            required: false,
            secret: false,
            placeholder: "Steam",
            defaultValue: "",
          },
        ],
      },
    ]);

    renderPanel();

    const titles = await screen.findAllByText("Тестовый worker: Steam");
    expect(titles.length).toBeGreaterThan(0);

    await userEvent.clear(screen.getByLabelText(/Название аккаунта/i));
    await userEvent.type(screen.getByLabelText(/Название аккаунта/i), "QA Steam");
    await userEvent.type(screen.getByLabelText(/Proxy host/i), "45.88.208.239");
    await userEvent.type(screen.getByLabelText(/Proxy login/i), "user-steam");
    await userEvent.type(screen.getByLabelText(/Proxy password/i), "proxy-secret");
    await userEvent.type(screen.getByLabelText(/IMAP host/i), "imap.mail.local");
    await userEvent.type(screen.getByLabelText(/IMAP username/i), "steam@mail.local");
    await userEvent.type(screen.getByLabelText(/IMAP password/i), "mail-secret");
    await userEvent.type(screen.getByLabelText(/IMAP searchFrom/i), "noreply@steampowered.com");
    await userEvent.type(screen.getByLabelText(/IMAP searchSubject/i), "Steam");

    await userEvent.click(
      screen.getByRole("button", { name: "Добавить аккаунт в проект" }),
    );

    await waitFor(() => {
      expect(createAccountRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "project-1",
        {
          accountTypeId: "test-worker.steam",
          platform: "steam",
          displayName: "QA Steam",
          proxyConfig: {
            host: "45.88.208.239",
            port: 1508,
            login: "user-steam",
            password: "proxy-secret",
          },
          mailConfig: {
            enabled: true,
            imapHost: "imap.mail.local",
            imapPort: 993,
            imapSecurity: "ssl",
            imapUsername: "steam@mail.local",
            imapPassword: "mail-secret",
            mailbox: "INBOX",
            searchFrom: "noreply@steampowered.com",
            searchSubject: "Steam",
          },
        },
      );
    });
  });

  it("для moderator показывает запрет на создание аккаунта", () => {
    renderPanel("moderator");

    expect(
      screen.getByText("Недостаточно прав для добавления аккаунтов в проект."),
    ).toBeInTheDocument();
    expect(listProjectAccountTypesRequest).not.toHaveBeenCalled();
    expect(createAccountRequest).not.toHaveBeenCalled();
  });
});
