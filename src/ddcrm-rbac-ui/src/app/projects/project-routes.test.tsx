import { render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import ProjectOverviewRoute from "@/app/projects/[projectId]/page";
import ProjectAccountCreateRoute from "@/app/projects/[projectId]/accounts/new/page";
import ProjectAccountManageRoute from "@/app/projects/[projectId]/accounts/manage/page";
import ProjectAccountsRoute from "@/app/projects/[projectId]/accounts/page";
import ProjectMessagesRoute from "@/app/projects/[projectId]/messages/page";
import ProjectMessageThreadRoute from "@/app/projects/[projectId]/messages/thread/page";
import ProjectProductEditRoute from "@/app/projects/[projectId]/products/edit/page";
import ProjectProductCreateRoute from "@/app/projects/[projectId]/products/new/page";
import ProjectProductsRoute from "@/app/projects/[projectId]/products/page";
import ProjectSchemasRoute from "@/app/projects/[projectId]/schemas/page";

const paramsState = {
  projectId: "project-77",
};

const replaceSpy = vi.fn();
const searchParamsState = {
  value: new URLSearchParams("accountId=acc-1&productId=prod-1&conversationId=conv-1"),
};

const sessionState = {
  value: {
    token: "token",
    baseUrl: "http://localhost:5073",
    profile: {
      userId: "11111111-1111-1111-1111-111111111111",
      email: "owner@ddcrm.local",
      displayName: "Owner Demo",
      role: "owner",
      authMode: "manual",
      loggedInAt: new Date().toISOString(),
    },
  },
};

vi.mock("next/navigation", () => ({
  useParams: () => paramsState,
  useRouter: () => ({ replace: replaceSpy }),
  useSearchParams: () => searchParamsState.value,
}));

vi.mock("@/lib/use-session-guard", () => ({
  useSessionGuard: () => ({
    session: sessionState.value,
    logout: vi.fn(),
  }),
}));

vi.mock("@/components/project-shell", () => ({
  ProjectShell: ({
    projectId,
    activeTab,
    children,
  }: {
    projectId: string;
    activeTab: string;
    children: (ctx: unknown) => ReactNode;
  }) => (
    <div data-testid={`shell-${activeTab}`}>
      <p>{projectId}</p>
      {children({
        apiSession: { baseUrl: "http://localhost:5073", token: "token" },
        project: {
          id: projectId,
          name: "Project",
          status: "active",
        },
        projects: [],
      })}
    </div>
  ),
}));

vi.mock("@/components/project-pages/products-panel", () => ({
  ProjectProductsPanel: () => <p>products-panel</p>,
}));
vi.mock("@/components/project-pages/messages-panel", () => ({
  ProjectMessagesPanel: () => <p>messages-panel</p>,
}));
vi.mock("@/components/project-pages/accounts-panel", () => ({
  ProjectAccountsPanel: () => <p>accounts-panel</p>,
}));
vi.mock("@/components/project-pages/project-overview-panel", () => ({
  ProjectOverviewPanel: () => <p>overview-panel</p>,
}));

describe("project route pages", () => {
  beforeEach(() => {
    replaceSpy.mockReset();
    searchParamsState.value = new URLSearchParams(
      "accountId=acc-1&productId=prod-1&conversationId=conv-1",
    );
    sessionState.value = {
      token: "token",
      baseUrl: "http://localhost:5073",
      profile: {
        userId: "11111111-1111-1111-1111-111111111111",
        email: "owner@ddcrm.local",
        displayName: "Owner Demo",
        role: "owner",
        authMode: "manual",
        loggedInAt: new Date().toISOString(),
      },
    };
  });

  it("project routes overview/accounts/products/messages открываются при активной сессии", () => {
    render(<ProjectOverviewRoute />);
    expect(screen.getByTestId("shell-overview")).toBeInTheDocument();
    expect(screen.getByText("overview-panel")).toBeInTheDocument();

    render(<ProjectAccountsRoute />);
    expect(screen.getByTestId("shell-accounts")).toBeInTheDocument();
    expect(screen.getByText("accounts-panel")).toBeInTheDocument();

    render(<ProjectProductsRoute />);
    expect(screen.getByTestId("shell-products")).toBeInTheDocument();
    expect(screen.getByText("products-panel")).toBeInTheDocument();

    render(<ProjectMessagesRoute />);
    expect(screen.getByTestId("shell-messages")).toBeInTheDocument();
    expect(screen.getByText("messages-panel")).toBeInTheDocument();
  });

  it("legacy operation routes выполняют redirect в modal URL", async () => {
    render(<ProjectAccountCreateRoute />);
    render(<ProjectAccountManageRoute />);
    render(<ProjectProductCreateRoute />);
    render(<ProjectProductEditRoute />);
    render(<ProjectMessageThreadRoute />);

    await waitFor(() => {
      expect(replaceSpy).toHaveBeenCalledWith("/projects/project-77/accounts?modal=create");
      expect(replaceSpy).toHaveBeenCalledWith(
        "/projects/project-77/accounts?modal=manage&accountId=acc-1",
      );
      expect(replaceSpy).toHaveBeenCalledWith(
        "/projects/project-77/products?modal=create&accountId=acc-1",
      );
      expect(replaceSpy).toHaveBeenCalledWith(
        "/projects/project-77/products?modal=edit&accountId=acc-1&productId=prod-1",
      );
      expect(replaceSpy).toHaveBeenCalledWith(
        "/projects/project-77/messages?modal=thread&accountId=acc-1&conversationId=conv-1",
      );
    });
  });

  it("legacy schemas route выполняет redirect на products", async () => {
    render(<ProjectSchemasRoute />);

    await waitFor(() => {
      expect(replaceSpy).toHaveBeenCalledWith("/projects/project-77/products");
    });
  });

  it("без сессии показывает состояние guard", () => {
    sessionState.value = null as never;

    render(<ProjectProductsRoute />);
    expect(screen.getByText("Проверяем сессию...")).toBeInTheDocument();
  });
});
