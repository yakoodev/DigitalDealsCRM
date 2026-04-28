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
        },
      );
    });

    await waitFor(() => {
      expect(pushMock).toHaveBeenCalledWith("/projects/project-1/accounts");
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
