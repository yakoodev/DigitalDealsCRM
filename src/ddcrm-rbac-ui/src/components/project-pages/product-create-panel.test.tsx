import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectProductCreatePanel } from "@/components/project-pages/product-create-panel";
import { runAccountActionRequest } from "@/lib/api-client";

const pushSpy = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushSpy }),
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
      <ProjectProductCreatePanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        preferredAccountId="acc-1"
      />
    </QueryClientProvider>,
  );
}

describe("ProjectProductCreatePanel", () => {
  beforeEach(() => {
    pushSpy.mockReset();
    vi.mocked(runAccountActionRequest).mockReset();
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, _accountId, action) => {
        if (action === "products.schemas.list") {
          return {
            items: [
              {
                schemaId: "digital_goods.v1",
                provider: "funpay",
                title: "Digital Goods",
                fields: [
                  { key: "title", type: "string", required: true },
                  { key: "price.amount", type: "number", required: true },
                  { key: "price.currency", type: "string", required: true },
                ],
              },
            ],
          };
        }

        return {
          productId: "prod-1",
          status: "active",
          version: "1",
        };
      },
    );
  });

  it("создаёт товар с payload WorkerV2Money и schemaId", async () => {
    renderPanel();

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "products.schemas.list",
        {},
      );
    });

    await userEvent.clear(screen.getByLabelText("Название"));
    await userEvent.type(screen.getByLabelText("Название"), "Super Product");
    await userEvent.clear(screen.getByLabelText("Цена"));
    await userEvent.type(screen.getByLabelText("Цена"), "199");

    await userEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "products.create",
        {
          schemaId: "digital_goods.v1",
          title: "Super Product",
          price: {
            amount: 199,
            currency: "RUB",
          },
          status: "active",
        },
      );
    });

    await waitFor(() => {
      expect(pushSpy).toHaveBeenCalledWith("/projects/project-1/products");
    });
  });
});
