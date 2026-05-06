import {
  type Account,
  type AccountCreateRequest,
  type AccountType,
  type ErrorResponse,
  type GenericObjectRequest,
  type GenericObjectResponseData,
  type MailConfig,
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
  integrationType: "service" | "worker" | "notification" | "custom" | "platform" | "unknown";
  status: "active" | "revoked";
  scopes: string[];
  maxInstances: number;
  grantedAtUtc: string;
  revokedAtUtc?: string | null;
  credentialStatus?: "pending_sync" | "active" | "revoking" | "revoked" | null;
  credentialMasked?: string | null;
  runtimeStatus?: "pending_provision" | "active" | "revoking" | "revoked" | null;
  runtimeAccountId?: string | null;
  runtimeLastError?: string | null;
}

export interface AdminIntegrationGrantUpsertPayload {
  scopes?: string[];
  maxInstances?: number;
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

export interface AdminTelegramTestMessagePayload {
  chatId: string;
  message?: string;
}

export interface AdminTelegramConnectivityResult {
  status: "ok" | "error";
  effectivePath: string;
  proxyAttempted: boolean;
  proxySucceeded: boolean;
  directAttempted: boolean;
  directSucceeded: boolean;
  reasonCode?: string | null;
  proxyError?: string | null;
  directError?: string | null;
  botId?: string | null;
  username?: string | null;
  firstName?: string | null;
}

export interface ProjectIntegrationStatus {
  integrationKey: string;
  integrationType: "service" | "worker" | "notification" | "custom" | "platform" | "unknown";
  status: string;
  scopes: string[];
  maxInstances: number;
  credentialStatus?: "pending_sync" | "active" | "revoking" | "revoked" | null;
  credentialMasked?: string | null;
  runtimeStatus?: "pending_provision" | "active" | "revoking" | "revoked" | null;
  runtimeAccountId?: string | null;
  runtimeLastError?: string | null;
}

export interface ProjectTelegramBindingsSummary {
  groupChats: number;
  userDmChats: number;
}

export interface ProjectIntegrationsStatusEnvelopeData {
  items: ProjectIntegrationStatus[];
  telegram: ProjectTelegramBindingsSummary;
}

export interface ProjectIntegrationInstance {
  instanceId: string;
  integrationKey: string;
  displayName: string;
  isDefault: boolean;
  runtimeAccountId: string;
  runtimeStatus: string;
  runtimeLastError?: string | null;
  configurationUpdatedAtUtc?: string | null;
  provisionedAtUtc?: string | null;
  deprovisionedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ProjectIntegrationInstanceListData {
  integrationKey: string;
  maxInstances: number;
  items: ProjectIntegrationInstance[];
}

export interface ProjectIntegrationInstanceUpsertPayload {
  displayName?: string;
  makeDefault?: boolean;
  autoProvision?: boolean;
  proxyConfig?: {
    host: string;
    port: number;
    login: string;
    password: string;
  };
  mailConfig?: MailConfig;
}

export interface ProjectIntegrationUiSession {
  token: string;
  expiresAtUtc: string;
  iframeUrl: string;
}

export interface SteamIntegrationAccount {
  id: string;
  loginName: string;
  displayName?: string | null;
  steamId64?: string | null;
  email?: string | null;
  phoneMasked?: string | null;
  proxy?: string | null;
  folderName?: string | null;
  status?: string | null;
  note?: string | null;
  tags: string[];
  metadata: Record<string, string>;
  createdAt: string;
  updatedAt: string;
}

export interface SteamIntegrationAccountUpsertPayload {
  loginName: string;
  displayName?: string;
  email?: string;
  emailLogin?: string;
  emailPassword?: string;
  phoneMasked?: string;
  password?: string;
  loginPassword?: string;
  sharedSecret?: string;
  identitySecret?: string;
  guardRecoveryCode?: string;
  maFilePayload?: string;
  accessToken?: string;
  refreshToken?: string;
  authSessionId?: string;
  steamLoginSecure?: string;
  steamRememberLogin?: string;
  webCookie?: string;
  deviceId?: string;
  machineName?: string;
  familyViewPin?: string;
  countryCode?: string;
  timeZone?: string;
  sessionPayload?: string;
  recoveryPayload?: string;
  steamId64?: string;
  proxy?: string;
  folderName?: string;
  authHeaders?: Record<string, string>;
  tags?: string[];
  note?: string;
  metadata?: Record<string, string>;
  status?: string;
}

export interface SteamIntegrationJobCreatePayload {
  type: string;
  accountIds: string[];
  dryRun?: boolean;
  parallelism?: number;
  retryCount?: number;
  payload?: Record<string, string>;
}

export interface SteamIntegrationJobItem {
  id: string;
  jobId: string;
  accountId: string;
  status: string;
  attempt: number;
  errorText?: string | null;
  reasonCode?: string | null;
  retryable: boolean;
  startedAt?: string | null;
  finishedAt?: string | null;
  request: Record<string, string>;
  result: Record<string, string>;
}

export interface SteamIntegrationJob {
  id: string;
  type: string;
  status: string;
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  totalCount: number;
  successCount: number;
  failureCount: number;
  dryRun: boolean;
  payload: Record<string, string>;
  items?: SteamIntegrationJobItem[] | null;
}

export interface SteamWorkflowBlockCatalogItem {
  key: string;
  title: string;
  executionMode: "legacy_job" | "workflow_queue";
  legacyJobType?: string | null;
  requiredFields: string[];
  payloadSchemaHints: Record<string, unknown>;
}

export interface SteamWorkflowBlockExecution {
  id: string;
  projectId: string;
  blockKey: string;
  status: string;
  executionMode: string;
  legacyJobId?: string | null;
  enqueuedAtUtc: string;
  fallback: {
    used: boolean;
    mode: "legacy_job" | "workflow_queue";
  };
}

export interface OfferVariant {
  id: string;
  accountId: string;
  workerProductId: string;
  platform: string;
  observedTitle: string;
  observedDescription?: string | null;
  observedPrice: number;
  observedCurrency: string;
  priority: number;
  isActive: boolean;
}

export interface Offer {
  id: string;
  name: string;
  description?: string | null;
  status: "active" | "paused" | "archived";
  minPrice?: number | null;
  maxPrice?: number | null;
  averagePrice?: number | null;
  currencies: string[];
  variantCount: number;
  variants: OfferVariant[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface OfferCreatePayload {
  name: string;
  description?: string;
  status?: Offer["status"];
}

export interface OfferUpdatePayload {
  name?: string;
  description?: string;
  status?: Offer["status"];
}

export interface OfferVariantUpsertPayload {
  accountId: string;
  workerProductId: string;
  platform: string;
  observedTitle: string;
  observedDescription?: string;
  observedPrice: number;
  observedCurrency: string;
  priority: number;
  isActive: boolean;
}

export interface WorkflowNode {
  id: string;
  type:
    | "PurchaseStart"
    | "MessageStart"
    | "ReviewStart"
    | "Condition"
    | "SetVariables"
    | "LoadOffer"
    | "SelectAccountPriorityFallback"
    | "InvokeWorkerAction"
    | "InvokeCustomHttp"
    | "SteamAction"
    | "Task"
    | "SendBuyerResponse"
    | "Notify"
    | "End";
  name?: string;
  config?: Record<string, unknown>;
  ui?: WorkflowNodeUi;
}

export interface WorkflowEdge {
  id: string;
  source: string;
  sourceHandle?: string;
  target: string;
  targetHandle?: string;
  condition?: string;
}

export interface WorkflowNodeUi {
  position?: {
    x: number;
    y: number;
  };
}

export interface WorkflowDraftUi {
  viewport?: {
    x: number;
    y: number;
    zoom: number;
  };
  entryNodeId?: string;
}

export interface WorkflowDraft {
  version: string;
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  maxSteps: number;
  maxDurationSeconds: number;
  maxRetries: number;
  ui?: WorkflowDraftUi;
}

export interface WorkflowExecutionStep {
  nodeId: string;
  nodeType: string;
  stepIndex: number;
  status: string;
  startedAtUtc: string;
  finishedAtUtc?: string | null;
  outputJson?: string | null;
  error?: string | null;
}

export interface WorkflowExecution {
  id: string;
  sourceOrderId: string;
  workflowVersion: number;
  status: string;
  startedAtUtc: string;
  finishedAtUtc?: string | null;
  lastError?: string | null;
  steps: WorkflowExecutionStep[];
}

export interface WorkflowDraftEnvelopeData {
  draft: WorkflowDraft;
  status: "draft" | "published";
  publishedVersion: number;
  publishedAtUtc?: string | null;
}

export interface ProjectCustomHttpIntegration {
  id: string;
  name: string;
  baseUrl: string;
  status: "active" | "disabled";
  bearerTokenMasked: string;
  lastTestedAtUtc?: string | null;
  updatedAtUtc: string;
}

export interface ProjectCustomHttpIntegrationCreatePayload {
  name: string;
  baseUrl: string;
  bearerToken: string;
  status?: ProjectCustomHttpIntegration["status"];
  defaultHeaders?: Record<string, string>;
}

export interface ProjectCustomHttpIntegrationUpdatePayload {
  name?: string;
  baseUrl?: string;
  bearerToken?: string;
  status?: ProjectCustomHttpIntegration["status"];
  defaultHeaders?: Record<string, string>;
}

export interface ProjectCustomHttpIntegrationTestPayload {
  method?: string;
  path?: string;
  headers?: Record<string, string>;
  payload?: Record<string, unknown>;
}

export interface ProjectCustomHttpIntegrationTestResult {
  statusCode: number;
  endpoint: string;
  body?: string | null;
}

export interface AdminCustomHttpAllowlistEntry {
  id: string;
  hostPattern: string;
  isActive: boolean;
  note?: string | null;
  updatedAtUtc: string;
}

export interface AdminCustomHttpAllowlistUpsertPayload {
  hostPattern: string;
  isActive: boolean;
  note?: string;
}

export interface TelegramLinkCodeCreatePayload {
  bindingType?: "group" | "user";
}

export interface TelegramLinkCodePayload {
  code: string;
  bindingType: "group" | "user";
  expiresAtUtc: string;
}

export interface ProjectIntegrationRuntimeConfigurePayload {
  proxyConfig: ProxyConfig;
  mailConfig?: MailConfig;
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

interface AdminTelegramConnectivityEnvelope {
  requestId: string;
  status: string;
  effectivePath: string;
  proxyAttempted: boolean;
  proxySucceeded: boolean;
  directAttempted: boolean;
  directSucceeded: boolean;
  reasonCode?: string | null;
  proxyError?: string | null;
  directError?: string | null;
  botId?: string | null;
  username?: string | null;
  firstName?: string | null;
}

interface ProjectIntegrationsStatusEnvelope {
  requestId: string;
  items: ProjectIntegrationStatus[];
  telegram: ProjectTelegramBindingsSummary;
}

interface ProjectIntegrationInstanceListEnvelope {
  requestId: string;
  integrationKey: string;
  maxInstances: number;
  items: ProjectIntegrationInstance[];
}

interface ProjectIntegrationInstanceEnvelope {
  requestId: string;
  instance: ProjectIntegrationInstance;
}

interface ProjectIntegrationUiSessionEnvelope {
  requestId: string;
  token: string;
  expiresAtUtc: string;
  iframeUrl: string;
}

interface OfferEnvelope {
  requestId: string;
  offer: Offer;
}

interface OfferListEnvelope {
  requestId: string;
  items: Offer[];
}

interface WorkflowDraftEnvelope {
  requestId: string;
  draft: WorkflowDraft;
  status: "draft" | "published";
  publishedVersion: number;
  publishedAtUtc?: string | null;
}

interface WorkflowExecutionListEnvelope {
  requestId: string;
  items: WorkflowExecution[];
}

interface ProjectCustomHttpIntegrationEnvelope {
  requestId: string;
  integration: ProjectCustomHttpIntegration;
}

interface ProjectCustomHttpIntegrationListEnvelope {
  requestId: string;
  items: ProjectCustomHttpIntegration[];
}

interface ProjectCustomHttpIntegrationTestEnvelope {
  requestId: string;
  statusCode: number;
  endpoint: string;
  body?: string | null;
}

interface AdminCustomHttpAllowlistEnvelope {
  requestId: string;
  entry: AdminCustomHttpAllowlistEntry;
}

interface AdminCustomHttpAllowlistListEnvelope {
  requestId: string;
  items: AdminCustomHttpAllowlistEntry[];
}

interface TelegramLinkCodeEnvelope {
  requestId: string;
  code: string;
  bindingType: "group" | "user";
  expiresAtUtc: string;
}

interface SteamIntegrationAccountsEnvelope {
  requestId: string;
  items: SteamIntegrationAccount[];
  totalCount: number;
}

interface SteamIntegrationAccountEnvelope {
  requestId: string;
  account: SteamIntegrationAccount;
}

interface SteamIntegrationJobsEnvelope {
  requestId: string;
  items: SteamIntegrationJob[];
}

interface SteamWorkflowBlocksCatalogEnvelope {
  requestId: string;
  blocks: SteamWorkflowBlockCatalogItem[];
}

interface SteamWorkflowBlockEnqueueEnvelope {
  requestId: string;
  execution: SteamWorkflowBlockExecution;
  job?: SteamIntegrationJob | null;
}

interface SteamIntegrationJobEnvelope {
  requestId: string;
  job: SteamIntegrationJob;
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

async function requestAuthedEnvelope<TSuccess>(
  session: ApiSession,
  path: string,
  init: {
    method: "GET" | "PUT" | "POST" | "DELETE" | "PATCH";
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

async function requestAdminEnvelope<TSuccess>(
  session: ApiSession,
  path: string,
  init: {
    method: "GET" | "PUT" | "POST" | "DELETE" | "PATCH";
    body?: unknown;
    idempotent?: boolean;
  },
) {
  return requestAuthedEnvelope<TSuccess>(session, path, init);
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

export async function triggerAdminIntegrationRuntimeRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  operation: "provision" | "deprovision" | "restart",
) {
  return requestAdminEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/admin/integrations/projects/${encodeURIComponent(projectId)}/grants/${encodeURIComponent(integrationKey)}/runtime/${operation}`,
    {
      method: "POST",
      idempotent: true,
    },
  );
}

export async function sendAdminTelegramTestMessageRequest(
  session: ApiSession,
  payload: AdminTelegramTestMessagePayload,
) {
  return requestAdminEnvelope<{ requestId: string; status: string }>(
    session,
    "/v1/admin/integrations/telegram/test-message",
    {
      method: "POST",
      body: payload,
    },
  );
}

export async function checkAdminTelegramConnectivityRequest(
  session: ApiSession,
): Promise<AdminTelegramConnectivityResult> {
  const response = await requestAdminEnvelope<AdminTelegramConnectivityEnvelope>(
    session,
    "/v1/admin/integrations/telegram/test-connectivity",
    {
      method: "POST",
    },
  );

  return {
    status: response.status === "error" ? "error" : "ok",
    effectivePath: response.effectivePath,
    proxyAttempted: response.proxyAttempted,
    proxySucceeded: response.proxySucceeded,
    directAttempted: response.directAttempted,
    directSucceeded: response.directSucceeded,
    reasonCode: response.reasonCode,
    proxyError: response.proxyError,
    directError: response.directError,
    botId: response.botId,
    username: response.username,
    firstName: response.firstName,
  };
}

export async function listProjectIntegrationsStatusRequest(
  session: ApiSession,
  projectId: string,
): Promise<ProjectIntegrationsStatusEnvelopeData> {
  const response = await requestAuthedEnvelope<ProjectIntegrationsStatusEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/status`,
    {
      method: "GET",
    },
  );

  return {
    items: response.items ?? [],
    telegram: response.telegram ?? { groupChats: 0, userDmChats: 0 },
  };
}

export async function listProjectIntegrationInstancesRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
): Promise<ProjectIntegrationInstanceListData> {
  const response = await requestAuthedEnvelope<ProjectIntegrationInstanceListEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/instances`,
    {
      method: "GET",
    },
  );

  return {
    integrationKey: response.integrationKey ?? integrationKey,
    maxInstances: response.maxInstances ?? 1,
    items: response.items ?? [],
  };
}

export async function createProjectIntegrationInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  payload: ProjectIntegrationInstanceUpsertPayload,
) {
  const response = await requestAuthedEnvelope<ProjectIntegrationInstanceEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/instances`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return response.instance;
}

export async function updateProjectIntegrationInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  payload: ProjectIntegrationInstanceUpsertPayload,
) {
  const response = await requestAuthedEnvelope<ProjectIntegrationInstanceEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/instances/${encodeURIComponent(instanceId)}`,
    {
      method: "PATCH",
      body: payload,
      idempotent: true,
    },
  );

  return response.instance;
}

export async function deleteProjectIntegrationInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
) {
  return requestAuthedEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/instances/${encodeURIComponent(instanceId)}`,
    {
      method: "DELETE",
      idempotent: true,
    },
  );
}

export async function triggerProjectIntegrationInstanceRuntimeRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  operation: "provision" | "deprovision" | "restart",
) {
  return requestAuthedEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/instances/${encodeURIComponent(instanceId)}/runtime/${operation}`,
    {
      method: "POST",
      idempotent: true,
    },
  );
}

export async function invokeProjectIntegrationInstanceActionRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  scope: "read" | "jobs",
  payload?: Record<string, unknown>,
) {
  const response = await requestAuthedEnvelope<{ requestId: string; result: Record<string, unknown> }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/instances/${encodeURIComponent(instanceId)}/actions/${scope}`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return response.result ?? {};
}

export async function createProjectIntegrationInstanceUiSessionRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
): Promise<ProjectIntegrationUiSession> {
  const response = await requestAuthedEnvelope<ProjectIntegrationUiSessionEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/instances/${encodeURIComponent(instanceId)}/ui/session`,
    {
      method: "POST",
      idempotent: true,
    },
  );

  return {
    token: response.token,
    expiresAtUtc: response.expiresAtUtc,
    iframeUrl: response.iframeUrl,
  };
}

async function invokeSteamActionByInstance(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  scope: "read" | "jobs",
  payload: Record<string, unknown>,
) {
  return invokeProjectIntegrationInstanceActionRequest(
    session,
    projectId,
    integrationKey,
    instanceId,
    scope,
    payload,
  );
}

export async function listSteamIntegrationAccountsByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  params?: {
    query?: string;
    status?: string;
    page?: number;
    pageSize?: number;
  },
) {
  const payload: Record<string, unknown> = {
    operation: "accounts.list",
    page: typeof params?.page === "number" ? params.page : 1,
    pageSize: typeof params?.pageSize === "number" ? params.pageSize : 50,
  };
  if (params?.query?.trim()) {
    payload.query = params.query.trim();
  }
  if (params?.status?.trim()) {
    payload.status = params.status.trim();
  }

  const result = await invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "read",
    payload,
  );

  return {
    items: Array.isArray(result.items) ? (result.items as SteamIntegrationAccount[]) : [],
    totalCount:
      typeof result.totalCount === "number"
        ? result.totalCount
        : Array.isArray(result.items)
          ? result.items.length
          : 0,
  };
}

export async function createSteamIntegrationAccountByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  payload: SteamIntegrationAccountUpsertPayload,
) {
  const result = await invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "jobs",
    {
      operation: "accounts.create",
      account: payload,
    },
  );

  return result.account as SteamIntegrationAccount;
}

export async function updateSteamIntegrationAccountByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  accountId: string,
  payload: SteamIntegrationAccountUpsertPayload,
) {
  const result = await invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "jobs",
    {
      operation: "accounts.update",
      accountId,
      account: payload,
    },
  );

  return result.account as SteamIntegrationAccount;
}

export async function archiveSteamIntegrationAccountByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  accountId: string,
) {
  return invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "jobs",
    {
      operation: "accounts.archive",
      accountId,
    },
  );
}

export async function listSteamIntegrationJobsByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  take = 30,
) {
  const result = await invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "read",
    {
      operation: "jobs.list",
      take,
    },
  );

  return Array.isArray(result.jobs) ? (result.jobs as SteamIntegrationJob[]) : [];
}

export async function listSteamWorkflowBlocksCatalogByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
) {
  const result = await invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "read",
    {
      operation: "workflow.blocks.catalog",
    },
  );

  return Array.isArray(result.blocks) ? (result.blocks as SteamWorkflowBlockCatalogItem[]) : [];
}

export async function enqueueSteamWorkflowBlockByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  payload: {
    blockKey: string;
    accountIds?: string[];
    dryRun?: boolean;
    parallelism?: number;
    retryCount?: number;
    jobPayload?: Record<string, unknown>;
  },
) {
  const result = await invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "jobs",
    {
      operation: "workflow.blocks.enqueue",
      ...payload,
    },
  );

  return {
    execution: result.execution as SteamWorkflowBlockExecution,
    job: (result.job as SteamIntegrationJob | null | undefined) ?? null,
  };
}

export async function createSteamIntegrationJobByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  payload: SteamIntegrationJobCreatePayload,
) {
  const result = await invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "jobs",
    {
      operation: "jobs.create",
      job: payload,
    },
  );

  return result.job as SteamIntegrationJob;
}

export async function cancelSteamIntegrationJobByInstanceRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  instanceId: string,
  jobId: string,
) {
  return invokeSteamActionByInstance(
    session,
    projectId,
    integrationKey,
    instanceId,
    "jobs",
    {
      operation: "jobs.cancel",
      jobId,
    },
  );
}

export async function listSteamIntegrationAccountsRequest(
  session: ApiSession,
  projectId: string,
  params?: {
    query?: string;
    status?: string;
    page?: number;
    pageSize?: number;
  },
) {
  const search = new URLSearchParams();
  if (params?.query?.trim()) {
    search.set("query", params.query.trim());
  }

  if (params?.status?.trim()) {
    search.set("status", params.status.trim());
  }

  if (typeof params?.page === "number") {
    search.set("page", String(params.page));
  }

  if (typeof params?.pageSize === "number") {
    search.set("pageSize", String(params.pageSize));
  }

  const query = search.size > 0 ? `?${search.toString()}` : "";
  const response = await requestAuthedEnvelope<SteamIntegrationAccountsEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/steam/accounts${query}`,
    {
      method: "GET",
    },
  );

  return {
    items: response.items ?? [],
    totalCount: response.totalCount ?? response.items?.length ?? 0,
  };
}

export async function createSteamIntegrationAccountRequest(
  session: ApiSession,
  projectId: string,
  payload: SteamIntegrationAccountUpsertPayload,
) {
  const response = await requestAuthedEnvelope<SteamIntegrationAccountEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/steam/accounts`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return response.account;
}

export async function updateSteamIntegrationAccountRequest(
  session: ApiSession,
  projectId: string,
  accountId: string,
  payload: SteamIntegrationAccountUpsertPayload,
) {
  const response = await requestAuthedEnvelope<SteamIntegrationAccountEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/steam/accounts/${encodeURIComponent(accountId)}`,
    {
      method: "PATCH",
      body: payload,
      idempotent: true,
    },
  );

  return response.account;
}

export async function archiveSteamIntegrationAccountRequest(
  session: ApiSession,
  projectId: string,
  accountId: string,
) {
  return requestAuthedEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/steam/accounts/${encodeURIComponent(accountId)}`,
    {
      method: "DELETE",
      idempotent: true,
    },
  );
}

export async function listSteamIntegrationJobsRequest(
  session: ApiSession,
  projectId: string,
  take = 30,
) {
  const response = await requestAuthedEnvelope<SteamIntegrationJobsEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/steam/jobs?take=${encodeURIComponent(String(take))}`,
    {
      method: "GET",
    },
  );

  return response.items ?? [];
}

export async function createSteamIntegrationJobRequest(
  session: ApiSession,
  projectId: string,
  payload: SteamIntegrationJobCreatePayload,
) {
  const response = await requestAuthedEnvelope<SteamIntegrationJobEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/steam/jobs`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return response.job;
}

export async function cancelSteamIntegrationJobRequest(
  session: ApiSession,
  projectId: string,
  jobId: string,
) {
  return requestAuthedEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/steam/jobs/${encodeURIComponent(jobId)}/cancel`,
    {
      method: "POST",
      idempotent: true,
    },
  );
}

export async function triggerProjectIntegrationRuntimeRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  operation: "provision" | "deprovision" | "restart",
) {
  return requestAuthedEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/runtime/${operation}`,
    {
      method: "POST",
      idempotent: true,
    },
  );
}

export async function configureProjectIntegrationRuntimeRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  payload: ProjectIntegrationRuntimeConfigurePayload,
) {
  return requestAuthedEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/runtime/configure`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );
}

export async function invokeProjectIntegrationActionRequest(
  session: ApiSession,
  projectId: string,
  integrationKey: string,
  scope: "read" | "jobs",
  payload?: Record<string, unknown>,
) {
  const response = await requestAuthedEnvelope<{ requestId: string; result: Record<string, unknown> }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/${encodeURIComponent(integrationKey)}/actions/${scope}`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return response.result ?? {};
}

export async function createTelegramLinkCodeRequest(
  session: ApiSession,
  projectId: string,
  payload: TelegramLinkCodeCreatePayload,
): Promise<TelegramLinkCodePayload> {
  const response = await requestAuthedEnvelope<TelegramLinkCodeEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/telegram/link-codes`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return {
    code: response.code,
    bindingType: response.bindingType,
    expiresAtUtc: response.expiresAtUtc,
  };
}

export async function listOffersRequest(
  session: ApiSession,
  projectId: string,
): Promise<Offer[]> {
  const response = await requestAuthedEnvelope<OfferListEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers`,
    {
      method: "GET",
    },
  );

  return response.items ?? [];
}

export async function createOfferRequest(
  session: ApiSession,
  projectId: string,
  payload: OfferCreatePayload,
): Promise<Offer> {
  const response = await requestAuthedEnvelope<OfferEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return response.offer;
}

export async function getOfferRequest(
  session: ApiSession,
  projectId: string,
  offerId: string,
): Promise<Offer> {
  const response = await requestAuthedEnvelope<OfferEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers/${encodeURIComponent(offerId)}`,
    {
      method: "GET",
    },
  );

  return response.offer;
}

export async function updateOfferRequest(
  session: ApiSession,
  projectId: string,
  offerId: string,
  payload: OfferUpdatePayload,
): Promise<Offer> {
  const response = await requestAuthedEnvelope<OfferEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers/${encodeURIComponent(offerId)}`,
    {
      method: "PATCH",
      body: payload,
      idempotent: true,
    },
  );

  return response.offer;
}

export async function deleteOfferRequest(
  session: ApiSession,
  projectId: string,
  offerId: string,
) {
  return requestAuthedEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers/${encodeURIComponent(offerId)}`,
    {
      method: "DELETE",
      idempotent: true,
    },
  );
}

export async function replaceOfferVariantsRequest(
  session: ApiSession,
  projectId: string,
  offerId: string,
  items: OfferVariantUpsertPayload[],
): Promise<Offer> {
  const response = await requestAuthedEnvelope<OfferEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers/${encodeURIComponent(offerId)}/variants`,
    {
      method: "PUT",
      body: { items },
      idempotent: true,
    },
  );

  return response.offer;
}

export async function getOfferWorkflowDraftRequest(
  session: ApiSession,
  projectId: string,
  offerId: string,
): Promise<WorkflowDraftEnvelopeData> {
  const response = await requestAuthedEnvelope<WorkflowDraftEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers/${encodeURIComponent(offerId)}/workflow/draft`,
    {
      method: "GET",
    },
  );

  return {
    draft: response.draft,
    status: response.status,
    publishedVersion: response.publishedVersion,
    publishedAtUtc: response.publishedAtUtc,
  };
}

export async function saveOfferWorkflowDraftRequest(
  session: ApiSession,
  projectId: string,
  offerId: string,
  draft: WorkflowDraft,
): Promise<WorkflowDraftEnvelopeData> {
  const response = await requestAuthedEnvelope<WorkflowDraftEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers/${encodeURIComponent(offerId)}/workflow/draft`,
    {
      method: "PUT",
      body: draft,
      idempotent: true,
    },
  );

  return {
    draft: response.draft,
    status: response.status,
    publishedVersion: response.publishedVersion,
    publishedAtUtc: response.publishedAtUtc,
  };
}

export async function publishOfferWorkflowRequest(
  session: ApiSession,
  projectId: string,
  offerId: string,
): Promise<WorkflowDraftEnvelopeData> {
  const response = await requestAuthedEnvelope<WorkflowDraftEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers/${encodeURIComponent(offerId)}/workflow/publish`,
    {
      method: "POST",
      idempotent: true,
    },
  );

  return {
    draft: response.draft,
    status: response.status,
    publishedVersion: response.publishedVersion,
    publishedAtUtc: response.publishedAtUtc,
  };
}

export async function listOfferWorkflowExecutionsRequest(
  session: ApiSession,
  projectId: string,
  offerId: string,
): Promise<WorkflowExecution[]> {
  const response = await requestAuthedEnvelope<WorkflowExecutionListEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/offers/${encodeURIComponent(offerId)}/workflow/executions`,
    {
      method: "GET",
    },
  );

  // `outputJson` может быть очень тяжелым; UI истории его не показывает,
  // поэтому обнуляем поле и снижаем давление на память в браузере.
  return (response.items ?? []).map((execution) => ({
    ...execution,
    steps: (execution.steps ?? []).map(({ outputJson: _outputJson, ...step }) => step),
  }));
}

export async function listProjectCustomHttpIntegrationsRequest(
  session: ApiSession,
  projectId: string,
): Promise<ProjectCustomHttpIntegration[]> {
  const response = await requestAuthedEnvelope<ProjectCustomHttpIntegrationListEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/custom-http`,
    {
      method: "GET",
    },
  );

  return response.items ?? [];
}

export async function createProjectCustomHttpIntegrationRequest(
  session: ApiSession,
  projectId: string,
  payload: ProjectCustomHttpIntegrationCreatePayload,
): Promise<ProjectCustomHttpIntegration> {
  const response = await requestAuthedEnvelope<ProjectCustomHttpIntegrationEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/custom-http`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return response.integration;
}

export async function updateProjectCustomHttpIntegrationRequest(
  session: ApiSession,
  projectId: string,
  integrationId: string,
  payload: ProjectCustomHttpIntegrationUpdatePayload,
): Promise<ProjectCustomHttpIntegration> {
  const response = await requestAuthedEnvelope<ProjectCustomHttpIntegrationEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/custom-http/${encodeURIComponent(integrationId)}`,
    {
      method: "PATCH",
      body: payload,
      idempotent: true,
    },
  );

  return response.integration;
}

export async function deleteProjectCustomHttpIntegrationRequest(
  session: ApiSession,
  projectId: string,
  integrationId: string,
) {
  return requestAuthedEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/custom-http/${encodeURIComponent(integrationId)}`,
    {
      method: "DELETE",
      idempotent: true,
    },
  );
}

export async function testProjectCustomHttpIntegrationRequest(
  session: ApiSession,
  projectId: string,
  integrationId: string,
  payload: ProjectCustomHttpIntegrationTestPayload,
): Promise<ProjectCustomHttpIntegrationTestResult> {
  const response = await requestAuthedEnvelope<ProjectCustomHttpIntegrationTestEnvelope>(
    session,
    `/v1/projects/${encodeURIComponent(projectId)}/integrations/custom-http/${encodeURIComponent(integrationId)}/test`,
    {
      method: "POST",
      body: payload,
      idempotent: true,
    },
  );

  return {
    statusCode: response.statusCode,
    endpoint: response.endpoint,
    body: response.body,
  };
}

export async function listAdminCustomHttpAllowlistRequest(
  session: ApiSession,
): Promise<AdminCustomHttpAllowlistEntry[]> {
  const response = await requestAdminEnvelope<AdminCustomHttpAllowlistListEnvelope>(
    session,
    "/v1/admin/integrations/custom-http/allowlist",
    {
      method: "GET",
    },
  );

  return response.items ?? [];
}

export async function upsertAdminCustomHttpAllowlistEntryRequest(
  session: ApiSession,
  entryId: string,
  payload: AdminCustomHttpAllowlistUpsertPayload,
): Promise<AdminCustomHttpAllowlistEntry> {
  const response = await requestAdminEnvelope<AdminCustomHttpAllowlistEnvelope>(
    session,
    `/v1/admin/integrations/custom-http/allowlist/${encodeURIComponent(entryId)}`,
    {
      method: "PUT",
      body: payload,
      idempotent: true,
    },
  );

  return response.entry;
}

export async function deleteAdminCustomHttpAllowlistEntryRequest(
  session: ApiSession,
  entryId: string,
) {
  return requestAdminEnvelope<{ requestId: string; status: string }>(
    session,
    `/v1/admin/integrations/custom-http/allowlist/${encodeURIComponent(entryId)}`,
    {
      method: "DELETE",
      idempotent: true,
    },
  );
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
