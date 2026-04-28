import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ProjectShell } from "@/components/project-shell";
import * as apiClient from "@/lib/api-client";
import type { PlatformSession } from "@/lib/auth";

const pushSpy = vi.fn();
const replaceSpy = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: pushSpy,
    replace: replaceSpy,
  }),
}));

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>(
    "@/lib/api-client",
  );

  return {
    ...actual,
    listProjectsRequest: vi.fn(),
    listAccountsRequest: vi.fn(),
  };
});

const session: PlatformSession = {
  token: "token",
  baseUrl: "http://localhost:5073",
  profile: {
    userId: "11111111-1111-1111-1111-111111111111",
    email: "owner@ddcrm.local",
    displayName: "Owner Demo",
    role: "owner",
    authMode: "demo",
    loggedInAt: "2026-04-26T00:00:00.000Z",
  },
};

function renderShell() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectShell
        session={session}
        projectId="project-1"
        activeTab="accounts"
        onLogout={vi.fn()}
      >
        {() => <p>child-rendered</p>}
      </ProjectShell>
    </QueryClientProvider>,
  );
}

describe("ProjectShell", () => {
  it("показывает theme toggle и контент активного проекта", async () => {
    vi.mocked(apiClient.listProjectsRequest).mockResolvedValue([
      {
        id: "project-1",
        name: "Project One",
        status: "active",
      },
    ]);
    vi.mocked(apiClient.listAccountsRequest).mockResolvedValue([]);

    renderShell();

    await waitFor(() => {
      expect(
        screen.getByRole("heading", { name: "Project One", level: 1 }),
      ).toBeInTheDocument();
    });

    expect(screen.getByTestId("theme-option-system")).toBeInTheDocument();
    expect(screen.getByText("child-rendered")).toBeInTheDocument();
  });
});
