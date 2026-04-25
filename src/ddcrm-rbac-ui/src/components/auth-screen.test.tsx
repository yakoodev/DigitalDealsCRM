import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { AuthScreen } from "./auth-screen";
import type { PlatformSession } from "@/lib/auth";
import * as authLib from "@/lib/auth";

vi.mock("@/lib/auth", async () => {
  const actual = await vi.importActual<typeof import("@/lib/auth")>("@/lib/auth");
  return {
    ...actual,
    authenticateDemo: vi.fn(),
    authenticateManual: vi.fn(),
  };
});

const demoSession: PlatformSession = {
  token: "demo-token",
  baseUrl: "http://localhost:5073",
  profile: {
    userId: "11111111-1111-1111-1111-111111111111",
    email: "owner@ddcrm.local",
    displayName: "Owner Demo",
    role: "owner",
    authMode: "demo",
    loggedInAt: "2026-04-25T00:00:00.000Z",
  },
};

describe("AuthScreen", () => {
  it("выполняет demo-вход и передает сессию наружу", async () => {
    const user = userEvent.setup();
    const onAuthenticated = vi.fn();
    const authenticateDemoMock = vi.mocked(authLib.authenticateDemo);
    authenticateDemoMock.mockResolvedValue(demoSession);

    render(<AuthScreen onAuthenticated={onAuthenticated} />);

    await user.click(screen.getByTestId("auth-demo-submit"));

    await waitFor(() => {
      expect(authenticateDemoMock).toHaveBeenCalledTimes(1);
      expect(onAuthenticated).toHaveBeenCalledWith(demoSession);
    });
  });

  it("поддерживает ручной JWT-вход", async () => {
    const user = userEvent.setup();
    const onAuthenticated = vi.fn();
    const authenticateManualMock = vi.mocked(authLib.authenticateManual);
    const manualSession: PlatformSession = {
      ...demoSession,
      token: "manual-token",
      profile: {
        ...demoSession.profile,
        authMode: "manual",
        role: "moderator",
      },
    };
    authenticateManualMock.mockReturnValue(manualSession);

    render(<AuthScreen onAuthenticated={onAuthenticated} />);

    await user.click(screen.getByTestId("auth-mode-manual"));
    await user.type(screen.getByTestId("auth-manual-token"), "header.payload.signature");
    await user.click(screen.getByTestId("auth-manual-submit"));

    await waitFor(() => {
      expect(authenticateManualMock).toHaveBeenCalledTimes(1);
      expect(onAuthenticated).toHaveBeenCalledWith(manualSession);
    });
  });
});
