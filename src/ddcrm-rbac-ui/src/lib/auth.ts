import type { ApiSession } from "@/lib/api-client";
import { projectRoles, type ProjectRole } from "@/lib/rbac";

const SESSION_STORAGE_KEY = "ddcrm-platform.session";

const DEFAULT_EXTERNAL_API_BASE_URL = "http://localhost:5073";
const DEFAULT_JWT_ISSUER =
  process.env.NEXT_PUBLIC_EXTERNAL_API_JWT_ISSUER ?? "ddcrm-local";
const DEFAULT_JWT_AUDIENCE =
  process.env.NEXT_PUBLIC_EXTERNAL_API_JWT_AUDIENCE ?? "ddcrm-api";
const DEFAULT_JWT_SIGNING_KEY =
  process.env.NEXT_PUBLIC_EXTERNAL_API_JWT_SIGNING_KEY ??
  "replace-with-long-random-signing-key";
const DEFAULT_SYSTEM_PERMISSION_CLAIM_TYPE =
  process.env.NEXT_PUBLIC_EXTERNAL_API_SYSTEM_PERMISSION_CLAIM_TYPE ??
  "ddcrm.system.permissions";
const DEFAULT_SYSTEM_PERMISSION_CLAIM_VALUE =
  process.env.NEXT_PUBLIC_EXTERNAL_API_SYSTEM_PERMISSION_CLAIM_VALUE ??
  "system.accountManager.manage";
const JWT_TIME_SKEW_SECONDS = 120;
const DEMO_TOKEN_VALID_FROM_UNIX = 1704067200; // 2024-01-01T00:00:00Z
const DEMO_TOKEN_VALID_TO_UNIX = 2524608000; // 2050-01-01T00:00:00Z

export interface PlatformUserProfile {
  userId: string;
  email: string;
  displayName: string;
  role: ProjectRole;
  systemPermissions?: string[];
  isSystemAdmin?: boolean;
  authMode: "demo" | "manual";
  loggedInAt: string;
}

export interface PlatformSession extends ApiSession {
  profile: PlatformUserProfile;
}

interface DemoUserCredential {
  userId: string;
  email: string;
  password: string;
  displayName: string;
  role: ProjectRole;
  systemPermissions?: readonly string[];
}

export const demoUsers: readonly DemoUserCredential[] = [
  {
    userId: "11111111-1111-1111-1111-111111111111",
    email: "owner@ddcrm.local",
    password: "Owner123!",
    displayName: "Owner Demo",
    role: "owner",
  },
  {
    userId: "22222222-2222-2222-2222-222222222222",
    email: "admin@ddcrm.local",
    password: "Admin123!",
    displayName: "Admin Demo",
    role: "admin",
    systemPermissions: [DEFAULT_SYSTEM_PERMISSION_CLAIM_VALUE],
  },
  {
    userId: "33333333-3333-3333-3333-333333333333",
    email: "moderator@ddcrm.local",
    password: "Moderator123!",
    displayName: "Moderator Demo",
    role: "moderator",
  },
] as const;

function normalizeBaseUrl(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return DEFAULT_EXTERNAL_API_BASE_URL;
  }

  return trimmed.replace(/\/+$/, "");
}

function toBase64Url(bytes: Uint8Array) {
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function encodeJsonBase64Url(payload: Record<string, unknown>) {
  return toBase64Url(new TextEncoder().encode(JSON.stringify(payload)));
}

async function signHs256(unsignedPayload: string, signingKey: string) {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(signingKey),
    {
      name: "HMAC",
      hash: "SHA-256",
    },
    false,
    ["sign"],
  );

  const signature = await crypto.subtle.sign(
    "HMAC",
    key,
    new TextEncoder().encode(unsignedPayload),
  );

  return toBase64Url(new Uint8Array(signature));
}

function splitPermissions(value: string) {
  return value
    .split(/[,\s;]+/g)
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}

function resolveSystemPermissions(payload: Record<string, unknown> | null): string[] {
  if (!payload) {
    return [];
  }

  const rawValue = payload[DEFAULT_SYSTEM_PERMISSION_CLAIM_TYPE];
  if (typeof rawValue === "string") {
    return splitPermissions(rawValue);
  }

  if (Array.isArray(rawValue)) {
    return rawValue
      .filter((item): item is string => typeof item === "string")
      .flatMap((item) => splitPermissions(item));
  }

  return [];
}

function hasSystemPermission(permissions: readonly string[]) {
  return permissions.some(
    (permission) =>
      permission.toLowerCase() === DEFAULT_SYSTEM_PERMISSION_CLAIM_VALUE.toLowerCase(),
  );
}

async function createDemoToken(subject: string, systemPermissions: readonly string[]) {
  const header = { alg: "HS256", typ: "JWT" };
  const payload: Record<string, unknown> = {
    sub: subject,
    iss: DEFAULT_JWT_ISSUER,
    aud: DEFAULT_JWT_AUDIENCE,
    // Используем стабильное окно валидности, чтобы demo JWT не зависел от локальных часов браузера.
    iat: DEMO_TOKEN_VALID_FROM_UNIX,
    nbf: DEMO_TOKEN_VALID_FROM_UNIX,
    exp: DEMO_TOKEN_VALID_TO_UNIX,
  };
  if (systemPermissions.length > 0) {
    payload[DEFAULT_SYSTEM_PERMISSION_CLAIM_TYPE] = systemPermissions.join(" ");
  }

  const unsigned = `${encodeJsonBase64Url(header)}.${encodeJsonBase64Url(payload)}`;
  const signature = await signHs256(unsigned, DEFAULT_JWT_SIGNING_KEY);
  return `${unsigned}.${signature}`;
}

export async function authenticateDemo(params: {
  email: string;
  password: string;
  baseUrl: string;
}): Promise<PlatformSession> {
  const email = params.email.trim().toLowerCase();
  const user = demoUsers.find((candidate) => candidate.email === email);

  if (!user || user.password !== params.password) {
    throw new Error("Неверный email или пароль.");
  }

  const permissions = [...(user.systemPermissions ?? [])];
  const token = await createDemoToken(user.userId, permissions);
  return {
    token,
    baseUrl: normalizeBaseUrl(params.baseUrl),
    profile: {
      userId: user.userId,
      email: user.email,
      displayName: user.displayName,
      role: user.role,
      systemPermissions: permissions,
      isSystemAdmin: hasSystemPermission(permissions),
      authMode: "demo",
      loggedInAt: new Date().toISOString(),
    },
  };
}

function parseJwtPayload(token: string) {
  const parts = token.split(".");
  if (parts.length !== 3) {
    return null;
  }

  try {
    const normalized = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = normalized.padEnd(
      normalized.length + ((4 - (normalized.length % 4)) % 4),
      "=",
    );
    return JSON.parse(atob(padded)) as Record<string, unknown>;
  } catch {
    return null;
  }
}

function isRole(value: string): value is ProjectRole {
  return projectRoles.includes(value as ProjectRole);
}

function isTokenTimeWindowValid(token: string): boolean {
  const payload = parseJwtPayload(token);
  if (!payload) {
    return true;
  }

  const now = Math.floor(Date.now() / 1000);
  const exp = typeof payload.exp === "number" ? payload.exp : null;
  if (exp !== null && exp < now - JWT_TIME_SKEW_SECONDS) {
    return false;
  }

  const nbf = typeof payload.nbf === "number" ? payload.nbf : null;
  if (nbf !== null && nbf > now + JWT_TIME_SKEW_SECONDS) {
    return false;
  }

  return true;
}

export function authenticateManual(params: {
  token: string;
  baseUrl: string;
  displayName: string;
  email: string;
  role: ProjectRole;
  userId?: string;
}): PlatformSession {
  const token = params.token.trim();
  if (!token) {
    throw new Error("JWT обязателен для ручного входа.");
  }

  const payload = parseJwtPayload(token);
  const subject =
    (typeof payload?.sub === "string" && payload.sub.trim()) ||
    params.userId?.trim() ||
    crypto.randomUUID();

  if (!isRole(params.role)) {
    throw new Error("Некорректная роль в ручном входе.");
  }
  const systemPermissions = resolveSystemPermissions(payload);

  return {
    token,
    baseUrl: normalizeBaseUrl(params.baseUrl),
    profile: {
      userId: subject,
      email: params.email.trim() || "manual@ddcrm.local",
      displayName: params.displayName.trim() || "Manual User",
      role: params.role,
      systemPermissions,
      isSystemAdmin: hasSystemPermission(systemPermissions),
      authMode: "manual",
      loggedInAt: new Date().toISOString(),
    },
  };
}

export function readStoredSession(): PlatformSession | null {
  if (typeof window === "undefined") {
    return null;
  }

  const raw = localStorage.getItem(SESSION_STORAGE_KEY);
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as PlatformSession;
    if (
      !parsed ||
      typeof parsed.token !== "string" ||
      !parsed.token.trim() ||
      typeof parsed.baseUrl !== "string" ||
      !parsed.profile ||
      typeof parsed.profile.userId !== "string" ||
      typeof parsed.profile.email !== "string" ||
      typeof parsed.profile.displayName !== "string" ||
      !isRole(parsed.profile.role)
    ) {
      return null;
    }

    if (!isTokenTimeWindowValid(parsed.token)) {
      localStorage.removeItem(SESSION_STORAGE_KEY);
      return null;
    }

    return {
      ...parsed,
      baseUrl: normalizeBaseUrl(parsed.baseUrl),
      profile: {
        ...parsed.profile,
        systemPermissions: Array.isArray(parsed.profile.systemPermissions)
          ? parsed.profile.systemPermissions.filter(
              (value): value is string => typeof value === "string",
            )
          : [],
        isSystemAdmin:
          typeof parsed.profile.isSystemAdmin === "boolean"
            ? parsed.profile.isSystemAdmin
            : hasSystemPermission(
                Array.isArray(parsed.profile.systemPermissions)
                  ? parsed.profile.systemPermissions.filter(
                      (value): value is string => typeof value === "string",
                    )
                  : [],
              ),
      },
    };
  } catch {
    return null;
  }
}

export function storeSession(session: PlatformSession) {
  if (typeof window === "undefined") {
    return;
  }

  localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
}

export function clearStoredSession() {
  if (typeof window === "undefined") {
    return;
  }

  localStorage.removeItem(SESSION_STORAGE_KEY);
}

export function updateSessionRole(
  session: PlatformSession,
  role: ProjectRole,
): PlatformSession {
  return {
    ...session,
    profile: {
      ...session.profile,
      role,
    },
  };
}

export function getDefaultBaseUrl() {
  return DEFAULT_EXTERNAL_API_BASE_URL;
}

export function getJwtMeta() {
  return {
    issuer: DEFAULT_JWT_ISSUER,
    audience: DEFAULT_JWT_AUDIENCE,
  };
}

export function readJwtInfo(token: string): {
  subject: string | null;
  expiresAt: string | null;
} {
  const payload = parseJwtPayload(token);
  if (!payload) {
    return {
      subject: null,
      expiresAt: null,
    };
  }

  const subject = typeof payload.sub === "string" ? payload.sub : null;
  const expires =
    typeof payload.exp === "number" ? new Date(payload.exp * 1000).toISOString() : null;

  return {
    subject,
    expiresAt: expires,
  };
}
