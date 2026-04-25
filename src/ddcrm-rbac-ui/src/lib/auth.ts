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

export interface PlatformUserProfile {
  userId: string;
  email: string;
  displayName: string;
  role: ProjectRole;
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

async function createDemoToken(subject: string) {
  const header = { alg: "HS256", typ: "JWT" };
  const now = Math.floor(Date.now() / 1000);
  const payload = {
    sub: subject,
    iss: DEFAULT_JWT_ISSUER,
    aud: DEFAULT_JWT_AUDIENCE,
    iat: now,
    nbf: now,
    exp: now + 60 * 60 * 12,
  };

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

  const token = await createDemoToken(user.userId);
  return {
    token,
    baseUrl: normalizeBaseUrl(params.baseUrl),
    profile: {
      userId: user.userId,
      email: user.email,
      displayName: user.displayName,
      role: user.role,
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

  return {
    token,
    baseUrl: normalizeBaseUrl(params.baseUrl),
    profile: {
      userId: subject,
      email: params.email.trim() || "manual@ddcrm.local",
      displayName: params.displayName.trim() || "Manual User",
      role: params.role,
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
      typeof parsed.baseUrl !== "string" ||
      !parsed.profile ||
      typeof parsed.profile.userId !== "string" ||
      typeof parsed.profile.email !== "string" ||
      typeof parsed.profile.displayName !== "string" ||
      !isRole(parsed.profile.role)
    ) {
      return null;
    }

    return {
      ...parsed,
      baseUrl: normalizeBaseUrl(parsed.baseUrl),
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
