import type { ApiSession } from "@/lib/api-client";
import { projectRoles, type ProjectRole } from "@/lib/rbac";

const SESSION_STORAGE_KEY = "ddcrm-platform.session";

const DEFAULT_EXTERNAL_API_BASE_URL = "http://localhost:5073";
const DEFAULT_JWT_ISSUER =
  process.env.NEXT_PUBLIC_EXTERNAL_API_JWT_ISSUER ?? "ddcrm-local";
const DEFAULT_JWT_AUDIENCE =
  process.env.NEXT_PUBLIC_EXTERNAL_API_JWT_AUDIENCE ?? "ddcrm-api";
const DEFAULT_SYSTEM_PERMISSION_CLAIM_TYPE =
  process.env.NEXT_PUBLIC_EXTERNAL_API_SYSTEM_PERMISSION_CLAIM_TYPE ??
  "ddcrm.system.permissions";
const DEFAULT_SYSTEM_PERMISSION_CLAIM_VALUE =
  process.env.NEXT_PUBLIC_EXTERNAL_API_SYSTEM_PERMISSION_CLAIM_VALUE ??
  "system.accountManager.manage";
const DEFAULT_SYSTEM_INTEGRATIONS_PERMISSION_CLAIM_VALUE =
  process.env.NEXT_PUBLIC_EXTERNAL_API_SYSTEM_INTEGRATIONS_PERMISSION_CLAIM_VALUE ??
  "system.integrations.manage";
const JWT_TIME_SKEW_SECONDS = 120;

export interface PlatformUserProfile {
  userId: string;
  email: string;
  displayName: string;
  role: ProjectRole;
  systemPermissions?: string[];
  isSystemAdmin?: boolean;
  authMode: "password" | "integration" | "manual";
  authProvider?: string;
  loggedInAt: string;
}

export interface PlatformSession extends ApiSession {
  profile: PlatformUserProfile;
}

interface AuthSessionUser {
  userId: string;
  email: string;
  displayName: string;
  systemPermissions?: string[];
  requiresPasswordChange?: boolean;
  authProvider?: string;
}

interface AuthSessionPayload {
  requestId: string;
  token: string;
  expiresAtUtc: string;
  user: AuthSessionUser;
}

export interface AuthProviderInfo {
  provider: string;
  displayName: string;
  enabled: boolean;
  status: string;
}

interface AuthProviderListPayload {
  requestId: string;
  items: AuthProviderInfo[];
}

interface ErrorPayload {
  errorCode?: string;
  message?: string;
  details?: Record<string, unknown>;
  requestId?: string;
}

function normalizeBaseUrl(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return DEFAULT_EXTERNAL_API_BASE_URL;
  }

  return trimmed.replace(/\/+$/, "");
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
  const required = new Set([
    DEFAULT_SYSTEM_PERMISSION_CLAIM_VALUE.toLowerCase(),
    DEFAULT_SYSTEM_INTEGRATIONS_PERMISSION_CLAIM_VALUE.toLowerCase(),
  ]);

  return permissions.some((permission) =>
    required.has(permission.toLowerCase()),
  );
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

function resolveRoleFromJwtOrDefault(token: string): ProjectRole {
  const payload = parseJwtPayload(token);
  const roleCandidate = payload?.role;
  if (typeof roleCandidate === "string" && isRole(roleCandidate)) {
    return roleCandidate;
  }

  return "owner";
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

function parseErrorMessage(raw: string, status: number) {
  if (!raw) {
    return `HTTP_${status}: Неизвестная ошибка запроса авторизации.`;
  }

  try {
    const payload = JSON.parse(raw) as ErrorPayload;
    if (payload && typeof payload.message === "string" && payload.message.trim()) {
      const code = payload.errorCode?.trim() || `HTTP_${status}`;
      const requestId = payload.requestId?.trim();
      return requestId
        ? `${code}: ${payload.message} (requestId: ${requestId})`
        : `${code}: ${payload.message}`;
    }
  } catch {
    // ignore parse errors and fallback to raw response
  }

  return `HTTP_${status}: ${raw}`;
}

async function postJson<TPayload, TResponse>(
  baseUrl: string,
  path: string,
  payload: TPayload,
  token?: string,
) {
  const response = await fetch(`${normalizeBaseUrl(baseUrl)}${path}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(payload),
  });

  const raw = await response.text();
  if (!response.ok) {
    throw new Error(parseErrorMessage(raw, response.status));
  }

  return (raw ? (JSON.parse(raw) as TResponse) : ({} as TResponse));
}

async function getJson<TResponse>(baseUrl: string, path: string) {
  const response = await fetch(`${normalizeBaseUrl(baseUrl)}${path}`, {
    method: "GET",
    headers: {
      Accept: "application/json",
    },
  });

  const raw = await response.text();
  if (!response.ok) {
    throw new Error(parseErrorMessage(raw, response.status));
  }

  return (raw ? (JSON.parse(raw) as TResponse) : ({} as TResponse));
}

function createSessionFromAuthPayload(
  baseUrl: string,
  payload: AuthSessionPayload,
): PlatformSession {
  const token = payload.token.trim();
  if (!token) {
    throw new Error("Сервер не вернул JWT токен.");
  }

  const jwtPayload = parseJwtPayload(token);
  const userId =
    (typeof jwtPayload?.sub === "string" && jwtPayload.sub.trim()) ||
    payload.user.userId;
  const role = resolveRoleFromJwtOrDefault(token);
  const systemPermissions =
    Array.isArray(payload.user.systemPermissions) && payload.user.systemPermissions.length > 0
      ? payload.user.systemPermissions
      : resolveSystemPermissions(jwtPayload);

  return {
    token,
    baseUrl: normalizeBaseUrl(baseUrl),
    profile: {
      userId,
      email: payload.user.email,
      displayName: payload.user.displayName,
      role,
      systemPermissions,
      isSystemAdmin: hasSystemPermission(systemPermissions),
      authMode:
        payload.user.authProvider && payload.user.authProvider !== "local"
          ? "integration"
          : "password",
      authProvider: payload.user.authProvider ?? "local",
      loggedInAt: new Date().toISOString(),
    },
  };
}

export async function registerWithPassword(params: {
  baseUrl: string;
  email: string;
  password: string;
  displayName?: string;
}): Promise<{ session: PlatformSession; requiresPasswordChange: boolean }> {
  const response = await postJson<
    { email: string; password: string; displayName?: string },
    AuthSessionPayload
  >(params.baseUrl, "/v1/auth/register", {
    email: params.email.trim(),
    password: params.password,
    displayName: params.displayName?.trim() || undefined,
  });

  const session = createSessionFromAuthPayload(params.baseUrl, response);
  return {
    session,
    requiresPasswordChange: Boolean(response.user.requiresPasswordChange),
  };
}

export async function loginWithPassword(params: {
  baseUrl: string;
  email: string;
  password: string;
}): Promise<{ session: PlatformSession; requiresPasswordChange: boolean }> {
  const response = await postJson<
    { email: string; password: string },
    AuthSessionPayload
  >(params.baseUrl, "/v1/auth/login", {
    email: params.email.trim(),
    password: params.password,
  });

  const session = createSessionFromAuthPayload(params.baseUrl, response);
  return {
    session,
    requiresPasswordChange: Boolean(response.user.requiresPasswordChange),
  };
}

export async function changePasswordWithSession(params: {
  session: PlatformSession;
  currentPassword: string;
  newPassword: string;
}) {
  await postJson(
    params.session.baseUrl,
    "/v1/auth/change-password",
    {
      currentPassword: params.currentPassword,
      newPassword: params.newPassword,
    },
    params.session.token,
  );
}

export async function loadAuthProviders(baseUrl: string): Promise<AuthProviderInfo[]> {
  const response = await getJson<AuthProviderListPayload>(
    baseUrl,
    "/v1/auth/providers",
  );
  return Array.isArray(response.items) ? response.items : [];
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

    const permissions = Array.isArray(parsed.profile.systemPermissions)
      ? parsed.profile.systemPermissions.filter(
          (value): value is string => typeof value === "string",
        )
      : [];

    return {
      ...parsed,
      baseUrl: normalizeBaseUrl(parsed.baseUrl),
      profile: {
        ...parsed.profile,
        authMode: parsed.profile.authMode ?? "password",
        authProvider: parsed.profile.authProvider ?? "local",
        systemPermissions: permissions,
        isSystemAdmin:
          typeof parsed.profile.isSystemAdmin === "boolean"
            ? parsed.profile.isSystemAdmin
            : hasSystemPermission(permissions),
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
