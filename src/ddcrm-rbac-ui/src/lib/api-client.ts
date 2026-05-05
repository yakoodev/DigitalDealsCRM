import {
  type Account,
  type AccountCreateRequest,
  type AccountType,
  type ErrorResponse,
  type GenericObjectRequest,
  type GenericObjectResponseData,
  type Project,
  type ProxyConfig,
  type ProxyCredentialsMasked,
  type RoleChangeRequest,
  addMember,
  changePlan,
  changeMemberRole,
  createAccount,
  createPayment,
  createProject,
  deleteAccount,
  getAccountProxyCredentialsMasked,
  listAccounts,
  listProjectAccountTypes,
  listProjects,
  proxyAccountApiAction,
  purchaseAddon,
  revealAccountProxyCredentials,
  removeMember,
  updateAccount,
  updateAccountProxyCredentials,
} from "@/generated/external-api";
import { createIdempotencyKey } from "@/lib/idempotency";

export interface ApiSession {
  token: string;
  baseUrl: string;
}

export type ProjectAccountType = AccountType;

export interface AdminWorkerServer {
  serverId: string;
  baseUrlTemplate: string;
  status: "active" | "draining" | "inactive";
  health: "healthy" | "degraded" | "unhealthy";
  capacity: number;
  currentLoad: number;
  dockerHost?: string | null;
  dockerNetwork?: string | null;
  lastHeartbeatAtUtc?: string | null;
  registry: AdminWorkerServerRegistrySummary;
  metadata: Record<string, unknown>;
}

export interface AdminWorkerServerRegistrySummary {
  enabled: boolean;
  host: string;
  username?: string | null;
  hasToken: boolean;
  tokenUpdatedAtUtc?: string | null;
}

export interface AdminWorkerServerRegistryUpsertPayload {
  enabled?: boolean;
  host?: string;
  username?: string;
  token?: string;
  clearToken?: boolean;
}

export interface AdminWorkerServerUpsertPayload {
  baseUrlTemplate?: string;
  status?: AdminWorkerServer["status"];
  health?: AdminWorkerServer["health"];
  capacity?: number;
  currentLoad?: number;
  dockerHost?: string;
  dockerNetwork?: string;
  registry?: AdminWorkerServerRegistryUpsertPayload;
  metadata?: Record<string, unknown>;
}

export interface AdminAccountTypeRuntime {
  autospawnEnabled: boolean;
  workerImage: string;
  workerPathPrefix: string;
  healthPath: string;
  containerPort: number;
  environmentVariables: Record<string, string>;
  workerCommand?: string[];
}

export interface AdminAccountType extends ProjectAccountType {
  runtime: AdminAccountTypeRuntime;
}

export interface AdminAccountTypeUpsertPayload {
  platform?: string;
  displayName?: string;
  description?: string;
  workerProfileId?: string;
  enabled?: boolean;
  sortOrder?: number;
  formFields?: ProjectAccountType["formFields"];
  runtime?: AdminAccountTypeRuntime;
}

export interface AdminIntegrationGrant {
  integrationKey: string;
  status: "active" | "revoked";
  scopes: string[];
  grantedAtUtc: string;
  revokedAtUtc?: string | null;
  credentialStatus?: "pending_sync" | "active" | "revoking" | "revoked" | null;
  credentialMasked?: string | null;
}

export interface AdminIntegrationGrantUpsertPayload {
  scopes?: string[];
}

export interface AdminTelegramProxyProfile {
  id: string;
  name: string;
  proxyType: "http" | "https" | "socks5";
  host: string;
  port: number;
  isActive: boolean;
  hasCredentials: boolean;
  updatedAtUtc: string;
}

export interface AdminTelegramProxyProfileUpsertPayload {
  name: string;
  proxyType?: "http" | "https" | "socks5";
  host: string;
  port: number;
  setActive: boolean;
  clearCredentials: boolean;
  login?: string;
  password?: string;
}

interface ProxyCredentialsUpdateInput {
  reason: string;
  proxyConfig: ProxyConfig;
}

interface RevealCredentialsInput {
  reason: string;
}

type ApiResponseEnvelope<TSuccess> = {
  data: TSuccess | ErrorResponse;
  status: number;
  headers: Headers;
};

interface AdminWorkerServerListEnvelope {
  requestId: string;
  items: AdminWorkerServer[];
}

interface AdminWorkerServerEnvelope {
  requestId: string;
  workerServer: AdminWorkerServer;
}

interface AdminAccountTypeListEnvelope {
  requestId: string;
  items: AdminAccountType[];
}

interface AdminAccountTypeEnvelope {
  requestId: string;
  accountType: AdminAccountType;
}

interface AdminIntegrationGrantEnvelope {
  requestId: string;
  grant: AdminIntegrationGrant;
}

interface AdminIntegrationGrantListEnvelope {
  requestId: string;
  items: AdminIntegrationGrant[];
}

interface AdminTelegramProxyProfileEnvelope {
  requestId: string;
  profile: AdminTelegramProxyProfile;
}

interface AdminTelegramProxyProfileListEnvelope {
  requestId: string;
  items: AdminTelegramProxyProfile[];
}

function resolveRequestId(headers: Headers, fallback?: string) {
  const headerValue = headers.get("x-request-id")?.trim();
  if (headerValue) {
    return headerValue;
  }

  if (fallback && fallback.trim()) {
    return fallback.trim();
  }

  return "n/a";
}

function trimTrailingSlash(value: string) {
  return value.endsWith("/") ? value.slice(0, -1) : value;
}

function createBaseUrlFetcher(baseUrl: string): typeof fetch {
  const normalizedBase = trimTrailingSlash(baseUrl);

  return ((input: RequestInfo | URL, init?: RequestInit) => {
    const inputUrl =
      typeof input === "string"
        ? input
        : input instanceof URL
          ? input.toString()
          : input.url;

    const absoluteUrl =
      inputUrl.startsWith("http://") || inputUrl.startsWith("https://")
        ? inputUrl
        : `${normalizedBase}${inputUrl}`;

    return fetch(absoluteUrl, init);
  }) as typeof fetch;
}

function buildRequestInit(
  session: ApiSession,
  includeIdempotencyKey: boolean,
): RequestInit {
  const headers: Record<string, string> = {
    Authorization: `Bearer ${session.token}`,
    Accept: "application/json",
  };

  if (includeIdempotencyKey) {
    headers["Idempotency-Key"] = createIdempotencyKey();
  }

  return { headers };
}

function unwrapOrThrow<TSuccess>(response: ApiResponseEnvelope<TSuccess>): TSuccess {
  if (response.status >= 200 && response.status < 300) {
    return response.data as TSuccess;
  }

  const errorPayload = response.data as ErrorResponse;
  const requestId = resolveRequestId(response.headers, errorPayload?.requestId);
  const details =
    errorPayload && typeof errorPayload.details === "object" && errorPayload.details
      ? (errorPayload.details as Record<string, unknown>)
      : null;

  if (
    response.status === 401 &&
    (!errorPayload || typeof errorPayload.message !== "string" || !errorPayload.message)
  ) {
    throw new Error(
      `UNAUTHORIZED: Сессия истекла или JWT невалиден. Выполните вход заново. (requestId: ${requestId})`,
    );
  }

  if (response.status === 401 && details) {
    if (details.hasBearerHeader === false) {
      throw new Error(
        `UNAUTHORIZED: Authorization Bearer заголовок не был отправлен браузером. Проверьте расширения/блокировщики и повторите вход. (requestId: ${requestId})`,
      );
    }

    if (typeof details.authFailure === "string" && details.authFailure) {
      throw new Error(
        `UNAUTHORIZED: JWT отклонён (${details.authFailure}). Выполните вход по email и паролю заново. (requestId: ${requestId})`,
      );
    }
  }

  const errorCode = errorPayload?.errorCode ?? `HTTP_${response.status}`;
  const message = errorPayload?.message ?? "Неизвестная ошибка API.";

  throw new Error(`${errorCode}: ${message} (requestId: ${requestId})`);
}

async function requestAdminEnvelope<TSuccess>(
  session: ApiSession,
  path: string,
  init: {
    method: "GET" | "PUT" | "POST" | "DELETE";
    body?: unknown;
    idempotent?: boolean;
  },
) {
  const requestInit = buildRequestInit(session, init.idempotent ?? false);
  const headers = new Headers(requestInit.headers);
  let body: string | undefined;
  if (typeof init.body !== "undefined") {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(init.body);
  }

  const response = await createBaseUrlFetcher(session.baseUrl)(path, {
    method: init.method,
    headers,
    body,
  });
  const raw = await response.text();
  const data = raw ? (JSON.parse(raw) as TSuccess | ErrorResponse) : ({} as TSuccess);

  return unwrapOrThrow({
    data,
    status: response.status,
    headers: response.headers,
  });
}

export async function listProjectsRequest(session: ApiSession): Promise<Project[]> {
  const response = await listProjects(
    buildRequestInit(session, false),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).items;
}

export async function createProjectRequest(
  session: ApiSession,
  name: string,
): Promise<Project> {
  const response = await createProject(
    { name },
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).project;
}

export async function addProjectMemberRequest(
  session: ApiSession,
  projectId: string,
  payload: GenericObjectRequest,
) {
  const response = await addMember(
    projectId,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response);
}

export async function changeProjectMemberRoleRequest(
  session: ApiSession,
  projectId: string,
  userId: string,
  role: RoleChangeRequest["role"],
) {
  const response = await changeMemberRole(
    projectId,
    userId,
    { role },
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response);
}

export async function removeProjectMemberRequest(
  session: ApiSession,
  projectId: string,
  userId: string,
) {
  const response = await removeMember(
    projectId,
    userId,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response);
}

export async function listAccountsRequest(
  session: ApiSession,
  projectId: string,
): Promise<Account[]> {
  const response = await listAccounts(
    projectId,
    buildRequestInit(session, false),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).items;
}

export async function listProjectAccountTypesRequest(
  session: ApiSession,
  projectId: string,
): Promise<ProjectAccountType[]> {
  const response = await listProjectAccountTypes(
    projectId,
    buildRequestInit(session, false),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).items;
}

export async function listAdminWorkerServersRequest(
  session: ApiSession,
): Promise<AdminWorkerServer[]> {
  const response = await requestAdminEnvelope<AdminWorkerServerListEnvelope>(
    session,
    "/v1/admin/account-manager/worker-servers",
    {
      method: "GET",
    },
  );

  return response.items ?? [];
}

export async function upsertAdminWorkerServerRequest(
  session: ApiSession,
  serverId: string,
  payload: AdminWorkerServerUpsertPayload,
): Promise<AdminWorkerServer> {
  const response = await requestAdminEnvelope<AdminWorkerServerEnvelope>(
    session,
    `/v1/admin/account-manager/worker-servers/${encodeURIComponent(serverId)}`,
    {
      method: "PUT",
      body: payload,
      idempotent: true,
    },
  );

  return response.workerServer;
}

export async function listAdminAccountTypesRequest(
  session: ApiSession,
): Promise<AdminAccountType[]> {
  const response = await requestAdminEnvelope<AdminAccountTypeListEnvelope>(
    session,
    "/v1/admin/account-manager/account-types",
    {
      method: "GET",
    },
  );

  return response.items ?? [];
}

export async function upsertAdminAccountTypeRequest(
  session: ApiSession,
  accountTypeId: string,
  payload: AdminAccountTypeUpsertPayload,
): Promise<AdminAccountType> {
  const response = await requestAdminEnvelope<AdminAccountTypeEnvelope>(
    session,
    `/v1/admin/account-manager/account-types/${encodeURIComponent(accountTypeId)}`,
    {
      method: "PUT",
      body: payload,
      idempotent: true,
    },
  );

  return response.accountType;
}

export async function listAdminProjectIntegrationGrantsRequest(
  session: ApiSession,
  projectId: string,
): Promise<AdminIntegrationGrant[]> {
  const response = await requestAdminEnvelope<AdminIntegrationGrantListEnvelope>(
    session,
    `/v1/admin/integrations/projects/${encodeURIComponent(projectId)}/grants`,
    {
      method: "GET",
    },
  );

  return response.items ?? [];
}

export async function upsertAdminProjectIntegrationGrantRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  payload: AdminIntegrationGrantUpsertPayload,
): Promise<AdminIntegrationGrant> {
  const response = await requestAdminEnvelope<AdminIntegrationGrantEnvelope>(
    session,
    `/v1/admin/integrations/projects/${encodeURIComponent(projectId)}/grants/${encodeURIComponent(integrationKey)}`,
    {
      method: "PUT",
      body: payload,
      idempotent: true,
    },
  );

  return response.grant;
}

export async function revokeAdminProjectIntegrationGrantRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
) {
  return requestAdminEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/admin/integrations/projects/${encodeURIComponent(projectId)}/grants/${encodeURIComponent(integrationKey)}`,
    {
      method: "DELETE",
      idempotent: true,
    },
  );
}

export async function listAdminTelegramProxyProfilesRequest(
  session: ApiSession,
): Promise<AdminTelegramProxyProfile[]> {
  const response = await requestAdminEnvelope<AdminTelegramProxyProfileListEnvelope>(
    session,
    "/v1/admin/integrations/telegram/proxies",
    {
      method: "GET",
    },
  );

  return response.items ?? [];
}

export async function upsertAdminTelegramProxyProfileRequest(
  session: ApiSession,
  proxyId: string,
  payload: AdminTelegramProxyProfileUpsertPayload,
): Promise<AdminTelegramProxyProfile> {
  const response = await requestAdminEnvelope<AdminTelegramProxyProfileEnvelope>(
    session,
    `/v1/admin/integrations/telegram/proxies/${encodeURIComponent(proxyId)}`,
    {
      method: "PUT",
      body: payload,
      idempotent: true,
    },
  );

  return response.profile;
}

export async function createAccountRequest(
  session: ApiSession,
  projectId: string,
  payload: AccountCreateRequest,
): Promise<Account> {
  const response = await createAccount(
    projectId,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).account;
}

export async function updateAccountRequest(
  session: ApiSession,
  projectId: string,
  accountId: string,
  payload: GenericObjectRequest,
): Promise<Account> {
  const response = await updateAccount(
    projectId,
    accountId,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).account;
}

export async function deleteAccountRequest(
  session: ApiSession,
  projectId: string,
  accountId: string,
) {
  const response = await deleteAccount(
    projectId,
    accountId,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response);
}

export async function getMaskedProxyCredentialsRequest(
  session: ApiSession,
  projectId: string,
  accountId: string,
): Promise<ProxyCredentialsMasked> {
  const response = await getAccountProxyCredentialsMasked(
    projectId,
    accountId,
    buildRequestInit(session, false),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).proxyCredentials;
}

export async function updateProxyCredentialsRequest(
  session: ApiSession,
  projectId: string,
  accountId: string,
  payload: ProxyCredentialsUpdateInput,
) {
  const response = await updateAccountProxyCredentials(
    projectId,
    accountId,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response);
}

export async function revealProxyCredentialsRequest(
  session: ApiSession,
  projectId: string,
  accountId: string,
  payload: RevealCredentialsInput,
): Promise<ProxyConfig> {
  const response = await revealAccountProxyCredentials(
    projectId,
    accountId,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).proxyConfig;
}

export async function createPaymentRequest(
  session: ApiSession,
  projectId: string,
  payload: GenericObjectRequest,
): Promise<GenericObjectResponseData | undefined> {
  const response = await createPayment(
    projectId,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).data;
}

export async function purchaseAddonRequest(
  session: ApiSession,
  projectId: string,
  addonId: string,
  payload?: GenericObjectRequest,
): Promise<GenericObjectResponseData | undefined> {
  const response = await purchaseAddon(
    projectId,
    addonId,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).data;
}

export async function changePlanRequest(
  session: ApiSession,
  projectId: string,
  payload: GenericObjectRequest,
) {
  const response = await changePlan(
    projectId,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response);
}

export async function proxyAccountActionRequest(
  session: ApiSession,
  routeKey: string,
  action: string,
  payload?: GenericObjectRequest,
) {
  const response = await proxyAccountApiAction(
    routeKey,
    action,
    payload,
    buildRequestInit(session, true),
    createBaseUrlFetcher(session.baseUrl),
  );

  return unwrapOrThrow(response).result;
}

export async function runAccountActionRequest(
  session: ApiSession,
  accountId: string,
  action: string,
  payload?: GenericObjectRequest,
) {
  const routeKey = buildRouteKey(accountId);
  return proxyAccountActionRequest(session, routeKey, action, payload);
}

export function buildRouteKey(accountId: string) {
  return `rk.${accountId.replaceAll("-", "")}`;
}
