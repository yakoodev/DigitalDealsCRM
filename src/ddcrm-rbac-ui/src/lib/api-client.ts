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
  getAccountProxyCredentialsMasked,
  listAccounts,
  listProjectAccountTypes,
  listProjects,
  proxyAccountApiAction,
  purchaseAddon,
  revealAccountProxyCredentials,
  removeMember,
  updateAccountProxyCredentials,
} from "@/generated/external-api";
import { createIdempotencyKey } from "@/lib/idempotency";

export interface ApiSession {
  token: string;
  baseUrl: string;
}

export type ProjectAccountType = AccountType;

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
        `UNAUTHORIZED: JWT отклонён (${details.authFailure}). Перелогиньтесь через Demo вход. (requestId: ${requestId})`,
      );
    }
  }

  const errorCode = errorPayload?.errorCode ?? `HTTP_${response.status}`;
  const message = errorPayload?.message ?? "Неизвестная ошибка API.";

  throw new Error(`${errorCode}: ${message} (requestId: ${requestId})`);
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
