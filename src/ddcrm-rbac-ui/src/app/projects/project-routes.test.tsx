import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import ProjectAccountCreateRoute from "@/app/projects/[projectId]/accounts/new/page";
import ProjectMessagesRoute from "@/app/projects/[projectId]/messages/page";
import ProjectProductsRoute from "@/app/projects/[projectId]/products/page";
import ProjectSchemasRoute from "@/app/projects/[projectId]/schemas/page";

const paramsState = {
  projectId: "project-77",
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
      authMode: "demo",
      loggedInAt: new Date().toISOString(),
    },
  },
};

vi.mock("next/navigation", () => ({
  useParams: () => paramsState,
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
vi.mock("@/components/project-pages/schemas-panel", () => ({
  ProjectSchemasPanel: () => <p>schemas-panel</p>,
}));
vi.mock("@/components/project-pages/account-create-panel", () => ({
  ProjectAccountCreatePanel: () => <p>account-create-panel</p>,
}));

describe("project route pages", () => {
  beforeEach(() => {
    sessionState.value = {
      token: "token",
      baseUrl: "http://localhost:5073",
      profile: {
        userId: "11111111-1111-1111-1111-111111111111",
        email: "owner@ddcrm.local",
        displayName: "Owner Demo",
        role: "owner",
        authMode: "demo",
        loggedInAt: new Date().toISOString(),
      },
    };
  });

  it("deeplink routes products/messages/schemas/accounts-new открываются при активной сессии", () => {
    render(<ProjectProductsRoute />);
    expect(screen.getByTestId("shell-products")).toBeInTheDocument();
    expect(screen.getByText("products-panel")).toBeInTheDocument();

    render(<ProjectMessagesRoute />);
    expect(screen.getByTestId("shell-messages")).toBeInTheDocument();
    expect(screen.getByText("messages-panel")).toBeInTheDocument();

    render(<ProjectSchemasRoute />);
    expect(screen.getByTestId("shell-schemas")).toBeInTheDocument();
    expect(screen.getByText("schemas-panel")).toBeInTheDocument();

    render(<ProjectAccountCreateRoute />);
    expect(screen.getByTestId("shell-accounts")).toBeInTheDocument();
    expect(screen.getByText("account-create-panel")).toBeInTheDocument();
  });

  it("без сессии показывает состояние guard", () => {
    sessionState.value = null as never;

    render(<ProjectProductsRoute />);
    expect(screen.getByText("Проверяем сессию...")).toBeInTheDocument();
  });
});
