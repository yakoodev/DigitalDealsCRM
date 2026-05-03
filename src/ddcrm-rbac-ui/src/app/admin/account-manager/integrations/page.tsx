"use client";

import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { AdminLayout } from "@/components/layout/admin-layout";
import {
  checkAdminTelegramConnectivityRequest,
  listAdminProjectIntegrationGrantsRequest,
  listAdminTelegramProxyProfilesRequest,
  listProjectsRequest,
  revokeAdminProjectIntegrationGrantRequest,
  sendAdminTelegramTestMessageRequest,
  triggerAdminIntegrationRuntimeRequest,
  upsertAdminProjectIntegrationGrantRequest,
  upsertAdminTelegramProxyProfileRequest,
  type AdminIntegrationGrant,
  type ApiSession,
} from "@/lib/api-client";
import type { Project } from "@/generated/external-api";
import { SYSTEM_INTEGRATIONS_PERMISSION } from "@/lib/auth";
import { useSessionGuard } from "@/lib/use-session-guard";

type IntegrationPreset = {
  key: string;
  title: string;
  integrationType: "service" | "worker" | "notification" | "platform";
  description: string;
  defaultScopes: string[];
};

const integrationPresets: readonly IntegrationPreset[] = [
  {
    key: "telegram",
    title: "Telegram Notifications",
    integrationType: "notification",
    description: "Notification-bus для Telegram bind flow и критичных уведомлений.",
    defaultScopes: ["send"],
  },
  {
    key: "steam-accounts-manager",
    title: "Steam Runtime",
    integrationType: "worker",
    description: "Steam как per-project worker runtime.",
    defaultScopes: ["read", "jobs"],
  },
  {
    key: "funpaystat",
    title: "FunPayStat Service",
    integrationType: "service",
    description: "Service-bus интеграция для read/jobs аналитики.",
    defaultScopes: ["read", "jobs"],
  },
  {
    key: "platform.funpay",
    title: "FunPay Platform Access",
    integrationType: "platform",
    description: "Платформенный grant для работы с аккаунтами FunPay в проекте.",
    defaultScopes: ["use"],
  },
] as const;

function parseScopes(value: string) {
  return value
    .split(",")
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}

function scopesToText(scopes: readonly string[]) {
  return scopes.length > 0 ? scopes.join(",") : "use";
}

function toProjectOptionLabel(project: Project) {
  return `${project.name} · ${project.id}`;
}

function readGrantByIntegrationKey(
  grants: readonly AdminIntegrationGrant[] | undefined,
  integrationKey: string,
) {
  return (grants ?? []).find((item) => item.integrationKey === integrationKey) ?? null;
}

export default function AccountManagerIntegrationsPage() {
  const queryClient = useQueryClient();
  const { session, logout } = useSessionGuard();

  const [projectId, setProjectId] = useState("");
  const [selectedPresetKey, setSelectedPresetKey] = useState(integrationPresets[0]?.key ?? "");
  const [integrationKey, setIntegrationKey] = useState(integrationPresets[0]?.key ?? "steam-accounts-manager");
  const [scopes, setScopes] = useState(scopesToText(integrationPresets[0]?.defaultScopes ?? ["read", "jobs"]));
  const [grantStatus, setGrantStatus] = useState(
    "Выберите проект, затем используйте пресет или ручной key/scopes для grant/revoke.",
  );
  const [matrixFilter, setMatrixFilter] = useState("");
  const [matrixStatus, setMatrixStatus] = useState(
    "Матрица показывает доступы всех проектов по ключевым интеграциям.",
  );
  const [runtimeStatus, setRuntimeStatus] = useState(
    "Runtime-операции доступны для worker-интеграций (Steam).",
  );

  const [proxyId, setProxyId] = useState(() =>
    typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
      ? crypto.randomUUID()
      : "00000000-0000-0000-0000-000000000000",
  );
  const [proxyName, setProxyName] = useState(() =>
    typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
      ? `tg-proxy-${crypto.randomUUID().slice(0, 8)}`
      : "tg-proxy-main",
  );
  const [proxyType, setProxyType] = useState<"http" | "https" | "socks5">("http");
  const [proxyHost, setProxyHost] = useState("127.0.0.1");
  const [proxyPort, setProxyPort] = useState("8080");
  const [proxySetActive, setProxySetActive] = useState(true);
  const [proxyClearCredentials, setProxyClearCredentials] = useState(false);
  const [proxyLogin, setProxyLogin] = useState("");
  const [proxyPassword, setProxyPassword] = useState("");
  const [proxyStatus, setProxyStatus] = useState(
    "Профиль прокси сохраняется без reveal credential-ов (write-only).",
  );
  const [telegramTestChatId, setTelegramTestChatId] = useState("");
  const [telegramTestMessage, setTelegramTestMessage] = useState("DDCRM test message");
  const [telegramTestStatus, setTelegramTestStatus] = useState(
    "Укажите chatId и отправьте тестовое сообщение через активный proxy.",
  );
  const [telegramConnectivityStatus, setTelegramConnectivityStatus] = useState(
    "Проверьте доступность Telegram API (getMe) через активный proxy/без proxy.",
  );

  const apiSession = useMemo<ApiSession>(
    () => ({
      token: session?.token ?? "",
      baseUrl: session?.baseUrl ?? "",
    }),
    [session?.baseUrl, session?.token],
  );

  const normalizedProjectId = projectId.trim();
  const normalizedIntegrationKey = integrationKey.trim();
  const hasIntegrationsPermission = useMemo(() => {
    const permissions = session?.profile.systemPermissions ?? [];
    return permissions.some((item) => item.toLowerCase() === SYSTEM_INTEGRATIONS_PERMISSION.toLowerCase());
  }, [session?.profile.systemPermissions]);

  const projectsQuery = useQuery({
    queryKey: ["projects", apiSession.baseUrl, apiSession.token],
    queryFn: () => listProjectsRequest(apiSession),
    enabled: Boolean(session?.profile.isSystemAdmin),
    staleTime: 20_000,
  });

  useEffect(() => {
    if (normalizedProjectId) {
      return;
    }

    const firstProject = projectsQuery.data?.[0];
    if (firstProject) {
      setProjectId(firstProject.id);
    }
  }, [normalizedProjectId, projectsQuery.data]);

  const grantsQuery = useQuery({
    queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token, normalizedProjectId],
    queryFn: () => listAdminProjectIntegrationGrantsRequest(apiSession, normalizedProjectId),
    enabled: Boolean(session?.profile.isSystemAdmin && normalizedProjectId),
  });

  const proxiesQuery = useQuery({
    queryKey: ["admin-telegram-proxies", apiSession.baseUrl, apiSession.token],
    queryFn: () => listAdminTelegramProxyProfilesRequest(apiSession),
    enabled: Boolean(session?.profile.isSystemAdmin),
  });

  const projectGrantQueries = useQueries({
    queries: (projectsQuery.data ?? []).map((project) => ({
      queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token, project.id],
      queryFn: () => listAdminProjectIntegrationGrantsRequest(apiSession, project.id),
      enabled: Boolean(session?.profile.isSystemAdmin),
      staleTime: 10_000,
    })),
  });

  const grantMutation = useMutation({
    mutationFn: (variables: { integrationKey: string; scopesText: string }) =>
      upsertAdminProjectIntegrationGrantRequest(apiSession, normalizedProjectId, variables.integrationKey.trim(), {
        scopes: parseScopes(variables.scopesText),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token, normalizedProjectId],
      });
      setGrantStatus("Grant сохранён.");
    },
    onMutate: () => {
      setGrantStatus("Сохраняю grant...");
    },
    onError: (error) => {
      setGrantStatus(error instanceof Error ? error.message : "Не удалось сохранить grant.");
    },
  });

  const revokeMutation = useMutation({
    mutationFn: (variables: { integrationKey: string }) =>
      revokeAdminProjectIntegrationGrantRequest(apiSession, normalizedProjectId, variables.integrationKey.trim()),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token, normalizedProjectId],
      });
      setGrantStatus("Grant отозван.");
    },
    onMutate: () => {
      setGrantStatus("Отзываю grant...");
    },
    onError: (error) => {
      setGrantStatus(error instanceof Error ? error.message : "Не удалось отозвать grant.");
    },
  });

  const matrixGrantMutation = useMutation({
    mutationFn: (variables: { projectId: string; integrationKey: string; scopesText: string }) =>
      upsertAdminProjectIntegrationGrantRequest(apiSession, variables.projectId, variables.integrationKey.trim(), {
        scopes: parseScopes(variables.scopesText),
      }),
    onMutate: () => {
      setMatrixStatus("Сохраняю grant в матрице...");
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token],
      });
      setMatrixStatus("Grant в матрице обновлён.");
    },
    onError: (error) => {
      setMatrixStatus(error instanceof Error ? error.message : "Не удалось обновить grant в матрице.");
    },
  });

  const matrixRevokeMutation = useMutation({
    mutationFn: (variables: { projectId: string; integrationKey: string }) =>
      revokeAdminProjectIntegrationGrantRequest(apiSession, variables.projectId, variables.integrationKey.trim()),
    onMutate: () => {
      setMatrixStatus("Отзываю grant в матрице...");
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token],
      });
      setMatrixStatus("Grant в матрице отозван.");
    },
    onError: (error) => {
      setMatrixStatus(error instanceof Error ? error.message : "Не удалось отозвать grant в матрице.");
    },
  });

  const matrixRuntimeMutation = useMutation({
    mutationFn: (variables: {
      projectId: string;
      integrationKey: string;
      operation: "provision" | "deprovision" | "restart";
    }) => triggerAdminIntegrationRuntimeRequest(
      apiSession,
      variables.projectId,
      variables.integrationKey.trim(),
      variables.operation,
    ),
    onMutate: () => {
      setMatrixStatus("Ставлю runtime-операцию в очередь...");
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token],
      });
      setMatrixStatus("Runtime-операция поставлена в очередь.");
    },
    onError: (error) => {
      setMatrixStatus(error instanceof Error ? error.message : "Не удалось выполнить runtime-операцию из матрицы.");
    },
  });

  const runtimeMutation = useMutation({
    mutationFn: (variables: {
      integrationKey: string;
      operation: "provision" | "deprovision" | "restart";
    }) => triggerAdminIntegrationRuntimeRequest(
      apiSession,
      normalizedProjectId,
      variables.integrationKey.trim(),
      variables.operation,
    ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token, normalizedProjectId],
      });
      setRuntimeStatus("Runtime-операция поставлена в очередь.");
    },
    onError: (error) => {
      setRuntimeStatus(error instanceof Error ? error.message : "Не удалось запустить runtime-операцию.");
    },
  });

  const proxyMutation = useMutation({
    mutationFn: () => upsertAdminTelegramProxyProfileRequest(apiSession, proxyId.trim(), {
      name: proxyName.trim(),
      proxyType,
      host: proxyHost.trim(),
      port: Number(proxyPort),
      setActive: proxySetActive,
      clearCredentials: proxyClearCredentials,
      login: proxyLogin.trim() || undefined,
      password: proxyPassword.trim() || undefined,
    }),
    onSuccess: async (payload) => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-telegram-proxies", apiSession.baseUrl, apiSession.token],
      });
      setProxyPassword("");
      setProxyClearCredentials(false);
      setProxyStatus(`Proxy-профиль сохранён: ${payload.name}.`);
    },
    onError: (error) => {
      setProxyStatus(error instanceof Error ? error.message : "Не удалось сохранить proxy-профиль.");
    },
  });

  const telegramTestMutation = useMutation({
    mutationFn: () => sendAdminTelegramTestMessageRequest(apiSession, {
      chatId: telegramTestChatId.trim(),
      message: telegramTestMessage.trim() || undefined,
    }),
    onSuccess: () => {
      setTelegramTestStatus("Тестовое сообщение отправлено. Проверьте чат и/или логи outbox.");
    },
    onError: (error) => {
      setTelegramTestStatus(error instanceof Error ? error.message : "Не удалось отправить тестовое сообщение.");
    },
  });

  const telegramConnectivityMutation = useMutation({
    mutationFn: () => checkAdminTelegramConnectivityRequest(apiSession),
    onSuccess: (payload) => {
      if (payload.status === "ok") {
        const username = payload.username ? `@${payload.username}` : "(без username)";
        const reason = payload.reasonCode ? `; reason=${payload.reasonCode}` : "";
        setTelegramConnectivityStatus(
          `Telegram API доступен через ${payload.effectivePath}: botId=${payload.botId ?? "n/a"}, ${username}${reason}. matrix: proxy=${payload.proxySucceeded ? "ok" : "fail"}, direct=${payload.directSucceeded ? "ok" : "fail"}.`,
        );
        return;
      }

      setTelegramConnectivityStatus(
        `Connectivity error (${payload.reasonCode ?? "unknown"}). matrix: proxy=${payload.proxySucceeded ? "ok" : "fail"}, direct=${payload.directSucceeded ? "ok" : "fail"}. proxyError=${payload.proxyError ?? "n/a"}; directError=${payload.directError ?? "n/a"}.`,
      );
    },
    onError: (error) => {
      setTelegramConnectivityStatus(error instanceof Error ? error.message : "Не удалось выполнить connectivity-check.");
    },
  });

  if (!session) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>Проверяем сессию...</h1>
        </section>
      </main>
    );
  }

  if (!session.profile.isSystemAdmin) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>403 · System admin required</h1>
          <p className="route-error">Недостаточно системных прав для управления integration bus.</p>
        </section>
      </main>
    );
  }

  if (!hasIntegrationsPermission) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>403 · Missing integrations permission</h1>
          <p className="route-error">
            Для управления integrations нужен системный permission <code>{SYSTEM_INTEGRATIONS_PERMISSION}</code>.
          </p>
          <p className="route-hint">
            Перелогиньтесь пользователем с полным набором системных прав, затем повторите операцию grant/proxy.
          </p>
        </section>
      </main>
    );
  }

  const grantItems = grantsQuery.data ?? [];
  const proxyItems = proxiesQuery.data ?? [];
  const projectMatrixRows = (projectsQuery.data ?? []).map((project, index) => {
    const grantQuery = projectGrantQueries[index];
    return {
      project,
      grants: grantQuery?.data ?? [],
      isPending: grantQuery?.isPending ?? false,
      error: grantQuery?.error,
    };
  });
  const matrixToken = matrixFilter.trim().toLowerCase();
  const filteredProjectMatrixRows = matrixToken
    ? projectMatrixRows.filter((row) =>
      row.project.name.toLowerCase().includes(matrixToken)
      || row.project.id.toLowerCase().includes(matrixToken))
    : projectMatrixRows;

  const selectedPreset = integrationPresets.find((item) => item.key === selectedPresetKey) ?? null;

  const applyPreset = (preset: IntegrationPreset, issueGrant: boolean) => {
    const scopesText = scopesToText(preset.defaultScopes);
    setSelectedPresetKey(preset.key);
    setIntegrationKey(preset.key);
    setScopes(scopesText);
    setGrantStatus(`Пресет применён: ${preset.title}.`);

    if (!issueGrant) {
      return;
    }

    if (!normalizedProjectId) {
      setGrantStatus("Сначала выберите проект, затем выдайте интеграцию.");
      return;
    }

    grantMutation.mutate({
      integrationKey: preset.key,
      scopesText,
    });
  };

  return (
    <AdminLayout session={session} activeTab="integrations" onLogout={logout}>
      <section className="module-board">
        <section className="module-main-column">
          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Матрица доступов проектов</h3>
            </div>
            <p className="route-hint">
              Ориентация матрицы: сверху проекты, слева интеграции. На пересечении выдаём grant, управляем runtime и быстро
              переходим в Steam-кабинет для добавления нескольких аккаунтов.
            </p>
            <div className="inline-actions">
              <input
                className="input"
                value={matrixFilter}
                onChange={(event) => setMatrixFilter(event.target.value)}
                placeholder="Фильтр: имя проекта или UUID"
              />
            </div>
            {projectsQuery.isPending ? <p className="route-hint">Загрузка проектов для матрицы...</p> : null}
            {projectsQuery.error ? (
              <p className="route-error">
                {projectsQuery.error instanceof Error ? projectsQuery.error.message : "Не удалось загрузить матрицу."}
              </p>
            ) : null}
            {!projectsQuery.isPending && !projectsQuery.error && filteredProjectMatrixRows.length === 0 ? (
              <p className="route-hint">Проекты не найдены по текущему фильтру.</p>
            ) : null}
            {!projectsQuery.isPending && !projectsQuery.error && filteredProjectMatrixRows.length > 0 ? (
              <div className="matrix-scroll">
                <table className="matrix-table">
                  <thead>
                    <tr>
                      <th>Интеграция</th>
                      {filteredProjectMatrixRows.map((row) => (
                        <th key={`matrix-head-${row.project.id}`}>
                          <div className="page-stack matrix-heading">
                            <strong>{row.project.name}</strong>
                            <span className="route-hint">{row.project.id}</span>
                            <button
                              type="button"
                              className="button button-ghost"
                              onClick={() => {
                                setProjectId(row.project.id);
                                setMatrixStatus(`Проект выбран: ${row.project.name}.`);
                              }}
                            >
                              Использовать
                            </button>
                          </div>
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {integrationPresets.map((preset) => (
                      <tr key={`matrix-row-${preset.key}`}>
                        <td className="matrix-project-cell">
                          <div className="page-stack">
                            <strong>{preset.title}</strong>
                            <small>{preset.key}</small>
                            <span className="entity-pill">{preset.integrationType}</span>
                            <small>default scopes: {preset.defaultScopes.join(", ")}</small>
                          </div>
                        </td>
                        {filteredProjectMatrixRows.map((row) => {
                          const grant = readGrantByIntegrationKey(row.grants, preset.key);
                          const hasGrant = grant && grant.status === "active";
                          return (
                            <td key={`matrix-cell-${row.project.id}-${preset.key}`}>
                              <div className="page-stack matrix-cell">
                                {row.isPending ? (
                                  <span className="entity-pill">loading...</span>
                                ) : row.error ? (
                                  <span className="entity-pill">error</span>
                                ) : grant ? (
                                  <div className="entity-pills">
                                    <span className={`entity-pill ${hasGrant ? "is-pill-ok" : "is-pill-danger"}`}>
                                      {grant.status}
                                    </span>
                                    <span className="entity-pill">{grant.integrationType}</span>
                                    <span className="entity-pill">{grant.scopes.join(", ") || "use"}</span>
                                    {grant.runtimeStatus ? (
                                      <span className="entity-pill">runtime: {grant.runtimeStatus}</span>
                                    ) : null}
                                    {grant.runtimeAccountId ? (
                                      <span className="entity-pill">rk.{grant.runtimeAccountId.replaceAll("-", "")}</span>
                                    ) : null}
                                  </div>
                                ) : (
                                  <span className="entity-pill">нет grant</span>
                                )}
                                <div className="inline-actions">
                                  <button
                                    type="button"
                                    className="button button-primary"
                                    disabled={
                                      matrixGrantMutation.isPending
                                      || matrixRevokeMutation.isPending
                                      || matrixRuntimeMutation.isPending
                                      || row.isPending
                                    }
                                    onClick={() =>
                                      matrixGrantMutation.mutate({
                                        projectId: row.project.id,
                                        integrationKey: preset.key,
                                        scopesText: scopesToText(preset.defaultScopes),
                                      })
                                    }
                                  >
                                    {hasGrant ? "Обновить" : "Выдать"}
                                  </button>
                                  <button
                                    type="button"
                                    className="button button-ghost"
                                    disabled={
                                      !hasGrant
                                      || matrixGrantMutation.isPending
                                      || matrixRevokeMutation.isPending
                                      || matrixRuntimeMutation.isPending
                                      || row.isPending
                                    }
                                    onClick={() =>
                                      matrixRevokeMutation.mutate({
                                        projectId: row.project.id,
                                        integrationKey: preset.key,
                                      })
                                    }
                                    >
                                      Отозвать
                                  </button>
                                  {preset.integrationType === "worker" ? (
                                    <>
                                      <button
                                        type="button"
                                        className="button button-ghost"
                                        disabled={
                                          matrixRuntimeMutation.isPending
                                          || matrixGrantMutation.isPending
                                          || matrixRevokeMutation.isPending
                                          || !hasGrant
                                        }
                                        onClick={() =>
                                          matrixRuntimeMutation.mutate({
                                            projectId: row.project.id,
                                            integrationKey: preset.key,
                                            operation: "provision",
                                          })
                                        }
                                      >
                                        Provision
                                      </button>
                                      <button
                                        type="button"
                                        className="button button-ghost"
                                        disabled={
                                          matrixRuntimeMutation.isPending
                                          || matrixGrantMutation.isPending
                                          || matrixRevokeMutation.isPending
                                          || !hasGrant
                                        }
                                        onClick={() =>
                                          matrixRuntimeMutation.mutate({
                                            projectId: row.project.id,
                                            integrationKey: preset.key,
                                            operation: "restart",
                                          })
                                        }
                                        >
                                          Restart
                                      </button>
                                      <a className="button button-ghost" href={`/projects/${row.project.id}/steam`}>
                                        Steam кабинет
                                      </a>
                                      <a className="button button-ghost" href={`/projects/${row.project.id}/steam#bulk-import`}>
                                        Добавить аккаунты
                                      </a>
                                    </>
                                  ) : null}
                                </div>
                                {preset.integrationType === "worker" ? (
                                  <p className="route-hint">
                                    Мульти-аккаунтный ввод и массовые задачи доступны в Steam-кабинете проекта.
                                  </p>
                                ) : null}
                              </div>
                            </td>
                          );
                        })}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
            <p className="route-hint">{matrixStatus}</p>
          </article>

          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Проект и grant-ы</h3>
            </div>
            <label className="field">
              <span>Проект (из моих доступных)</span>
              <select
                className="input"
                value={projectsQuery.data?.some((item) => item.id === normalizedProjectId) ? normalizedProjectId : ""}
                onChange={(event) => setProjectId(event.target.value)}
              >
                <option value="">Выберите проект</option>
                {(projectsQuery.data ?? []).map((project) => (
                  <option key={project.id} value={project.id}>
                    {toProjectOptionLabel(project)}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Project ID (можно вставить вручную)</span>
              <input
                className="input"
                value={projectId}
                onChange={(event) => setProjectId(event.target.value)}
                placeholder="uuid проекта"
              />
            </label>
            {projectsQuery.isPending ? <p className="route-hint">Загрузка проектов...</p> : null}
            {projectsQuery.error ? (
              <p className="route-error">
                {projectsQuery.error instanceof Error ? projectsQuery.error.message : "Не удалось загрузить проекты."}
              </p>
            ) : null}

            <div className="panel-title-row">
              <h3>Пресеты интеграций</h3>
            </div>
            <ul className="entity-list">
              {integrationPresets.map((preset) => (
                <li
                  key={preset.key}
                  className={`entity-list-item ${selectedPresetKey === preset.key ? "is-selected" : ""}`}
                >
                  <div>
                    <strong>{preset.title}</strong>
                    <div className="entity-pills">
                      <span className="entity-pill">{preset.key}</span>
                      <span className="entity-pill">{preset.integrationType}</span>
                      <span className="entity-pill">scopes: {preset.defaultScopes.join(", ")}</span>
                    </div>
                    <p className="route-hint">{preset.description}</p>
                  </div>
                  <div className="inline-actions">
                    <button
                      type="button"
                      className="button button-ghost"
                      onClick={() => applyPreset(preset, false)}
                    >
                      Заполнить форму
                    </button>
                    <button
                      type="button"
                      className="button button-primary"
                      disabled={grantMutation.isPending || !normalizedProjectId}
                      onClick={() => applyPreset(preset, true)}
                    >
                      Выдать проекту
                    </button>
                  </div>
                </li>
              ))}
            </ul>

            {grantsQuery.isPending ? <p className="route-hint">Загрузка grant-ов...</p> : null}
            {grantsQuery.error ? (
              <p className="route-error">
                {grantsQuery.error instanceof Error ? grantsQuery.error.message : "Не удалось загрузить grant-ы."}
              </p>
            ) : null}
            {!grantsQuery.isPending && !grantsQuery.error && normalizedProjectId && grantItems.length === 0 ? (
              <p className="route-hint">Для выбранного проекта grant-ы пока не выданы.</p>
            ) : null}
            {!grantsQuery.isPending && !grantsQuery.error && grantItems.length > 0 ? (
              <ul className="entity-list">
                {grantItems.map((item) => (
                  <li key={item.integrationKey} className="entity-list-item">
                    <div>
                      <strong>{item.integrationKey}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{item.status}</span>
                        <span className="entity-pill">{item.integrationType}</span>
                        <span className="entity-pill">scopes: {item.scopes.join(", ") || "n/a"}</span>
                        <span className="entity-pill">{item.credentialStatus ?? "no-credential"}</span>
                        {item.runtimeStatus ? <span className="entity-pill">runtime: {item.runtimeStatus}</span> : null}
                      </div>
                      {item.runtimeAccountId ? (
                        <p className="route-hint">rk.{item.runtimeAccountId.replaceAll("-", "")}</p>
                      ) : null}
                      {item.runtimeLastError ? <p className="route-error">Runtime error: {item.runtimeLastError}</p> : null}
                    </div>
                    <div className="inline-actions">
                      <button
                        type="button"
                        className="button button-ghost"
                        disabled={grantMutation.isPending || !normalizedProjectId}
                        onClick={() => {
                          setIntegrationKey(item.integrationKey);
                          setScopes(scopesToText(item.scopes));
                          grantMutation.mutate({ integrationKey: item.integrationKey, scopesText: scopesToText(item.scopes) });
                        }}
                      >
                        Обновить
                      </button>
                      <button
                        type="button"
                        className="button button-ghost"
                        disabled={revokeMutation.isPending || !normalizedProjectId}
                        onClick={() => revokeMutation.mutate({ integrationKey: item.integrationKey })}
                      >
                        Отозвать
                      </button>
                      {item.integrationType === "worker" ? (
                        <>
                          <button
                            type="button"
                            className="button button-ghost"
                            disabled={runtimeMutation.isPending || !normalizedProjectId}
                            onClick={() =>
                              runtimeMutation.mutate({ integrationKey: item.integrationKey, operation: "provision" })
                            }
                          >
                            Provision
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            disabled={runtimeMutation.isPending || !normalizedProjectId}
                            onClick={() =>
                              runtimeMutation.mutate({ integrationKey: item.integrationKey, operation: "restart" })
                            }
                          >
                            Restart
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            disabled={runtimeMutation.isPending || !normalizedProjectId}
                            onClick={() =>
                              runtimeMutation.mutate({ integrationKey: item.integrationKey, operation: "deprovision" })
                            }
                          >
                            Deprovision
                          </button>
                        </>
                      ) : null}
                    </div>
                  </li>
                ))}
              </ul>
            ) : null}
          </article>

          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Telegram proxy profiles</h3>
            </div>
            {proxiesQuery.isPending ? <p className="route-hint">Загрузка proxy-профилей...</p> : null}
            {proxiesQuery.error ? (
              <p className="route-error">
                {proxiesQuery.error instanceof Error ? proxiesQuery.error.message : "Не удалось загрузить proxy-профили."}
              </p>
            ) : null}
            {!proxiesQuery.isPending && !proxiesQuery.error && proxyItems.length === 0 ? (
              <p className="route-hint">Список proxy-профилей пуст.</p>
            ) : null}
            {!proxiesQuery.isPending && !proxiesQuery.error && proxyItems.length > 0 ? (
              <ul className="entity-list">
                {proxyItems.map((item) => (
                  <li key={item.id} className="entity-list-item">
                    <div>
                      <strong>{item.name}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{item.proxyType}</span>
                        <span className="entity-pill">{item.host}:{item.port}</span>
                        <span className="entity-pill">{item.isActive ? "active" : "inactive"}</span>
                        <span className="entity-pill">{item.hasCredentials ? "credentials:on" : "credentials:off"}</span>
                      </div>
                    </div>
                    <button
                      type="button"
                      className="button button-ghost"
                      onClick={() => {
                        setProxyId(item.id);
                        setProxyName(item.name);
                        setProxyType(item.proxyType);
                        setProxyHost(item.host);
                        setProxyPort(String(item.port));
                        setProxySetActive(item.isActive);
                        setProxyLogin("");
                        setProxyPassword("");
                        setProxyClearCredentials(false);
                      }}
                    >
                      В форму
                    </button>
                  </li>
                ))}
              </ul>
            ) : null}
          </article>
        </section>

        <aside className="module-side-column">
          <article className="glass-card page-stack panel-card-sticky">
            <div className="panel-title-row">
              <h3>Grant/Revoke</h3>
            </div>
            <label className="field">
              <span>Preset</span>
              <select
                className="input"
                value={selectedPreset?.key ?? ""}
                onChange={(event) => {
                  const next = integrationPresets.find((item) => item.key === event.target.value);
                  if (!next) {
                    return;
                  }

                  setSelectedPresetKey(next.key);
                  setIntegrationKey(next.key);
                  setScopes(scopesToText(next.defaultScopes));
                }}
              >
                {integrationPresets.map((preset) => (
                  <option key={preset.key} value={preset.key}>
                    {preset.title}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Integration key</span>
              <input
                className="input"
                value={integrationKey}
                onChange={(event) => setIntegrationKey(event.target.value)}
                placeholder="steam-accounts-manager"
              />
            </label>
            <label className="field">
              <span>Scopes (csv)</span>
              <input
                className="input"
                value={scopes}
                onChange={(event) => setScopes(event.target.value)}
                placeholder="read,jobs"
              />
            </label>
            <div className="hero-actions">
              <button
                type="button"
                className="button button-primary"
                disabled={grantMutation.isPending || !normalizedProjectId || !normalizedIntegrationKey}
                onClick={() => grantMutation.mutate({ integrationKey: normalizedIntegrationKey, scopesText: scopes })}
              >
                Выдать/обновить
              </button>
              <button
                type="button"
                className="button button-ghost"
                disabled={revokeMutation.isPending || !normalizedProjectId || !normalizedIntegrationKey}
                onClick={() => revokeMutation.mutate({ integrationKey: normalizedIntegrationKey })}
              >
                Отозвать
              </button>
            </div>
            <p className="route-hint">{grantStatus}</p>

            <div className="panel-title-row">
              <h3>Worker runtime</h3>
            </div>
            <div className="hero-actions">
              <button
                type="button"
                className="button button-ghost"
                disabled={runtimeMutation.isPending || !normalizedProjectId || !normalizedIntegrationKey}
                onClick={() =>
                  runtimeMutation.mutate({ integrationKey: normalizedIntegrationKey, operation: "provision" })
                }
              >
                Provision
              </button>
              <button
                type="button"
                className="button button-ghost"
                disabled={runtimeMutation.isPending || !normalizedProjectId || !normalizedIntegrationKey}
                onClick={() => runtimeMutation.mutate({ integrationKey: normalizedIntegrationKey, operation: "restart" })}
              >
                Restart
              </button>
              <button
                type="button"
                className="button button-ghost"
                disabled={runtimeMutation.isPending || !normalizedProjectId || !normalizedIntegrationKey}
                onClick={() =>
                  runtimeMutation.mutate({ integrationKey: normalizedIntegrationKey, operation: "deprovision" })
                }
              >
                Deprovision
              </button>
            </div>
            <p className="route-hint">{runtimeStatus}</p>

            <div className="panel-title-row">
              <h3>Telegram Grant</h3>
            </div>
            <div className="hero-actions">
              <button
                type="button"
                className="button button-primary"
                disabled={grantMutation.isPending || !normalizedProjectId}
                onClick={() => {
                  setSelectedPresetKey("telegram");
                  setIntegrationKey("telegram");
                  setScopes("send");
                  grantMutation.mutate({ integrationKey: "telegram", scopesText: "send" });
                }}
              >
                Выдать Telegram проекту
              </button>
              <button
                type="button"
                className="button button-ghost"
                disabled={revokeMutation.isPending || !normalizedProjectId}
                onClick={() => revokeMutation.mutate({ integrationKey: "telegram" })}
              >
                Отозвать Telegram
              </button>
            </div>
            <p className="route-hint">
              Эта кнопка нужна для включения bind-flow и отправки уведомлений в Telegram для выбранного проекта.
            </p>

            <div className="panel-title-row">
              <h3>Telegram proxy</h3>
            </div>
            <label className="field">
              <span>Proxy ID (uuid)</span>
              <input className="input" value={proxyId} onChange={(event) => setProxyId(event.target.value)} />
            </label>
            <label className="field">
              <span>Name</span>
              <input className="input" value={proxyName} onChange={(event) => setProxyName(event.target.value)} />
            </label>
            <div className="grid-2">
              <label className="field">
                <span>Type</span>
                <select
                  className="input"
                  value={proxyType}
                  onChange={(event) => setProxyType(event.target.value as "http" | "https" | "socks5")}
                >
                  <option value="http">http</option>
                  <option value="https">https</option>
                  <option value="socks5">socks5</option>
                </select>
              </label>
              <label className="field">
                <span>Port</span>
                <input className="input" value={proxyPort} onChange={(event) => setProxyPort(event.target.value)} />
              </label>
            </div>
            <label className="field">
              <span>Host</span>
              <input className="input" value={proxyHost} onChange={(event) => setProxyHost(event.target.value)} />
            </label>
            <label className="field">
              <span>Login (optional)</span>
              <input className="input" value={proxyLogin} onChange={(event) => setProxyLogin(event.target.value)} />
            </label>
            <label className="field">
              <span>Password (write-only)</span>
              <input
                className="input"
                type="password"
                value={proxyPassword}
                onChange={(event) => setProxyPassword(event.target.value)}
              />
            </label>
            <label className="field field-inline">
              <span>Set active</span>
              <input type="checkbox" checked={proxySetActive} onChange={(event) => setProxySetActive(event.target.checked)} />
            </label>
            <label className="field field-inline">
              <span>Clear credentials</span>
              <input
                type="checkbox"
                checked={proxyClearCredentials}
                onChange={(event) => setProxyClearCredentials(event.target.checked)}
              />
            </label>
            <button
              type="button"
              className="button button-primary"
              disabled={proxyMutation.isPending || !proxyId.trim() || !proxyName.trim() || !proxyHost.trim()}
              onClick={() => proxyMutation.mutate()}
            >
              Сохранить proxy
            </button>
            <p className="route-hint">{proxyStatus}</p>

            <div className="panel-title-row">
              <h3>Проверка Telegram</h3>
            </div>
            <label className="field">
              <span>Chat ID для теста</span>
              <input
                className="input"
                value={telegramTestChatId}
                onChange={(event) => setTelegramTestChatId(event.target.value)}
                placeholder="например: 123456789"
              />
            </label>
            <label className="field">
              <span>Сообщение</span>
              <input
                className="input"
                value={telegramTestMessage}
                onChange={(event) => setTelegramTestMessage(event.target.value)}
                placeholder="DDCRM test message"
              />
            </label>
            <button
              type="button"
              className="button button-primary"
              disabled={telegramTestMutation.isPending || !telegramTestChatId.trim()}
              onClick={() => telegramTestMutation.mutate()}
            >
              Отправить тест в Telegram
            </button>
            <p className="route-hint">{telegramTestStatus}</p>
            <button
              type="button"
              className="button button-ghost"
              disabled={telegramConnectivityMutation.isPending}
              onClick={() => telegramConnectivityMutation.mutate()}
            >
              Проверить Telegram API (getMe)
            </button>
            <p className="route-hint">{telegramConnectivityStatus}</p>
          </article>
        </aside>
      </section>
    </AdminLayout>
  );
}
