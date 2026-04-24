import {
  type Account,
  type AccountCreateRequest,
  type ErrorResponse,
  type GenericObjectRequest,
  type GenericObjectResponseData,
  type Project,
  type ProxyConfig,
  type ProxyCredentialsMasked,
  changePlan,
  createAccount,
  createPayment,
  createProject,
  getAccountProxyCredentialsMasked,
  listAccounts,
  listProjects,
  proxyAccountApiAction,
  purchaseAddon,
  revealAccountProxyCredentials,
  updateAccountProxyCredentials,
} from "@/generated/external-api";
import { createIdempotencyKey } from "@/lib/idempotency";

export interface ApiSession {
  token: string;
  baseUrl: string;
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
  const headers = new Headers();
  headers.set("Authorization", `Bearer ${session.token}`);
  headers.set("Accept", "application/json");

  if (includeIdempotencyKey) {
    headers.set("Idempotency-Key", createIdempotencyKey());
  }

  return { headers };
}

function unwrapOrThrow<TSuccess>(response: ApiResponseEnvelope<TSuccess>): TSuccess {
  if (response.status >= 200 && response.status < 300) {
    return response.data as TSuccess;
  }

  const errorPayload = response.data as ErrorResponse;
  const errorCode = errorPayload?.errorCode ?? `HTTP_${response.status}`;
  const message = errorPayload?.message ?? "Неизвестная ошибка API.";
  const requestId = errorPayload?.requestId ?? "n/a";

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

export function buildRouteKey(accountId: string) {
  return `rk.${accountId.replaceAll("-", "")}`;
}
