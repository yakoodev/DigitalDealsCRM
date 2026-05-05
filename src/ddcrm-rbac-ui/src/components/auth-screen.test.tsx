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
    loginWithPassword: vi.fn(),
    registerWithPassword: vi.fn(),
    changePasswordWithSession: vi.fn(),
    loadAuthProviders: vi.fn(),
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

const passwordSession: PlatformSession = {
  token: "password-token",
  baseUrl: "http://localhost:5073",
  profile: {
    userId: "11111111-1111-1111-1111-111111111111",
    email: "owner@ddcrm.local",
    displayName: "Owner",
    role: "owner",
    authMode: "password",
    loggedInAt: "2026-04-25T00:00:00.000Z",
  },
};

describe("AuthScreen", () => {
  it("выполняет вход по email/password", async () => {
    const user = userEvent.setup();
    const onAuthenticated = vi.fn();
    const loginWithPasswordMock = vi.mocked(authLib.loginWithPassword);
    const listProjectsRequestMock = vi.mocked(apiClient.listProjectsRequest);
    const loadAuthProvidersMock = vi.mocked(authLib.loadAuthProviders);
    loginWithPasswordMock.mockResolvedValue({
      session: passwordSession,
      requiresPasswordChange: false,
    });
    listProjectsRequestMock.mockResolvedValue([]);
    loadAuthProvidersMock.mockResolvedValue([]);

    render(<AuthScreen onAuthenticated={onAuthenticated} />);
    expect(screen.getByTestId("theme-option-system")).toBeInTheDocument();
    expect(screen.getByTestId("theme-option-light")).toBeInTheDocument();
    expect(screen.getByTestId("theme-option-dark")).toBeInTheDocument();

    await user.type(screen.getByTestId("auth-email"), "owner@ddcrm.local");
    await user.type(screen.getByTestId("auth-password"), "Passw0rd!123");
    await user.click(screen.getByTestId("auth-submit"));

    await waitFor(() => {
      expect(loginWithPasswordMock).toHaveBeenCalledTimes(1);
      expect(listProjectsRequestMock).toHaveBeenCalledTimes(1);
      expect(onAuthenticated).toHaveBeenCalledWith(passwordSession);
    });
  });

  it("требует смену пароля и завершает вход после change-password", async () => {
    const user = userEvent.setup();
    const onAuthenticated = vi.fn();
    const loginWithPasswordMock = vi.mocked(authLib.loginWithPassword);
    const changePasswordMock = vi.mocked(authLib.changePasswordWithSession);
    const listProjectsRequestMock = vi.mocked(apiClient.listProjectsRequest);
    const loadAuthProvidersMock = vi.mocked(authLib.loadAuthProviders);
    loginWithPasswordMock.mockResolvedValue({
      session: passwordSession,
      requiresPasswordChange: true,
    });
    changePasswordMock.mockResolvedValue(undefined);
    listProjectsRequestMock.mockResolvedValue([]);
    loadAuthProvidersMock.mockResolvedValue([]);

    render(<AuthScreen onAuthenticated={onAuthenticated} />);

    await user.type(screen.getByTestId("auth-email"), "root@ddcrm.local");
    await user.type(screen.getByTestId("auth-password"), "ChangeMe123!");
    await user.click(screen.getByTestId("auth-submit"));

    await waitFor(() => {
      expect(screen.getByTestId("auth-change-password-submit")).toBeInTheDocument();
    });

    await user.type(screen.getByTestId("auth-new-password"), "N3wPassw0rd!");
    await user.type(screen.getByTestId("auth-new-password-confirm"), "N3wPassw0rd!");
    await user.click(screen.getByTestId("auth-change-password-submit"));

    await waitFor(() => {
      expect(changePasswordMock).toHaveBeenCalledTimes(1);
      expect(onAuthenticated).toHaveBeenCalledWith(passwordSession);
    });
  });
});
