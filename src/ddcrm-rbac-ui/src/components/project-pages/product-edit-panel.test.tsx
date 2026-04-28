import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProjectProductEditPanel } from "@/components/project-pages/product-edit-panel";
import { runAccountActionRequest } from "@/lib/api-client";

const pushSpy = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushSpy }),
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
      <ProjectProductEditPanel
        apiSession={{ baseUrl: "http://localhost:5073", token: "token" }}
        projectId="project-1"
        accountId="acc-1"
        productId="prod-1"
      />
    </QueryClientProvider>,
  );
}

describe("ProjectProductEditPanel", () => {
  beforeEach(() => {
    pushSpy.mockReset();
    vi.mocked(runAccountActionRequest).mockReset();
    vi.mocked(runAccountActionRequest).mockImplementation(
      async (_session, _accountId, action) => {
        if (action === "products.list") {
          return {
            items: [
              {
                productId: "prod-1",
                title: "Old Product",
                price: {
                  amount: 100,
                  currency: "RUB",
                },
              },
            ],
          };
        }

        return {
          productId: "prod-1",
          status: "active",
          version: "2",
        };
      },
    );
  });

  it("отправляет products.update в формате changes + WorkerV2Money", async () => {
    renderPanel();

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "products.list",
        { limit: 200 },
      );
    });

    await waitFor(() => {
      expect(screen.getByLabelText("Название")).toHaveValue("Old Product");
    });

    await userEvent.clear(screen.getByLabelText("Цена"));
    await userEvent.type(screen.getByLabelText("Цена"), "150");
    await userEvent.click(screen.getByRole("button", { name: "Сохранить изменения" }));

    await waitFor(() => {
      expect(runAccountActionRequest).toHaveBeenCalledWith(
        { baseUrl: "http://localhost:5073", token: "token" },
        "acc-1",
        "products.update",
        {
          productId: "prod-1",
          changes: {
            price: {
              amount: 150,
              currency: "RUB",
            },
          },
        },
      );
    });

    await waitFor(() => {
      expect(pushSpy).toHaveBeenCalledWith("/projects/project-1/products");
    });
  });
});
