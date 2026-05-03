import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { AuthScreen } from "./auth-screen";
import type { PlatformSession } from "@/lib/auth";
import * as authLib from "@/lib/auth";
import * as apiClient from "@/lib/api-client";

vi.mock("@/lib/auth", async () => {
  const actual = await vi.importActual<typeof import("@/lib/auth")>("@/lib/auth");
  return {
    ...actual,
    authenticateDemo: vi.fn(),
  };
});

vi.mock("@/lib/api-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api-client")>(
    "@/lib/api-client",
  );
  return {
    ...actual,
    listProjectsRequest: vi.fn(),
  };
});

const demoSession: PlatformSession = {
  token: "demo-token",
  baseUrl: "http://localhost:5073",
  profile: {
    userId: "22222222-2222-2222-2222-222222222222",
    email: "admin@ddcrm.local",
    displayName: "System Admin",
    role: "admin",
    authMode: "demo",
    loggedInAt: "2026-04-25T00:00:00.000Z",
  },
};

describe("AuthScreen", () => {
  it("выполняет базовый парольный вход и передает сессию наружу", async () => {
    const user = userEvent.setup();
    const onAuthenticated = vi.fn();
    const authenticateDemoMock = vi.mocked(authLib.authenticateDemo);
    const listProjectsRequestMock = vi.mocked(apiClient.listProjectsRequest);
    authenticateDemoMock.mockResolvedValue(demoSession);
    listProjectsRequestMock.mockResolvedValue([]);

    render(<AuthScreen onAuthenticated={onAuthenticated} />);
    expect(screen.getByTestId("theme-option-system")).toBeInTheDocument();
    expect(screen.getByTestId("theme-option-light")).toBeInTheDocument();
    expect(screen.getByTestId("theme-option-dark")).toBeInTheDocument();

    await user.clear(screen.getByTestId("auth-email"));
    await user.type(screen.getByTestId("auth-email"), "admin@ddcrm.local");
    await user.clear(screen.getByTestId("auth-password"));
    await user.type(screen.getByTestId("auth-password"), "Admin123!");
    await user.click(screen.getByTestId("auth-submit"));

    await waitFor(() => {
      expect(authenticateDemoMock).toHaveBeenCalledTimes(1);
      expect(listProjectsRequestMock).toHaveBeenCalledTimes(1);
      expect(onAuthenticated).toHaveBeenCalledWith(demoSession);
    });
  });
});
