import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { useSessionGuard } from "@/lib/use-session-guard";

const replaceMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    replace: replaceMock,
  }),
  usePathname: () => "/projects/project-1/messages",
  useSearchParams: () => new URLSearchParams(),
}));

function Probe() {
  const { session } = useSessionGuard();
  return <p>{session ? session.profile.email : "no-session"}</p>;
}

function encodeBase64UrlJson(payload: Record<string, unknown>) {
  return btoa(JSON.stringify(payload))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
}

describe("useSessionGuard", () => {
  beforeEach(() => {
    replaceMock.mockReset();
    localStorage.clear();
  });

  it("редиректит на login при отсутствии сессии", async () => {
    render(<Probe />);

    await waitFor(() => {
      expect(replaceMock).toHaveBeenCalledWith(
        "/login?next=%2Fprojects%2Fproject-1%2Fmessages",
      );
    });
    expect(screen.getByText("no-session")).toBeInTheDocument();
  });

  it("возвращает активную сессию без редиректа", async () => {
    localStorage.setItem(
      "ddcrm-platform.session",
      JSON.stringify({
        token: "test-token",
        baseUrl: "http://localhost:5073",
        profile: {
          userId: "11111111-1111-1111-1111-111111111111",
          email: "owner@ddcrm.local",
          displayName: "Owner Demo",
          role: "owner",
          authMode: "demo",
          loggedInAt: new Date().toISOString(),
        },
      }),
    );

    render(<Probe />);

    expect(await screen.findByText("owner@ddcrm.local")).toBeInTheDocument();
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it("сбрасывает просроченную сессию и редиректит на login", async () => {
    const now = Math.floor(Date.now() / 1000);
    const expiredToken = `${encodeBase64UrlJson({ alg: "HS256", typ: "JWT" })}.${encodeBase64UrlJson({
      sub: "11111111-1111-1111-1111-111111111111",
      exp: now - 3600,
      nbf: now - 7200,
    })}.signature`;

    localStorage.setItem(
      "ddcrm-platform.session",
      JSON.stringify({
        token: expiredToken,
        baseUrl: "http://localhost:5073",
        profile: {
          userId: "11111111-1111-1111-1111-111111111111",
          email: "owner@ddcrm.local",
          displayName: "Owner Demo",
          role: "owner",
          authMode: "demo",
          loggedInAt: new Date().toISOString(),
        },
      }),
    );

    render(<Probe />);

    await waitFor(() => {
      expect(replaceMock).toHaveBeenCalledWith(
        "/login?next=%2Fprojects%2Fproject-1%2Fmessages",
      );
    });
    expect(screen.getByText("no-session")).toBeInTheDocument();
  });
});
