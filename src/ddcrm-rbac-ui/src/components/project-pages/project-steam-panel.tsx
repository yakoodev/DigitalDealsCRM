"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import type {
  ApiSession,
  SteamIntegrationAccount,
  SteamIntegrationAccountUpsertPayload,
  SteamIntegrationJob,
  SteamIntegrationJobCreatePayload,
} from "@/lib/api-client";
import {
  archiveSteamIntegrationAccountRequest,
  cancelSteamIntegrationJobRequest,
  createSteamIntegrationAccountRequest,
  createSteamIntegrationJobRequest,
  listProjectIntegrationsStatusRequest,
  listSteamIntegrationAccountsRequest,
  listSteamIntegrationJobsRequest,
  triggerProjectIntegrationRuntimeRequest,
  updateSteamIntegrationAccountRequest,
} from "@/lib/api-client";

interface ProjectSteamPanelProps {
  apiSession: ApiSession;
  projectId: string;
}

interface AccountFormState {
  loginName: string;
  displayName: string;
  email: string;
  emailLogin: string;
  emailPassword: string;
  phoneMasked: string;
  steamId64: string;
  proxy: string;
  folderName: string;
  password: string;
  loginPassword: string;
  sharedSecret: string;
  identitySecret: string;
  guardRecoveryCode: string;
  maFilePayload: string;
  accessToken: string;
  refreshToken: string;
  authSessionId: string;
  steamLoginSecure: string;
  steamRememberLogin: string;
  webCookie: string;
  deviceId: string;
  machineName: string;
  familyViewPin: string;
  countryCode: string;
  timeZone: string;
  authHeadersJson: string;
  sessionPayload: string;
  recoveryPayload: string;
  note: string;
  status: string;
  tagsCsv: string;
}

const defaultAccountFormState: AccountFormState = {
  loginName: "",
  displayName: "",
  email: "",
  emailLogin: "",
  emailPassword: "",
  phoneMasked: "",
  steamId64: "",
  proxy: "",
  folderName: "",
  password: "",
  loginPassword: "",
  sharedSecret: "",
  identitySecret: "",
  guardRecoveryCode: "",
  maFilePayload: "",
  accessToken: "",
  refreshToken: "",
  authSessionId: "",
  steamLoginSecure: "",
  steamRememberLogin: "",
  webCookie: "",
  deviceId: "",
  machineName: "",
  familyViewPin: "",
  countryCode: "",
  timeZone: "",
  authHeadersJson: "{}",
  sessionPayload: "",
  recoveryPayload: "",
  note: "",
  status: "Active",
  tagsCsv: "",
};

const quickJobPresets = [
  { type: "SessionValidate", label: "Проверить сессии" },
  { type: "SessionRefresh", label: "Обновить сессии" },
  { type: "PasswordChange", label: "Сменить пароль" },
] as const;

function formatDate(value?: string | null) {
  if (!value) {
    return "-";
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return parsed.toLocaleString("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function toStatusBadgeClass(value: string | null | undefined) {
  const normalized = (value ?? "").trim().toLowerCase();
  if (normalized === "active" || normalized === "completed" || normalized === "success") {
    return "is-pill-ok";
  }

  if (normalized === "failed" || normalized === "error" || normalized === "revoked") {
    return "is-pill-danger";
  }

  if (normalized === "running" || normalized === "pending" || normalized === "queued") {
    return "is-pill-warning";
  }

  return "";
}

function toPayload(form: AccountFormState): SteamIntegrationAccountUpsertPayload {
  const tags = form.tagsCsv
    .split(",")
    .map((item) => item.trim())
    .filter((item) => item.length > 0);

  let authHeaders: Record<string, string> | undefined;
  const authHeadersText = form.authHeadersJson.trim();
  if (authHeadersText && authHeadersText !== "{}") {
    const parsed = JSON.parse(authHeadersText) as Record<string, unknown>;
    authHeaders = Object.entries(parsed).reduce<Record<string, string>>((acc, [key, value]) => {
      if (typeof value === "string") {
        acc[key] = value;
      } else if (value !== null && typeof value !== "undefined") {
        acc[key] = String(value);
      }
      return acc;
    }, {});
  }

  const metadata: Record<string, string> = {};
  if (form.emailLogin.trim()) {
    metadata.emailLogin = form.emailLogin.trim();
  }
  if (form.steamId64.trim()) {
    metadata.steamId64 = form.steamId64.trim();
  }
  if (form.deviceId.trim()) {
    metadata.deviceId = form.deviceId.trim();
  }
  if (form.machineName.trim()) {
    metadata.machineName = form.machineName.trim();
  }
  if (form.countryCode.trim()) {
    metadata.countryCode = form.countryCode.trim();
  }
  if (form.timeZone.trim()) {
    metadata.timeZone = form.timeZone.trim();
  }
  if (form.authSessionId.trim()) {
    metadata.authSessionId = form.authSessionId.trim();
  }

  return {
    loginName: form.loginName.trim(),
    displayName: form.displayName.trim() || undefined,
    email: form.email.trim() || undefined,
    emailLogin: form.emailLogin.trim() || undefined,
    emailPassword: form.emailPassword.trim() || undefined,
    phoneMasked: form.phoneMasked.trim() || undefined,
    password: form.password.trim() || undefined,
    loginPassword: form.loginPassword.trim() || undefined,
    sharedSecret: form.sharedSecret.trim() || undefined,
    identitySecret: form.identitySecret.trim() || undefined,
    guardRecoveryCode: form.guardRecoveryCode.trim() || undefined,
    maFilePayload: form.maFilePayload.trim() || undefined,
    accessToken: form.accessToken.trim() || undefined,
    refreshToken: form.refreshToken.trim() || undefined,
    authSessionId: form.authSessionId.trim() || undefined,
    steamLoginSecure: form.steamLoginSecure.trim() || undefined,
    steamRememberLogin: form.steamRememberLogin.trim() || undefined,
    webCookie: form.webCookie.trim() || undefined,
    deviceId: form.deviceId.trim() || undefined,
    machineName: form.machineName.trim() || undefined,
    familyViewPin: form.familyViewPin.trim() || undefined,
    countryCode: form.countryCode.trim() || undefined,
    timeZone: form.timeZone.trim() || undefined,
    sessionPayload: form.sessionPayload.trim() || undefined,
    recoveryPayload: form.recoveryPayload.trim() || undefined,
    steamId64: form.steamId64.trim() || undefined,
    proxy: form.proxy.trim() || undefined,
    folderName: form.folderName.trim() || undefined,
    metadata: Object.keys(metadata).length > 0 ? metadata : undefined,
    authHeaders,
    note: form.note.trim() || undefined,
    status: form.status.trim() || undefined,
    tags,
  };
}

function buildJobPayload(params: {
  type: string;
  accountIds: string[];
  dryRun: boolean;
  parallelism: string;
  retryCount: string;
  payloadJson?: string;
}) {
  let payloadObject: Record<string, string> | undefined;
  if (params.payloadJson && params.payloadJson.trim() && params.payloadJson.trim() !== "{}") {
    const parsed = JSON.parse(params.payloadJson) as Record<string, unknown>;
    payloadObject = Object.entries(parsed).reduce<Record<string, string>>((acc, [key, value]) => {
      acc[key] = String(value);
      return acc;
    }, {});
  }

  return {
    type: params.type,
    accountIds: params.accountIds,
    dryRun: params.dryRun,
    parallelism: Number(params.parallelism) || 5,
    retryCount: Number(params.retryCount) || 2,
    payload: payloadObject,
  } satisfies SteamIntegrationJobCreatePayload;
}

function parseBulkAccountsInput(input: string): SteamIntegrationAccountUpsertPayload[] {
  const lines = input
    .split(/\r?\n/g)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith("#"));

  return lines.map((line, index) => {
    const delimiter = line.includes("|") ? "|" : ";";
    const columns = line.split(delimiter).map((item) => item.trim());
    const loginName = columns[0] ?? "";
    if (!loginName) {
      throw new Error(`Строка ${index + 1}: loginName обязателен.`);
    }

    const tags = (columns[8] ?? "")
      .split(",")
      .map((item) => item.trim())
      .filter((item) => item.length > 0);

    return {
      loginName,
      password: columns[1] || undefined,
      loginPassword: columns[1] || undefined,
      email: columns[2] || undefined,
      emailPassword: columns[3] || undefined,
      sharedSecret: columns[4] || undefined,
      identitySecret: columns[5] || undefined,
      steamId64: columns[6] || undefined,
      proxy: columns[7] || undefined,
      tags: tags.length > 0 ? tags : undefined,
      note: columns[9] || undefined,
      status: "Active",
    } satisfies SteamIntegrationAccountUpsertPayload;
  });
}

function resolveMaFileStringValue(source: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === "string" && value.trim().length > 0) {
      return value.trim();
    }
  }

  return "";
}

export function ProjectSteamPanel({ apiSession, projectId }: ProjectSteamPanelProps) {
  const queryClient = useQueryClient();

  const [queryText, setQueryText] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [formState, setFormState] = useState<AccountFormState>(defaultAccountFormState);
  const [editingAccountId, setEditingAccountId] = useState<string | null>(null);
  const [showAdvancedAccountFields, setShowAdvancedAccountFields] = useState(false);
  const [selectedAccountIds, setSelectedAccountIds] = useState<string[]>([]);
  const [jobType, setJobType] = useState("SessionValidate");
  const [jobDryRun, setJobDryRun] = useState(false);
  const [jobParallelism, setJobParallelism] = useState("5");
  const [jobRetryCount, setJobRetryCount] = useState("2");
  const [showAdvancedJobPayload, setShowAdvancedJobPayload] = useState(false);
  const [jobPayloadJson, setJobPayloadJson] = useState("{}");
  const [jobsTake, setJobsTake] = useState("40");
  const [jobsTypeFilter, setJobsTypeFilter] = useState("");
  const [bulkAccountsText, setBulkAccountsText] = useState("");
  const [showBulkImport, setShowBulkImport] = useState(
    () => typeof window !== "undefined" && window.location.hash === "#bulk-import",
  );
  const [statusMessage, setStatusMessage] = useState(
    "Используйте таблицу аккаунтов, отмечайте нужные строки и запускайте массовые jobs.",
  );

  const integrationStatusQuery = useQuery({
    queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listProjectIntegrationsStatusRequest(apiSession, projectId),
    staleTime: 10_000,
  });

  const steamRuntime = useMemo(
    () =>
      (integrationStatusQuery.data?.items ?? []).find(
        (item) => item.integrationKey === "steam-accounts-manager",
      ) ?? null,
    [integrationStatusQuery.data?.items],
  );

  const accountsQuery = useQuery({
    queryKey: ["steam-accounts", apiSession.baseUrl, apiSession.token, projectId, queryText, statusFilter],
    queryFn: () =>
      listSteamIntegrationAccountsRequest(apiSession, projectId, {
        query: queryText,
        status: statusFilter === "all" ? undefined : statusFilter,
        page: 1,
        pageSize: 200,
      }),
    staleTime: 5_000,
  });

  const jobsQuery = useQuery({
    queryKey: ["steam-jobs", apiSession.baseUrl, apiSession.token, projectId, jobsTake],
    queryFn: () => listSteamIntegrationJobsRequest(apiSession, projectId, Number(jobsTake) || 40),
    staleTime: 5_000,
  });

  const accounts = useMemo(() => accountsQuery.data?.items ?? [], [accountsQuery.data?.items]);
  const jobs = useMemo(() => jobsQuery.data ?? [], [jobsQuery.data]);
  const visibleAccountIds = useMemo(() => new Set(accounts.map((account) => account.id)), [accounts]);
  const selectedVisibleAccountIds = useMemo(
    () => selectedAccountIds.filter((id) => visibleAccountIds.has(id)),
    [selectedAccountIds, visibleAccountIds],
  );

  const filteredJobs = useMemo(() => {
    const token = jobsTypeFilter.trim().toLowerCase();
    if (!token) {
      return jobs;
    }

    return jobs.filter((job) => job.type.toLowerCase().includes(token));
  }, [jobs, jobsTypeFilter]);

  const accountStats = useMemo(() => {
    const active = accounts.filter((item) => (item.status ?? "").toLowerCase() === "active").length;
    const disabled = accounts.filter((item) => (item.status ?? "").toLowerCase() === "disabled").length;
    const archived = accounts.filter((item) => (item.status ?? "").toLowerCase() === "archived").length;

    return {
      total: accounts.length,
      active,
      disabled,
      archived,
    };
  }, [accounts]);

  const refreshAll = async () => {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: ["steam-accounts", apiSession.baseUrl, apiSession.token, projectId],
      }),
      queryClient.invalidateQueries({
        queryKey: ["steam-jobs", apiSession.baseUrl, apiSession.token, projectId],
      }),
      queryClient.invalidateQueries({
        queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
      }),
    ]);
  };

  const runtimeMutation = useMutation({
    mutationFn: (operation: "provision" | "deprovision" | "restart") =>
      triggerProjectIntegrationRuntimeRequest(apiSession, projectId, "steam-accounts-manager", operation),
    onSuccess: async (_data, operation) => {
      await refreshAll();
      setStatusMessage(`Runtime-операция \`${operation}\` поставлена в очередь.`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось выполнить runtime-операцию.");
    },
  });

  const saveAccountMutation = useMutation({
    mutationFn: async () => {
      const payload = toPayload(formState);
      if (!payload.loginName.trim()) {
        throw new Error("Логин аккаунта обязателен.");
      }

      if (editingAccountId) {
        return updateSteamIntegrationAccountRequest(apiSession, projectId, editingAccountId, payload);
      }

      return createSteamIntegrationAccountRequest(apiSession, projectId, payload);
    },
    onSuccess: async () => {
      await refreshAll();
      setStatusMessage(editingAccountId ? "Аккаунт обновлён." : "Аккаунт создан.");
      setEditingAccountId(null);
      setFormState(defaultAccountFormState);
      setShowAdvancedAccountFields(false);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось сохранить аккаунт.");
    },
  });

  const bulkCreateMutation = useMutation({
    mutationFn: async () => {
      const payloads = parseBulkAccountsInput(bulkAccountsText);
      if (payloads.length === 0) {
        throw new Error("Добавьте минимум одну строку для массового импорта.");
      }

      const errors: string[] = [];
      let successCount = 0;

      for (const payload of payloads) {
        try {
          await createSteamIntegrationAccountRequest(apiSession, projectId, payload);
          successCount += 1;
        } catch (error) {
          const reason = error instanceof Error ? error.message : "unknown error";
          errors.push(`${payload.loginName}: ${reason}`);
        }
      }

      return {
        successCount,
        totalCount: payloads.length,
        errors,
      };
    },
    onSuccess: async (result) => {
      await refreshAll();
      if (result.errors.length === 0) {
        setStatusMessage(`Импортировано ${result.successCount} из ${result.totalCount} аккаунтов.`);
        setBulkAccountsText("");
        return;
      }

      setStatusMessage(
        `Импортировано ${result.successCount} из ${result.totalCount}. Ошибки: ${result.errors.slice(0, 3).join(" | ")}`,
      );
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось выполнить массовый импорт.");
    },
  });

  const archiveAccountMutation = useMutation({
    mutationFn: (accountId: string) => archiveSteamIntegrationAccountRequest(apiSession, projectId, accountId),
    onSuccess: async () => {
      await refreshAll();
      setStatusMessage("Аккаунт архивирован.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось архивировать аккаунт.");
    },
  });

  const createJobMutation = useMutation({
    mutationFn: (payload: SteamIntegrationJobCreatePayload) =>
      createSteamIntegrationJobRequest(apiSession, projectId, payload),
    onSuccess: async (job) => {
      await refreshAll();
      setStatusMessage(`Jobs-задача ${job.type} создана.`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось создать jobs-задачу.");
    },
  });

  const cancelJobMutation = useMutation({
    mutationFn: (jobId: string) => cancelSteamIntegrationJobRequest(apiSession, projectId, jobId),
    onSuccess: async () => {
      await refreshAll();
      setStatusMessage("Jobs-задача отменена.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось отменить jobs-задачу.");
    },
  });

  const toggleAccountSelection = (accountId: string) => {
    setSelectedAccountIds((current) =>
      current.includes(accountId)
        ? current.filter((item) => item !== accountId)
        : [...current, accountId]);
  };

  const selectAllVisible = () => {
    setSelectedAccountIds(accounts.map((account) => account.id));
  };

  const selectOnlyActive = () => {
    setSelectedAccountIds(
      accounts
        .filter((account) => (account.status ?? "").toLowerCase() === "active")
        .map((account) => account.id),
    );
  };

  const clearSelection = () => {
    setSelectedAccountIds([]);
  };

  const beginEdit = (account: SteamIntegrationAccount) => {
    const metadata = account.metadata ?? {};
    setEditingAccountId(account.id);
    setFormState({
      loginName: account.loginName,
      displayName: account.displayName ?? "",
      email: account.email ?? "",
      emailLogin: metadata.emailLogin ?? "",
      emailPassword: "",
      phoneMasked: account.phoneMasked ?? "",
      steamId64: account.steamId64 ?? "",
      proxy: account.proxy ?? "",
      folderName: account.folderName ?? "",
      password: "",
      loginPassword: "",
      sharedSecret: "",
      identitySecret: "",
      guardRecoveryCode: "",
      maFilePayload: metadata.maFilePayload ?? "",
      accessToken: "",
      refreshToken: "",
      authSessionId: metadata.authSessionId ?? "",
      steamLoginSecure: "",
      steamRememberLogin: "",
      webCookie: "",
      deviceId: metadata.deviceId ?? "",
      machineName: metadata.machineName ?? "",
      familyViewPin: "",
      countryCode: metadata.countryCode ?? "",
      timeZone: metadata.timeZone ?? "",
      authHeadersJson: "{}",
      sessionPayload: "",
      recoveryPayload: "",
      note: account.note ?? "",
      status: account.status ?? "Active",
      tagsCsv: (account.tags ?? []).join(", "),
    });
  };

  const resetEditor = () => {
    setEditingAccountId(null);
    setFormState(defaultAccountFormState);
    setShowAdvancedAccountFields(false);
  };

  const importMaFile = async (file: File | null) => {
    if (!file) {
      return;
    }

    try {
      const raw = await file.text();
      const parsed = JSON.parse(raw) as Record<string, unknown>;
      const loginName = resolveMaFileStringValue(parsed, "account_name", "accountName", "login", "loginName");
      const sharedSecret = resolveMaFileStringValue(parsed, "shared_secret", "sharedSecret");
      const identitySecret = resolveMaFileStringValue(parsed, "identity_secret", "identitySecret");
      const steamId64 = resolveMaFileStringValue(parsed, "steamid", "steamId64");

      setFormState((current) => ({
        ...current,
        loginName: current.loginName.trim() ? current.loginName : loginName,
        sharedSecret: current.sharedSecret.trim() ? current.sharedSecret : sharedSecret,
        identitySecret: current.identitySecret.trim() ? current.identitySecret : identitySecret,
        steamId64: current.steamId64.trim() ? current.steamId64 : steamId64,
        recoveryPayload: current.recoveryPayload.trim() ? current.recoveryPayload : raw,
        maFilePayload: raw,
      }));

      setStatusMessage("maFile загружен. Поля login/shared/identity заполнены автоматически.");
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось прочитать maFile JSON.");
    }
  };

  const runQuickJob = (jobTypeValue: string) => {
    if (selectedVisibleAccountIds.length === 0) {
      setStatusMessage("Выберите минимум один аккаунт.");
      return;
    }

    createJobMutation.mutate(buildJobPayload({
      type: jobTypeValue,
      accountIds: selectedVisibleAccountIds,
      dryRun: false,
      parallelism: "6",
      retryCount: "1",
      payloadJson: "{}",
    }));
  };

  const runConfiguredJob = () => {
    if (selectedVisibleAccountIds.length === 0) {
      setStatusMessage("Выберите минимум один аккаунт.");
      return;
    }

    try {
      createJobMutation.mutate(buildJobPayload({
        type: jobType,
        accountIds: selectedVisibleAccountIds,
        dryRun: jobDryRun,
        parallelism: jobParallelism,
        retryCount: jobRetryCount,
        payloadJson: showAdvancedJobPayload ? jobPayloadJson : "{}",
      }));
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : "Некорректный payload для jobs.");
    }
  };

  return (
    <div className="page-stack" data-testid="project-steam-panel">
      <header className="page-section-header">
        <h2>Steam кабинет проекта</h2>
        <p>Таблица аккаунтов, массовые jobs и контроль runtime.</p>
      </header>

      <article className="glass-card page-stack">
        <div className="panel-title-row">
          <h3>Runtime</h3>
          <button
            type="button"
            className="button button-ghost"
            disabled={integrationStatusQuery.isFetching}
            onClick={() => integrationStatusQuery.refetch()}
          >
            Обновить статус
          </button>
        </div>
        {integrationStatusQuery.isPending ? <p className="route-hint">Загрузка состояния runtime...</p> : null}
        {integrationStatusQuery.error ? (
          <p className="route-error">
            {integrationStatusQuery.error instanceof Error
              ? integrationStatusQuery.error.message
              : "Не удалось загрузить состояние runtime."}
          </p>
        ) : null}
        {steamRuntime ? (
          <>
            <div className="entity-pills">
              <span className={`entity-pill ${toStatusBadgeClass(steamRuntime.status)}`}>grant: {steamRuntime.status}</span>
              <span className={`entity-pill ${toStatusBadgeClass(steamRuntime.runtimeStatus ?? "")}`}>
                runtime: {steamRuntime.runtimeStatus ?? "n/a"}
              </span>
              {steamRuntime.runtimeAccountId ? (
                <span className="entity-pill">rk.{steamRuntime.runtimeAccountId.replaceAll("-", "")}</span>
              ) : null}
              <span className="entity-pill">scopes: {steamRuntime.scopes.join(", ") || "n/a"}</span>
            </div>
            <div className="inline-actions">
              <button type="button" className="button button-ghost" disabled={runtimeMutation.isPending} onClick={() => runtimeMutation.mutate("provision")}>
                Provision
              </button>
              <button type="button" className="button button-ghost" disabled={runtimeMutation.isPending} onClick={() => runtimeMutation.mutate("restart")}>
                Restart
              </button>
              <button type="button" className="button button-ghost" disabled={runtimeMutation.isPending} onClick={() => runtimeMutation.mutate("deprovision")}>
                Deprovision
              </button>
            </div>
            {steamRuntime.runtimeLastError ? <p className="route-error">{steamRuntime.runtimeLastError}</p> : null}
          </>
        ) : (
          <p className="route-hint">Steam интеграция пока не выдана проекту.</p>
        )}
      </article>

      <section className="module-board">
        <section className="module-main-column">
          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Аккаунты Steam</h3>
              <button type="button" className="button button-ghost" disabled={accountsQuery.isFetching} onClick={() => accountsQuery.refetch()}>
                Обновить
              </button>
            </div>
            <div className="inline-actions">
              <input className="input" value={queryText} onChange={(event) => setQueryText(event.target.value)} placeholder="Поиск по логину/email" />
              <select className="input" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
                <option value="all">Все статусы</option>
                <option value="Active">Active</option>
                <option value="Disabled">Disabled</option>
                <option value="Archived">Archived</option>
              </select>
            </div>
            <div className="entity-pills">
              <span className="entity-pill">total: {accountStats.total}</span>
              <span className="entity-pill is-pill-ok">active: {accountStats.active}</span>
              <span className="entity-pill is-pill-warning">disabled: {accountStats.disabled}</span>
              <span className="entity-pill is-pill-danger">archived: {accountStats.archived}</span>
              <span className="entity-pill">selected: {selectedVisibleAccountIds.length}</span>
            </div>
            <div className="inline-actions">
              <button type="button" className="button button-ghost" onClick={selectAllVisible}>Выбрать видимые</button>
              <button type="button" className="button button-ghost" onClick={selectOnlyActive}>Только active</button>
              <button type="button" className="button button-ghost" onClick={clearSelection}>Снять выбор</button>
            </div>
            <div className="inline-actions">
              {quickJobPresets.map((preset) => (
                <button key={preset.type} type="button" className="button button-primary" disabled={createJobMutation.isPending || selectedVisibleAccountIds.length === 0} onClick={() => runQuickJob(preset.type)}>
                  {preset.label}
                </button>
              ))}
            </div>

            {accountsQuery.isPending ? <p className="route-hint">Загрузка аккаунтов...</p> : null}
            {accountsQuery.error ? (
              <p className="route-error">
                {accountsQuery.error instanceof Error ? accountsQuery.error.message : "Не удалось загрузить аккаунты."}
              </p>
            ) : null}
            {!accountsQuery.isPending && !accountsQuery.error && accounts.length === 0 ? (
              <p className="route-hint">Аккаунты не найдены.</p>
            ) : null}
            {!accountsQuery.isPending && !accountsQuery.error && accounts.length > 0 ? (
              <div className="matrix-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th />
                      <th>Логин</th>
                      <th>Статус</th>
                      <th>SteamID64</th>
                      <th>Email</th>
                      <th>Proxy</th>
                      <th>Теги</th>
                      <th>Обновлено</th>
                      <th>Действия</th>
                    </tr>
                  </thead>
                  <tbody>
                    {accounts.map((account) => (
                      <tr key={account.id}>
                        <td>
                          <input type="checkbox" checked={selectedAccountIds.includes(account.id)} onChange={() => toggleAccountSelection(account.id)} />
                        </td>
                        <td>
                          <div className="page-stack">
                            <strong>{account.loginName}</strong>
                            {account.displayName ? <small>{account.displayName}</small> : null}
                            <small>{account.id}</small>
                          </div>
                        </td>
                        <td><span className={`entity-pill ${toStatusBadgeClass(account.status)}`}>{account.status ?? "unknown"}</span></td>
                        <td>{account.steamId64 ?? account.metadata?.steamId64 ?? "-"}</td>
                        <td>{account.email ?? "-"}</td>
                        <td>{account.proxy ?? "-"}</td>
                        <td>{(account.tags ?? []).join(", ") || "-"}</td>
                        <td>{formatDate(account.updatedAt)}</td>
                        <td>
                          <div className="inline-actions">
                            <button type="button" className="button button-ghost" onClick={() => beginEdit(account)}>Редактировать</button>
                            <button type="button" className="button button-ghost" disabled={archiveAccountMutation.isPending} onClick={() => archiveAccountMutation.mutate(account.id)}>
                              Архивировать
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </article>

          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Очередь jobs</h3>
              <div className="inline-actions">
                <input className="input" value={jobsTypeFilter} onChange={(event) => setJobsTypeFilter(event.target.value)} placeholder="Фильтр по типу jobs" />
                <input className="input" value={jobsTake} onChange={(event) => setJobsTake(event.target.value)} placeholder="take" />
                <button type="button" className="button button-ghost" disabled={jobsQuery.isFetching} onClick={() => jobsQuery.refetch()}>Обновить jobs</button>
              </div>
            </div>
            {jobsQuery.isPending ? <p className="route-hint">Загрузка jobs...</p> : null}
            {jobsQuery.error ? (
              <p className="route-error">
                {jobsQuery.error instanceof Error ? jobsQuery.error.message : "Не удалось загрузить jobs."}
              </p>
            ) : null}
            {!jobsQuery.isPending && !jobsQuery.error && filteredJobs.length === 0 ? <p className="route-hint">Jobs пока нет.</p> : null}
            {!jobsQuery.isPending && !jobsQuery.error && filteredJobs.length > 0 ? (
              <div className="matrix-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Тип</th>
                      <th>Статус</th>
                      <th>Успех/ошибка</th>
                      <th>DryRun</th>
                      <th>Создана</th>
                      <th>Финиш</th>
                      <th>Items</th>
                      <th>Действие</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredJobs.map((job: SteamIntegrationJob) => (
                      <tr key={job.id}>
                        <td>
                          <div className="page-stack">
                            <strong>{job.type}</strong>
                            <small>{job.id}</small>
                          </div>
                        </td>
                        <td><span className={`entity-pill ${toStatusBadgeClass(job.status)}`}>{job.status}</span></td>
                        <td>{job.successCount}/{job.failureCount}</td>
                        <td>{job.dryRun ? "true" : "false"}</td>
                        <td>{formatDate(job.createdAt)}</td>
                        <td>{formatDate(job.finishedAt)}</td>
                        <td>{job.items?.length ?? 0}</td>
                        <td>
                          <button type="button" className="button button-ghost" disabled={cancelJobMutation.isPending} onClick={() => cancelJobMutation.mutate(job.id)}>
                            Отменить
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </article>
        </section>

        <aside className="module-side-column">
          <article className="glass-card page-stack panel-card-sticky">
            <div className="panel-title-row">
              <h3>{editingAccountId ? "Редактирование Steam-аккаунта" : "Новый Steam-аккаунт"}</h3>
            </div>
            <div className="stacked-block steam-form-callout">
              <p className="route-hint">
                Рекомендуемый порядок: 1) заполните базу + Guard, 2) сохраните аккаунт, 3) выделите его в таблице и
                запустите <strong>Проверить/Обновить сессии</strong>.
              </p>
            </div>
            <div className="panel-title-row">
              <h4>1. Базовые данные</h4>
            </div>
            <div className="grid-2">
              <label className="field">
                <span>Login *</span>
                <input className="input" value={formState.loginName} onChange={(event) => setFormState((current) => ({ ...current, loginName: event.target.value }))} />
              </label>
              <label className="field">
                <span>Пароль Steam</span>
                <input className="input" type="password" value={formState.password} onChange={(event) => setFormState((current) => ({ ...current, password: event.target.value }))} />
              </label>
              <label className="field">
                <span>Display name</span>
                <input className="input" value={formState.displayName} onChange={(event) => setFormState((current) => ({ ...current, displayName: event.target.value }))} />
              </label>
              <label className="field">
                <span>Email</span>
                <input className="input" value={formState.email} onChange={(event) => setFormState((current) => ({ ...current, email: event.target.value }))} />
              </label>
              <label className="field">
                <span>Телефон (masked)</span>
                <input className="input" value={formState.phoneMasked} onChange={(event) => setFormState((current) => ({ ...current, phoneMasked: event.target.value }))} />
              </label>
              <label className="field">
                <span>SteamID64</span>
                <input className="input" value={formState.steamId64} onChange={(event) => setFormState((current) => ({ ...current, steamId64: event.target.value }))} />
              </label>
            </div>

            <div className="panel-title-row">
              <h4>2. Steam Guard и секреты</h4>
            </div>
            <div className="grid-2">
              <label className="field">
                <span>Shared Secret (base64)</span>
                <input className="input" value={formState.sharedSecret} onChange={(event) => setFormState((current) => ({ ...current, sharedSecret: event.target.value }))} />
              </label>
              <label className="field">
                <span>Identity Secret (base64)</span>
                <input className="input" value={formState.identitySecret} onChange={(event) => setFormState((current) => ({ ...current, identitySecret: event.target.value }))} />
              </label>
              <label className="field">
                <span>Guard recovery code</span>
                <input className="input" value={formState.guardRecoveryCode} onChange={(event) => setFormState((current) => ({ ...current, guardRecoveryCode: event.target.value }))} />
              </label>
              <label className="field">
                <span>Login password (если отличается)</span>
                <input className="input" type="password" value={formState.loginPassword} onChange={(event) => setFormState((current) => ({ ...current, loginPassword: event.target.value }))} />
              </label>
            </div>
            <label className="field">
              <span>Импорт maFile (.json)</span>
              <input
                className="input"
                type="file"
                accept=".json,application/json"
                onChange={(event) => {
                  const file = event.target.files?.[0] ?? null;
                  void importMaFile(file);
                }}
              />
            </label>
            <label className="field">
              <span>Session payload</span>
              <textarea className="input" rows={3} value={formState.sessionPayload} onChange={(event) => setFormState((current) => ({ ...current, sessionPayload: event.target.value }))} />
            </label>
            <label className="field">
              <span>GuardData / Recovery payload</span>
              <textarea className="input" rows={3} value={formState.recoveryPayload} onChange={(event) => setFormState((current) => ({ ...current, recoveryPayload: event.target.value }))} />
            </label>

            <div className="panel-title-row">
              <h4>3. Организация и сервисные поля</h4>
            </div>
            <div className="grid-2">
              <label className="field">
                <span>Папка</span>
                <input className="input" value={formState.folderName} onChange={(event) => setFormState((current) => ({ ...current, folderName: event.target.value }))} />
              </label>
              <label className="field">
                <span>Proxy</span>
                <input className="input" value={formState.proxy} onChange={(event) => setFormState((current) => ({ ...current, proxy: event.target.value }))} />
              </label>
              <label className="field">
                <span>Email login (если отдельный)</span>
                <input className="input" value={formState.emailLogin} onChange={(event) => setFormState((current) => ({ ...current, emailLogin: event.target.value }))} />
              </label>
              <label className="field">
                <span>Email password</span>
                <input className="input" type="password" value={formState.emailPassword} onChange={(event) => setFormState((current) => ({ ...current, emailPassword: event.target.value }))} />
              </label>
            </div>
            <div className="grid-2">
              <label className="field">
                <span>Tags (через запятую)</span>
                <input className="input" value={formState.tagsCsv} onChange={(event) => setFormState((current) => ({ ...current, tagsCsv: event.target.value }))} />
              </label>
              <label className="field">
                <span>Status</span>
                <select className="input" value={formState.status} onChange={(event) => setFormState((current) => ({ ...current, status: event.target.value }))}>
                  <option value="Active">Active</option>
                  <option value="Disabled">Disabled</option>
                  <option value="Archived">Archived</option>
                </select>
              </label>
            </div>
            <label className="field">
              <span>Примечание</span>
              <textarea className="input" rows={3} value={formState.note} onChange={(event) => setFormState((current) => ({ ...current, note: event.target.value }))} />
            </label>
            <label className="field field-inline">
              <span>Расширенная авторизация (token/cookie/device)</span>
              <input type="checkbox" checked={showAdvancedAccountFields} onChange={(event) => setShowAdvancedAccountFields(event.target.checked)} />
            </label>
            {showAdvancedAccountFields ? (
              <div className="page-stack steam-advanced-form">
                <div className="grid-2">
                  <label className="field">
                    <span>Family view pin</span>
                    <input className="input" type="password" value={formState.familyViewPin} onChange={(event) => setFormState((current) => ({ ...current, familyViewPin: event.target.value }))} />
                  </label>
                  <label className="field">
                    <span>Auth session id</span>
                    <input className="input" value={formState.authSessionId} onChange={(event) => setFormState((current) => ({ ...current, authSessionId: event.target.value }))} />
                  </label>
                  <label className="field">
                    <span>Device ID</span>
                    <input className="input" value={formState.deviceId} onChange={(event) => setFormState((current) => ({ ...current, deviceId: event.target.value }))} />
                  </label>
                  <label className="field">
                    <span>Machine name</span>
                    <input className="input" value={formState.machineName} onChange={(event) => setFormState((current) => ({ ...current, machineName: event.target.value }))} />
                  </label>
                  <label className="field">
                    <span>Country code</span>
                    <input className="input" value={formState.countryCode} onChange={(event) => setFormState((current) => ({ ...current, countryCode: event.target.value }))} />
                  </label>
                  <label className="field">
                    <span>Time zone</span>
                    <input className="input" value={formState.timeZone} onChange={(event) => setFormState((current) => ({ ...current, timeZone: event.target.value }))} />
                  </label>
                </div>
                <label className="field">
                  <span>Access token</span>
                  <textarea className="input" rows={2} value={formState.accessToken} onChange={(event) => setFormState((current) => ({ ...current, accessToken: event.target.value }))} />
                </label>
                <label className="field">
                  <span>Refresh token</span>
                  <textarea className="input" rows={2} value={formState.refreshToken} onChange={(event) => setFormState((current) => ({ ...current, refreshToken: event.target.value }))} />
                </label>
                <label className="field">
                  <span>steamLoginSecure</span>
                  <textarea className="input" rows={2} value={formState.steamLoginSecure} onChange={(event) => setFormState((current) => ({ ...current, steamLoginSecure: event.target.value }))} />
                </label>
                <label className="field">
                  <span>steamRememberLogin</span>
                  <textarea className="input" rows={2} value={formState.steamRememberLogin} onChange={(event) => setFormState((current) => ({ ...current, steamRememberLogin: event.target.value }))} />
                </label>
                <label className="field">
                  <span>Web cookie</span>
                  <textarea className="input" rows={2} value={formState.webCookie} onChange={(event) => setFormState((current) => ({ ...current, webCookie: event.target.value }))} />
                </label>
                <label className="field">
                  <span>Auth headers (JSON)</span>
                  <textarea className="input" rows={3} value={formState.authHeadersJson} onChange={(event) => setFormState((current) => ({ ...current, authHeadersJson: event.target.value }))} />
                </label>
                <label className="field">
                  <span>maFile payload (JSON/Text)</span>
                  <textarea className="input" rows={3} value={formState.maFilePayload} onChange={(event) => setFormState((current) => ({ ...current, maFilePayload: event.target.value }))} />
                </label>
              </div>
            ) : null}
            <div className="hero-actions">
              <button type="button" className="button button-primary" disabled={saveAccountMutation.isPending} onClick={() => saveAccountMutation.mutate()}>
                {editingAccountId ? "Сохранить" : "Создать"}
              </button>
              {editingAccountId ? <button type="button" className="button button-ghost" onClick={resetEditor}>Отмена</button> : null}
            </div>

            <div className="panel-title-row" id="bulk-import">
              <h3>Массовый импорт</h3>
            </div>
            <label className="field field-inline">
              <span>Показать bulk-форму</span>
              <input type="checkbox" checked={showBulkImport} onChange={(event) => setShowBulkImport(event.target.checked)} />
            </label>
            {showBulkImport ? (
              <>
                <p className="route-hint">
                  Формат строки: <code>login|password|email|emailPassword|sharedSecret|identitySecret|steamId64|proxy|tagsCsv|note</code>
                </p>
                <label className="field">
                  <span>Список аккаунтов (по одному на строку)</span>
                  <textarea className="input" rows={6} value={bulkAccountsText} onChange={(event) => setBulkAccountsText(event.target.value)} />
                </label>
                <button type="button" className="button button-primary" disabled={bulkCreateMutation.isPending || !bulkAccountsText.trim()} onClick={() => bulkCreateMutation.mutate()}>
                  Импортировать
                </button>
              </>
            ) : null}
            <div className="panel-title-row">
              <h3>Конструктор jobs</h3>
            </div>
            <p className="route-hint">Выбрано аккаунтов: {selectedVisibleAccountIds.length}</p>
            <label className="field">
              <span>Тип jobs</span>
              <select className="input" value={jobType} onChange={(event) => setJobType(event.target.value)}>
                <option value="SessionValidate">SessionValidate</option>
                <option value="SessionRefresh">SessionRefresh</option>
                <option value="ProfileUpdate">ProfileUpdate</option>
                <option value="PrivacyUpdate">PrivacyUpdate</option>
                <option value="PasswordChange">PasswordChange</option>
              </select>
            </label>
            <div className="grid-2">
              <label className="field">
                <span>Parallelism</span>
                <input className="input" value={jobParallelism} onChange={(event) => setJobParallelism(event.target.value)} />
              </label>
              <label className="field">
                <span>Retry count</span>
                <input className="input" value={jobRetryCount} onChange={(event) => setJobRetryCount(event.target.value)} />
              </label>
            </div>
            <label className="field field-inline">
              <span>Dry run</span>
              <input type="checkbox" checked={jobDryRun} onChange={(event) => setJobDryRun(event.target.checked)} />
            </label>
            <label className="field field-inline">
              <span>Расширенный payload</span>
              <input type="checkbox" checked={showAdvancedJobPayload} onChange={(event) => setShowAdvancedJobPayload(event.target.checked)} />
            </label>
            {showAdvancedJobPayload ? (
              <label className="field">
                <span>Payload (JSON)</span>
                <textarea className="input" rows={4} value={jobPayloadJson} onChange={(event) => setJobPayloadJson(event.target.value)} />
              </label>
            ) : null}
            <button type="button" className="button button-primary" disabled={createJobMutation.isPending || selectedVisibleAccountIds.length === 0} onClick={runConfiguredJob}>
              Запустить jobs
            </button>
          </article>
        </aside>
      </section>

      <p className="route-hint">{statusMessage}</p>
    </div>
  );
}
