"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import type { AccountCreateRequest, ProxyConfig } from "@/generated/external-api";
import {
  type ApiSession,
  buildRouteKey,
  changePlanRequest,
  createAccountRequest,
  createPaymentRequest,
  createProjectRequest,
  getMaskedProxyCredentialsRequest,
  listAccountsRequest,
  listProjectsRequest,
  proxyAccountActionRequest,
  purchaseAddonRequest,
  revealProxyCredentialsRequest,
  updateProxyCredentialsRequest,
} from "@/lib/api-client";
import {
  hasPermission,
  projectPermissions,
  projectRoles,
  type ProjectRole,
} from "@/lib/rbac";

const SESSION_STORAGE_KEY = "ddcrm-rbac-ui.session";
const DEFAULT_EXTERNAL_API_BASE_URL = "http://localhost:5073";

interface StoredSessionState {
  token: string;
  baseUrl: string;
  role: ProjectRole;
}

function parseJwtSubject(token: string) {
  const parts = token.split(".");
  if (parts.length !== 3) {
    return null;
  }

  try {
    const base64 = parts[1].replaceAll("-", "+").replaceAll("_", "/");
    const json = JSON.parse(atob(base64));
    return typeof json.sub === "string" ? json.sub : null;
  } catch {
    return null;
  }
}

function parseJsonInput(value: string) {
  if (!value.trim()) {
    return undefined;
  }

  return JSON.parse(value) as Record<string, unknown>;
}

function readStoredSessionState(): StoredSessionState | null {
  if (typeof window === "undefined") {
    return null;
  }

  const rawValue = localStorage.getItem(SESSION_STORAGE_KEY);
  if (!rawValue) {
    return null;
  }

  try {
    const parsed = JSON.parse(rawValue) as StoredSessionState;
    if (!parsed.token || !parsed.baseUrl || !projectRoles.includes(parsed.role)) {
      return null;
    }

    return parsed;
  } catch {
    localStorage.removeItem(SESSION_STORAGE_KEY);
    return null;
  }
}

export default function HomePage() {
  const queryClient = useQueryClient();

  const [storedSession] = useState<StoredSessionState | null>(() =>
    readStoredSessionState(),
  );

  const [tokenDraft, setTokenDraft] = useState(storedSession?.token ?? "");
  const [baseUrlDraft, setBaseUrlDraft] = useState(
    storedSession?.baseUrl ?? DEFAULT_EXTERNAL_API_BASE_URL,
  );
  const [roleDraft, setRoleDraft] = useState<ProjectRole>(
    storedSession?.role ?? "owner",
  );

  const [session, setSession] = useState<ApiSession | null>(
    storedSession
      ? {
          token: storedSession.token,
          baseUrl: storedSession.baseUrl,
        }
      : null,
  );
  const [activeRole, setActiveRole] = useState<ProjectRole>(
    storedSession?.role ?? "owner",
  );
  const [statusMessage, setStatusMessage] = useState(
    storedSession
      ? "Сессия восстановлена из localStorage."
      : "Сессия не инициализирована.",
  );

  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [selectedAccountId, setSelectedAccountId] = useState("");

  const [projectNameInput, setProjectNameInput] = useState("DDCRM Demo Project");

  const [createPlatformInput, setCreatePlatformInput] = useState("avito");
  const [createDisplayNameInput, setCreateDisplayNameInput] = useState("Demo Marketplace Account");
  const [createProxyHostInput, setCreateProxyHostInput] = useState("");
  const [createProxyPortInput, setCreateProxyPortInput] = useState("1508");
  const [createProxyLoginInput, setCreateProxyLoginInput] = useState("");
  const [createProxyPasswordInput, setCreateProxyPasswordInput] = useState("");

  const [updateReasonInput, setUpdateReasonInput] = useState("UI proxy update");
  const [updateProxyHostInput, setUpdateProxyHostInput] = useState("");
  const [updateProxyPortInput, setUpdateProxyPortInput] = useState("1508");
  const [updateProxyLoginInput, setUpdateProxyLoginInput] = useState("");
  const [updateProxyPasswordInput, setUpdateProxyPasswordInput] = useState("");

  const [revealReasonInput, setRevealReasonInput] = useState("UI proxy reveal");
  const [revealedProxyConfig, setRevealedProxyConfig] = useState<ProxyConfig | null>(
    null,
  );

  const [paymentAmountInput, setPaymentAmountInput] = useState("99.99");
  const [paymentCurrencyInput, setPaymentCurrencyInput] = useState("USD");
  const [planKeyInput, setPlanKeyInput] = useState("pro");
  const [addonIdInput, setAddonIdInput] = useState("extra-workers");

  const [gatewayRouteKeyInput, setGatewayRouteKeyInput] = useState("");
  const [gatewayActionInput, setGatewayActionInput] = useState("ext.account.health.check");
  const [gatewayPayloadInput, setGatewayPayloadInput] = useState("{\"ping\":true}");
  const [gatewayResult, setGatewayResult] = useState<Record<string, unknown> | null>(
    null,
  );

  const [activityLog, setActivityLog] = useState<string[]>([]);

  const canManageAccountLifecycle = hasPermission(
    activeRole,
    projectPermissions.accountsLifecycleManage,
  );
  const canRevealProxy = hasPermission(
    activeRole,
    projectPermissions.proxyCredentialsReveal,
  );
  const canUpdateProxy = hasPermission(
    activeRole,
    projectPermissions.proxyCredentialsUpdate,
  );
  const canViewBilling = hasPermission(activeRole, projectPermissions.billingView);
  const canChangeBilling = hasPermission(
    activeRole,
    projectPermissions.billingChangePlan,
  );
  const canUseGateway = hasPermission(activeRole, projectPermissions.workersOperate);

  const logEvent = (message: string) => {
    setActivityLog((previous) => [
      `${new Date().toLocaleTimeString("ru-RU")} - ${message}`,
      ...previous.slice(0, 11),
    ]);
  };

  const projectsQuery = useQuery({
    queryKey: ["projects", session?.baseUrl, session?.token],
    queryFn: () => listProjectsRequest(session as ApiSession),
    enabled: Boolean(session),
  });

  const projectOptions = useMemo(() => projectsQuery.data ?? [], [projectsQuery.data]);

  const effectiveProjectId = useMemo(() => {
    if (projectOptions.length === 0) {
      return "";
    }

    const isSelectedProjectAvailable = projectOptions.some(
      (project) => project.id === selectedProjectId,
    );

    return isSelectedProjectAvailable ? selectedProjectId : projectOptions[0].id;
  }, [projectOptions, selectedProjectId]);

  const accountsQuery = useQuery({
    queryKey: ["accounts", session?.baseUrl, session?.token, effectiveProjectId],
    queryFn: () =>
      listAccountsRequest(session as ApiSession, effectiveProjectId),
    enabled: Boolean(session && effectiveProjectId),
  });

  const accountOptions = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data]);

  const effectiveAccountId = useMemo(() => {
    if (accountOptions.length === 0) {
      return "";
    }

    const isSelectedAccountAvailable = accountOptions.some(
      (account) => account.id === selectedAccountId,
    );

    return isSelectedAccountAvailable ? selectedAccountId : accountOptions[0].id;
  }, [accountOptions, selectedAccountId]);

  const effectiveGatewayRouteKey = useMemo(() => {
    const normalized = gatewayRouteKeyInput.trim();
    if (normalized) {
      return normalized;
    }

    return effectiveAccountId ? buildRouteKey(effectiveAccountId) : "";
  }, [effectiveAccountId, gatewayRouteKeyInput]);

  const maskedProxyQuery = useQuery({
    queryKey: [
      "masked-proxy",
      session?.baseUrl,
      session?.token,
      effectiveProjectId,
      effectiveAccountId,
    ],
    queryFn: () =>
      getMaskedProxyCredentialsRequest(
        session as ApiSession,
        effectiveProjectId,
        effectiveAccountId,
      ),
    enabled: Boolean(session && effectiveProjectId && effectiveAccountId),
  });

  const createProjectMutation = useMutation({
    mutationFn: async () => {
      const normalizedName = projectNameInput.trim();
      if (!normalizedName) {
        throw new Error("Название проекта обязательно.");
      }

      return createProjectRequest(session as ApiSession, normalizedName);
    },
    onSuccess: async (project) => {
      setStatusMessage(`Проект "${project.name}" создан.`);
      logEvent(`createProject -> ${project.id}`);
      await queryClient.invalidateQueries({ queryKey: ["projects"] });
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка createProject.");
    },
  });

  const createAccountMutation = useMutation({
    mutationFn: async () => {
      if (!effectiveProjectId) {
        throw new Error("Сначала выберите проект.");
      }

      const proxyPort = Number(createProxyPortInput);
      if (!Number.isInteger(proxyPort) || proxyPort < 1 || proxyPort > 65_535) {
        throw new Error("Порт proxy должен быть в диапазоне 1..65535.");
      }

      const payload: AccountCreateRequest = {
        platform: createPlatformInput.trim(),
        displayName: createDisplayNameInput.trim(),
        proxyConfig: {
          host: createProxyHostInput.trim(),
          port: proxyPort,
          login: createProxyLoginInput.trim(),
          password: createProxyPasswordInput.trim(),
        },
      };

      if (!payload.platform || !payload.displayName) {
        throw new Error("platform и displayName обязательны.");
      }

      if (!payload.proxyConfig.host || !payload.proxyConfig.login || !payload.proxyConfig.password) {
        throw new Error("Все поля proxyConfig обязательны.");
      }

      return createAccountRequest(
        session as ApiSession,
        effectiveProjectId,
        payload,
      );
    },
    onSuccess: async (account) => {
      setStatusMessage(`Аккаунт "${account.displayName}" создан.`);
      logEvent(`createAccount -> ${account.id}`);
      await queryClient.invalidateQueries({ queryKey: ["accounts"] });
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка createAccount.");
    },
  });

  const updateProxyMutation = useMutation({
    mutationFn: async () => {
      if (!effectiveProjectId || !effectiveAccountId) {
        throw new Error("Нужно выбрать проект и аккаунт.");
      }

      const proxyPort = Number(updateProxyPortInput);
      if (!Number.isInteger(proxyPort) || proxyPort < 1 || proxyPort > 65_535) {
        throw new Error("Порт proxy должен быть в диапазоне 1..65535.");
      }

      await updateProxyCredentialsRequest(
        session as ApiSession,
        effectiveProjectId,
        effectiveAccountId,
        {
          reason: updateReasonInput.trim(),
          proxyConfig: {
            host: updateProxyHostInput.trim(),
            port: proxyPort,
            login: updateProxyLoginInput.trim(),
            password: updateProxyPasswordInput.trim(),
          },
        },
      );
    },
    onSuccess: async () => {
      setStatusMessage("Proxy credentials обновлены.");
      logEvent(`updateAccountProxyCredentials -> ${effectiveAccountId}`);
      await queryClient.invalidateQueries({ queryKey: ["accounts"] });
      await queryClient.invalidateQueries({ queryKey: ["masked-proxy"] });
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error ? error.message : "Ошибка updateAccountProxyCredentials.",
      );
    },
  });

  const revealProxyMutation = useMutation({
    mutationFn: async () => {
      if (!effectiveProjectId || !effectiveAccountId) {
        throw new Error("Нужно выбрать проект и аккаунт.");
      }

      return revealProxyCredentialsRequest(
        session as ApiSession,
        effectiveProjectId,
        effectiveAccountId,
        { reason: revealReasonInput.trim() },
      );
    },
    onSuccess: (proxyConfig) => {
      setRevealedProxyConfig(proxyConfig);
      setStatusMessage("Полные proxy credentials получены.");
      logEvent(`revealAccountProxyCredentials -> ${effectiveAccountId}`);
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error ? error.message : "Ошибка revealAccountProxyCredentials.",
      );
    },
  });

  const createPaymentMutation = useMutation({
    mutationFn: async () => {
      if (!effectiveProjectId) {
        throw new Error("Сначала выберите проект.");
      }

      const amount = Number(paymentAmountInput);
      if (Number.isNaN(amount) || amount <= 0) {
        throw new Error("Сумма платежа должна быть числом больше нуля.");
      }

      return createPaymentRequest(session as ApiSession, effectiveProjectId, {
        amount,
        currency: paymentCurrencyInput.trim() || "USD",
        operation: "ui.create-payment",
      });
    },
    onSuccess: (result) => {
      setStatusMessage("Платёж создан через Core/Billing.");
      logEvent(`createPayment -> ${JSON.stringify(result ?? {})}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка createPayment.");
    },
  });

  const changePlanMutation = useMutation({
    mutationFn: async () => {
      if (!effectiveProjectId) {
        throw new Error("Сначала выберите проект.");
      }

      await changePlanRequest(session as ApiSession, effectiveProjectId, {
        planKey: planKeyInput.trim(),
        reason: "ui.change-plan",
      });
    },
    onSuccess: () => {
      setStatusMessage("План проекта изменён.");
      logEvent(`changePlan -> ${effectiveProjectId}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка changePlan.");
    },
  });

  const purchaseAddonMutation = useMutation({
    mutationFn: async () => {
      if (!effectiveProjectId) {
        throw new Error("Сначала выберите проект.");
      }

      return purchaseAddonRequest(
        session as ApiSession,
        effectiveProjectId,
        addonIdInput.trim(),
        { source: "ui.purchase-addon" },
      );
    },
    onSuccess: (result) => {
      setStatusMessage("Add-on приобретён.");
      logEvent(`purchaseAddon -> ${JSON.stringify(result ?? {})}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка purchaseAddon.");
    },
  });

  const gatewayMutation = useMutation({
    mutationFn: async () => {
      const routeKey = effectiveGatewayRouteKey;
      const action = gatewayActionInput.trim();

      if (!routeKey || !action) {
        throw new Error("routeKey и action обязательны.");
      }

      const parsedPayload = parseJsonInput(gatewayPayloadInput);
      return proxyAccountActionRequest(
        session as ApiSession,
        routeKey,
        action,
        parsedPayload,
      );
    },
    onSuccess: (result) => {
      setGatewayResult(result ?? {});
      setStatusMessage("Gateway proxy вызов выполнен.");
      logEvent(`proxyAccountApiAction -> ${gatewayActionInput}`);
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error ? error.message : "Ошибка proxyAccountApiAction.",
      );
    },
  });

  const jwtSubject = useMemo(() => parseJwtSubject(tokenDraft), [tokenDraft]);

  const applySession = () => {
    const normalizedToken = tokenDraft.trim();
    const normalizedBaseUrl = baseUrlDraft.trim().replace(/\/+$/, "");

    if (!normalizedToken) {
      setStatusMessage("JWT токен обязателен.");
      return;
    }

    if (!normalizedBaseUrl) {
      setStatusMessage("Base URL обязателен.");
      return;
    }

    const stored: StoredSessionState = {
      token: normalizedToken,
      baseUrl: normalizedBaseUrl,
      role: roleDraft,
    };

    setSession({
      token: normalizedToken,
      baseUrl: normalizedBaseUrl,
    });
    setActiveRole(roleDraft);
    setStatusMessage("Сессия применена. Можно запускать проверки и операции.");
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(stored));
    queryClient.invalidateQueries();
  };

  const clearSession = () => {
    setSession(null);
    setSelectedProjectId("");
    setSelectedAccountId("");
    setRevealedProxyConfig(null);
    setGatewayResult(null);
    setActivityLog([]);
    localStorage.removeItem(SESSION_STORAGE_KEY);
    setStatusMessage("Сессия очищена.");
    queryClient.clear();
  };

  return (
    <main className="page-shell">
      <header className="hero">
        <p className="eyebrow">DDCRM · WP-RBAC-UI · Contract-first MVP</p>
        <h1>RBAC Console</h1>
        <p>
          Базовый UI поверх External API: проекты, аккаунты, proxy credentials,
          billing и gateway action. RBAC-ограничения применяются по матрице ролей
          и подтверждаются сервером.
        </p>
      </header>

      <section className="panel">
        <h2>Session Bootstrap</h2>
        <div className="grid-3">
          <label className="field">
            <span>Core API Base URL</span>
            <input
              className="input"
              value={baseUrlDraft}
              onChange={(event) => setBaseUrlDraft(event.target.value)}
              placeholder="http://localhost:5073"
            />
          </label>

          <label className="field">
            <span>UI Role</span>
            <select
              className="input"
              value={roleDraft}
              onChange={(event) => setRoleDraft(event.target.value as ProjectRole)}
            >
              {projectRoles.map((role) => (
                <option key={role} value={role}>
                  {role}
                </option>
              ))}
            </select>
          </label>

          <div className="field">
            <span>JWT Subject</span>
            <div className="hint">{jwtSubject ?? "sub не распознан"}</div>
          </div>
        </div>

        <label className="field">
          <span>Bearer JWT</span>
          <textarea
            className="input textarea"
            value={tokenDraft}
            onChange={(event) => setTokenDraft(event.target.value)}
            placeholder="eyJhbGciOiJIUzI1NiIs..."
          />
        </label>

        <div className="actions">
          <button className="button button-primary" onClick={applySession}>
            Применить Сессию
          </button>
          <button className="button button-ghost" onClick={clearSession}>
            Очистить
          </button>
        </div>

        <p className="status">{statusMessage}</p>
      </section>

      {session ? (
        <div className="dashboard-grid">
          <section className="panel">
            <h2>Projects</h2>
            {projectsQuery.isPending ? (
              <p className="hint">Загрузка проектов...</p>
            ) : projectsQuery.error ? (
              <p className="error">
                {projectsQuery.error instanceof Error
                  ? projectsQuery.error.message
                  : "Ошибка listProjects."}
              </p>
            ) : (
              <div className="field">
                <span>Project</span>
                <select
                  className="input"
                  value={effectiveProjectId}
                  onChange={(event) => setSelectedProjectId(event.target.value)}
                >
                  {projectOptions.map((project) => (
                    <option key={project.id} value={project.id}>
                      {project.name} · {project.status}
                    </option>
                  ))}
                </select>
              </div>
            )}

            <div className="inline">
              <input
                className="input"
                value={projectNameInput}
                onChange={(event) => setProjectNameInput(event.target.value)}
                placeholder="Название проекта"
              />
              <button
                className="button button-primary"
                disabled={createProjectMutation.isPending}
                onClick={() => createProjectMutation.mutate()}
              >
                Create Project
              </button>
            </div>
          </section>

          <section className="panel">
            <h2>Accounts</h2>
            {accountsQuery.isPending ? (
              <p className="hint">Загрузка аккаунтов...</p>
            ) : accountsQuery.error ? (
              <p className="error">
                {accountsQuery.error instanceof Error
                  ? accountsQuery.error.message
                  : "Ошибка listAccounts."}
              </p>
            ) : (
              <div className="field">
                <span>Account</span>
                <select
                  className="input"
                  value={effectiveAccountId}
                  onChange={(event) => {
                    setSelectedAccountId(event.target.value);
                    setGatewayRouteKeyInput(buildRouteKey(event.target.value));
                  }}
                >
                  {accountOptions.map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.displayName} · {account.platform} · {account.businessStatus}
                    </option>
                  ))}
                </select>
              </div>
            )}

            {canManageAccountLifecycle ? (
              <div className="stack">
                <div className="grid-2">
                  <label className="field">
                    <span>Platform</span>
                    <input
                      className="input"
                      value={createPlatformInput}
                      onChange={(event) => setCreatePlatformInput(event.target.value)}
                    />
                  </label>
                  <label className="field">
                    <span>Display Name</span>
                    <input
                      className="input"
                      value={createDisplayNameInput}
                      onChange={(event) => setCreateDisplayNameInput(event.target.value)}
                    />
                  </label>
                </div>

                <div className="grid-4">
                  <input
                    className="input"
                    placeholder="proxy host"
                    value={createProxyHostInput}
                    onChange={(event) => setCreateProxyHostInput(event.target.value)}
                  />
                  <input
                    className="input"
                    placeholder="port"
                    value={createProxyPortInput}
                    onChange={(event) => setCreateProxyPortInput(event.target.value)}
                  />
                  <input
                    className="input"
                    placeholder="login"
                    value={createProxyLoginInput}
                    onChange={(event) => setCreateProxyLoginInput(event.target.value)}
                  />
                  <input
                    className="input"
                    placeholder="password"
                    type="password"
                    value={createProxyPasswordInput}
                    onChange={(event) => setCreateProxyPasswordInput(event.target.value)}
                  />
                </div>

                <button
                  className="button button-primary"
                  disabled={createAccountMutation.isPending}
                  onClick={() => createAccountMutation.mutate()}
                >
                  Create Account
                </button>
              </div>
            ) : (
              <p className="hint">
                Текущая UI-роль не имеет `project.accounts.lifecycle.manage`.
              </p>
            )}
          </section>

          <section className="panel">
            <h2>Proxy Credentials</h2>
            {maskedProxyQuery.isPending ? (
              <p className="hint">Загрузка маскированных данных...</p>
            ) : maskedProxyQuery.error ? (
              <p className="error">
                {maskedProxyQuery.error instanceof Error
                  ? maskedProxyQuery.error.message
                  : "Ошибка getAccountProxyCredentialsMasked."}
              </p>
            ) : maskedProxyQuery.data ? (
              <div className="hint">
                configured={String(maskedProxyQuery.data.configured)} · host=
                {maskedProxyQuery.data.hostMasked ?? "n/a"} · login=
                {maskedProxyQuery.data.loginMasked ?? "n/a"}
              </div>
            ) : (
              <p className="hint">Выберите аккаунт для просмотра proxy state.</p>
            )}

            {canRevealProxy ? (
              <div className="inline">
                <input
                  className="input"
                  value={revealReasonInput}
                  onChange={(event) => setRevealReasonInput(event.target.value)}
                  placeholder="reason (min 3)"
                />
                <button
                  className="button button-primary"
                  disabled={revealProxyMutation.isPending}
                  onClick={() => revealProxyMutation.mutate()}
                >
                  Reveal Proxy
                </button>
              </div>
            ) : (
              <p className="hint">
                Для роли {activeRole} скрыта операция reveal (RBAC guard).
              </p>
            )}

            {revealedProxyConfig ? (
              <pre className="pre">
                {JSON.stringify(revealedProxyConfig, null, 2)}
              </pre>
            ) : null}

            {canUpdateProxy ? (
              <div className="stack">
                <input
                  className="input"
                  value={updateReasonInput}
                  onChange={(event) => setUpdateReasonInput(event.target.value)}
                  placeholder="reason"
                />
                <div className="grid-4">
                  <input
                    className="input"
                    placeholder="host"
                    value={updateProxyHostInput}
                    onChange={(event) => setUpdateProxyHostInput(event.target.value)}
                  />
                  <input
                    className="input"
                    placeholder="port"
                    value={updateProxyPortInput}
                    onChange={(event) => setUpdateProxyPortInput(event.target.value)}
                  />
                  <input
                    className="input"
                    placeholder="login"
                    value={updateProxyLoginInput}
                    onChange={(event) => setUpdateProxyLoginInput(event.target.value)}
                  />
                  <input
                    className="input"
                    placeholder="password"
                    type="password"
                    value={updateProxyPasswordInput}
                    onChange={(event) => setUpdateProxyPasswordInput(event.target.value)}
                  />
                </div>
                <button
                  className="button button-primary"
                  disabled={updateProxyMutation.isPending}
                  onClick={() => updateProxyMutation.mutate()}
                >
                  Update Proxy
                </button>
              </div>
            ) : (
              <p className="hint">
                Для роли {activeRole} скрыта операция update proxy (RBAC guard).
              </p>
            )}
          </section>

          <section className="panel">
            <h2>Billing</h2>
            {canViewBilling ? (
              <div className="stack">
                <div className="grid-2">
                  <input
                    className="input"
                    value={paymentAmountInput}
                    onChange={(event) => setPaymentAmountInput(event.target.value)}
                    placeholder="amount"
                  />
                  <input
                    className="input"
                    value={paymentCurrencyInput}
                    onChange={(event) => setPaymentCurrencyInput(event.target.value)}
                    placeholder="currency"
                  />
                </div>
                <button
                  className="button button-primary"
                  disabled={!canChangeBilling || createPaymentMutation.isPending}
                  onClick={() => createPaymentMutation.mutate()}
                >
                  Create Payment
                </button>

                <div className="inline">
                  <input
                    className="input"
                    value={planKeyInput}
                    onChange={(event) => setPlanKeyInput(event.target.value)}
                    placeholder="planKey"
                  />
                  <button
                    className="button button-primary"
                    disabled={!canChangeBilling || changePlanMutation.isPending}
                    onClick={() => changePlanMutation.mutate()}
                  >
                    Change Plan
                  </button>
                </div>

                <div className="inline">
                  <input
                    className="input"
                    value={addonIdInput}
                    onChange={(event) => setAddonIdInput(event.target.value)}
                    placeholder="addonId"
                  />
                  <button
                    className="button button-primary"
                    disabled={!canChangeBilling || purchaseAddonMutation.isPending}
                    onClick={() => purchaseAddonMutation.mutate()}
                  >
                    Purchase Add-on
                  </button>
                </div>
              </div>
            ) : (
              <p className="hint">
                Финансовый блок скрыт для роли {activeRole} согласно access matrix.
              </p>
            )}
          </section>

          <section className="panel">
            <h2>Gateway Proxy</h2>
            {canUseGateway ? (
              <div className="stack">
                <div className="grid-2">
                  <input
                    className="input"
                    value={effectiveGatewayRouteKey}
                    onChange={(event) => setGatewayRouteKeyInput(event.target.value)}
                    placeholder="routeKey"
                  />
                  <input
                    className="input"
                    value={gatewayActionInput}
                    onChange={(event) => setGatewayActionInput(event.target.value)}
                    placeholder="action"
                  />
                </div>

                <textarea
                  className="input textarea"
                  value={gatewayPayloadInput}
                  onChange={(event) => setGatewayPayloadInput(event.target.value)}
                  placeholder='{"foo":"bar"}'
                />

                <button
                  className="button button-primary"
                  disabled={gatewayMutation.isPending}
                  onClick={() => gatewayMutation.mutate()}
                >
                  Invoke account-api action
                </button>

                {gatewayResult ? (
                  <pre className="pre">{JSON.stringify(gatewayResult, null, 2)}</pre>
                ) : null}
              </div>
            ) : (
              <p className="hint">
                Для роли {activeRole} скрыт gateway proxy (нужно право
                `project.workers.operate`).
              </p>
            )}
          </section>

          <section className="panel">
            <h2>Role Permissions</h2>
            <ul className="permission-list">
              {Object.values(projectPermissions).map((permission) => (
                <li key={permission}>
                  <span>{permission}</span>
                  <strong>
                    {hasPermission(activeRole, permission) ? "allowed" : "denied"}
                  </strong>
                </li>
              ))}
            </ul>
          </section>

          <section className="panel">
            <h2>Activity Log</h2>
            {activityLog.length === 0 ? (
              <p className="hint">Операции пока не запускались.</p>
            ) : (
              <ul className="activity-list">
                {activityLog.map((entry) => (
                  <li key={entry}>{entry}</li>
                ))}
              </ul>
            )}
          </section>
        </div>
      ) : (
        <section className="panel">
          <h2>Ready Check</h2>
          <p className="hint">
            Примените сессию (JWT + role + Core API URL), после этого загрузятся
            проекты и остальные секции.
          </p>
        </section>
      )}
    </main>
  );
}
