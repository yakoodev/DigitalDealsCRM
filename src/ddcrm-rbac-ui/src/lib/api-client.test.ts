import { beforeEach, describe, expect, it, vi } from "vitest";
import { createProjectRequest } from "@/lib/api-client";
import * as externalApi from "@/generated/external-api";

vi.mock("@/generated/external-api", async () => {
  const actual = await vi.importActual<typeof import("@/generated/external-api")>(
    "@/generated/external-api",
  );

  return {
    ...actual,
    createProject: vi.fn(),
  };
});

describe("api-client headers", () => {
  beforeEach(() => {
    vi.mocked(externalApi.createProject).mockReset();
  });

  it("передаёт Authorization в POST createProject", async () => {
    vi.mocked(externalApi.createProject).mockResolvedValue({
      status: 201,
      data: {
        requestId: "req-1",
        project: {
          id: "project-1",
          name: "Test",
          status: "active",
        },
      },
      headers: new Headers(),
    });

    await createProjectRequest(
      {
        baseUrl: "http://localhost:5073",
        token: "test-token",
      },
      "Test",
    );

    expect(externalApi.createProject).toHaveBeenCalledTimes(1);
    const [, options] = vi.mocked(externalApi.createProject).mock.calls[0];
    const headers = options?.headers as Record<string, string>;

    expect(options?.headers).not.toBeInstanceOf(Headers);
    expect(headers.Authorization).toBe("Bearer test-token");
    expect(headers.Accept).toBe("application/json");
    expect(typeof headers["Idempotency-Key"]).toBe("string");
    expect(headers["Idempotency-Key"].length).toBeGreaterThan(0);
  });
});
