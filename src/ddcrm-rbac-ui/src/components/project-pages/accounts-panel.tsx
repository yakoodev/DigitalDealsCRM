"use client";

import { useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useMemo, useState } from "react";
import { RouteModalHost } from "@/components/layout/route-modal-host";
import { ProjectAccountCreatePanel } from "@/components/project-pages/account-create-panel";
import { ProjectAccountManagePanel } from "@/components/project-pages/account-manage-panel";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import { useRouteModal } from "@/hooks/use-route-modal";
import type { ApiSession } from "@/lib/api-client";
import { listProjectIntegrationsStatusRequest, runAccountActionRequest } from "@/lib/api-client";
import { hasPermission, projectPermissions, type ProjectRole } from "@/lib/rbac";
import { isRecord, toReadableValue } from "@/lib/worker-result";

interface ProjectAccountsPanelProps {
  apiSession: ApiSession;
  projectId: string;
  activeRole: ProjectRole;
  canOpenIntegrationGrants?: boolean;
}

interface NormalizedAccountInfo {
  accountId: string;
  platform: string;
  nickname: string;
  status: string;
  displayName: string;
  balance: string;
  productsCount: string;
  unreadCount: string;
  workerInstanceId: string;
  workerMachineName: string;
  workerStartedAtUtc: string;
  requestId: string;
  proxyName: string;
}

function normalizeBalance(value: unknown): string {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value.toFixed(2);
  }

  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) {
      return parsed.toFixed(2);
    }
  }

  return "";
}

function normalizeCount(value: unknown): string {
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }

  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) {
      return String(parsed);
    }
  }

  return "";
}

function normalizeAccountInfo(payload: Record<string, unknown>): NormalizedAccountInfo {
  const account = isRecord(payload.account) ? payload.account : payload;
  const profile = isRecord(account.profile) ? account.profile : null;
  const raw = isRecord(account.raw) ? account.raw : null;
  const stats = isRecord(account.stats) ? account.stats : null;
  const telemetry = isRecord(account.telemetry) ? account.telemetry : null;
  const proxy = isRecord(account.proxy) ? account.proxy : null;

  const accountId = toReadableValue(account.accountId ?? payload.accountId);
  const platform = toReadableValue(
    account.provider
      ?? account.service
      ?? account.platform
      ?? payload.provider
      ?? payload.platform,
  );
  const nickname = toReadableValue(account.nickname ?? payload.nickname);
  const status = toReadableValue(account.status ?? payload.status);
  const displayName = toReadableValue(
    profile?.displayName ?? account.displayName ?? payload.displayName,
  );
  const balance = normalizeBalance(profile?.balance ?? account.balance ?? payload.balance);

  return {
    accountId,
    platform,
    nickname,
    status,
    displayName,
    balance,
    productsCount: normalizeCount(
      stats?.productsCount
      ?? telemetry?.productsCount
      ?? account.productsCount
      ?? payload.productsCount,
    ),
    unreadCount: normalizeCount(
      stats?.unreadCount
      ?? telemetry?.unreadCount
      ?? account.unreadCount
      ?? payload.unreadCount,
    ),
    workerInstanceId: toReadableValue(raw?.workerInstanceId ?? payload.workerInstanceId),
    workerMachineName: toReadableValue(raw?.workerMachineName ?? payload.workerMachineName),
    workerStartedAtUtc: toReadableValue(raw?.workerStartedAtUtc ?? payload.workerStartedAtUtc),
    requestId: toReadableValue(payload.requestId),
    proxyName: toReadableValue(
      proxy?.displayName
      ?? proxy?.name
      ?? account.proxyName
      ?? payload.proxyName,
    ),
  };
}

function resolveStatusView(statusValue: string, fallback: string) {
  const normalized = (statusValue || fallback).trim().toLowerCase();
  if (
    normalized.includes("error")
    || normalized.includes("fail")
    || normalized.includes("disabled")
    || normalized.includes("paused")
  ) {
    return { tone: "status--bad", key: "error" as const };
  }

  if (
    normalized.includes("warn")
    || normalized.includes("pending")
    || normalized.includes("sync")
    || normalized.includes("token")
  ) {
    return { tone: "status--warn", key: "warn" as const };
  }

  return { tone: "status--ok", key: "ok" as const };
}

export function ProjectAccountsPanel({
  apiSession,
  projectId,
  activeRole,
  canOpenIntegrationGrants = false,
}: ProjectAccountsPanelProps) {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [platformFilter, setPlatformFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState("all");
  const [feedback, setFeedback] = useState("");
  const { modal, accountId: modalAccountId, closeModal, openModal } = useRouteModal();

  const canManageLifecycle = hasPermission(activeRole, projectPermissions.accountsLifecycleManage);
  const canRevealProxy = hasPermission(activeRole, projectPermissions.proxyCredentialsReveal);
  const canUpdateProxy = hasPermission(activeRole, projectPermissions.proxyCredentialsUpdate);

  const {
    accounts,
    selectedAccountId,
    selectedAccount,
    isLoading,
    error,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const integrationsStatusQuery = useQuery({
    queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listProjectIntegrationsStatusRequest(apiSession, projectId),
    staleTime: 10_000,
  });

  const integrationRuntimeAccountIds = useMemo(() => {
    const ids = new Set<string>();
    for (const item of integrationsStatusQuery.data?.items ?? []) {
      if (item.runtimeAccountId?.trim()) {
        ids.add(item.runtimeAccountId.trim());
      }
    }
    return ids;
  }, [integrationsStatusQuery.data?.items]);

  const integrationPlatformHints = useMemo(() => {
    const hints = new Set<string>();

    for (const item of integrationsStatusQuery.data?.items ?? []) {
      if (item.integrationType !== "worker") {
        continue;
      }

      const parts = item.integrationKey
        .trim()
        .toLowerCase()
        .split(/[^a-z0-9]+/g)
        .filter((part) => part.length > 0);

      if (parts.length > 0) {
        hints.add(parts[0]);
      }
    }

    return hints;
  }, [integrationsStatusQuery.data?.items]);

  const visibleAccounts = useMemo(
    () =>
      accounts.filter((account) => {
        if (integrationRuntimeAccountIds.has(account.id)) {
          return false;
        }

        const normalizedPlatform = account.platform.trim().toLowerCase();
        if (!normalizedPlatform) {
          return true;
        }

        return !integrationPlatformHints.has(normalizedPlatform);
      }),
    [accounts, integrationPlatformHints, integrationRuntimeAccountIds],
  );

  const accountInfoQueries = useQueries({
    queries: visibleAccounts.map((account) => ({
      queryKey: ["account.info", apiSession.baseUrl, apiSession.token, projectId, account.id] as const,
      queryFn: () => runAccountActionRequest(apiSession, account.id, "account.info", {}),
      enabled: Boolean(account.id),
      refetchInterval: 30_000,
      staleTime: 10_000,
    })),
  });

  const accountInfoById = useMemo(() => {
    const map = new Map<string, NormalizedAccountInfo>();
    for (let index = 0; index < visibleAccounts.length; index += 1) {
      const account = visibleAccounts[index];
      const query = accountInfoQueries[index];
      if (query?.data && isRecord(query.data)) {
        map.set(account.id, normalizeAccountInfo(query.data));
      }
    }
    return map;
  }, [accountInfoQueries, visibleAccounts]);

  const accountInfoErrorById = useMemo(() => {
    const map = new Map<string, string>();
    for (let index = 0; index < visibleAccounts.length; index += 1) {
      const account = visibleAccounts[index];
      const query = accountInfoQueries[index];
      const message =
        query?.error instanceof Error
          ? query.error.message
          : query?.error
            ? "Не удалось получить account.info."
            : null;
      if (message) {
        map.set(account.id, message);
      }
    }
    return map;
  }, [accountInfoQueries, visibleAccounts]);

  const platforms = useMemo(
    () => Array.from(new Set(visibleAccounts.map((account) => account.platform))).sort(),
    [visibleAccounts],
  );

  const filteredAccounts = useMemo(() => {
    const query = search.trim().toLowerCase();
    return visibleAccounts.filter((account) => {
      const info = accountInfoById.get(account.id);
      const statusView = resolveStatusView(info?.status ?? "", account.businessStatus);
      const statusMatches = statusFilter === "all" || statusView.key === statusFilter;
      const platformMatches = platformFilter === "all" || account.platform === platformFilter;
      if (!statusMatches || !platformMatches) {
        return false;
      }

      const searchable = [
        account.displayName,
        account.platform,
        account.id,
        account.businessStatus,
        info?.status ?? "",
        info?.workerMachineName ?? "",
      ].join(" ").toLowerCase();

      return !query || searchable.includes(query);
    });
  }, [accountInfoById, platformFilter, search, statusFilter, visibleAccounts]);

  const selectedAccountResolved = useMemo(() => {
    if (selectedAccount && filteredAccounts.some((account) => account.id === selectedAccount.id)) {
      return selectedAccount;
    }
    if (!selectedAccountId) {
      return filteredAccounts[0] ?? null;
    }
    return filteredAccounts.find((account) => account.id === selectedAccountId) ?? filteredAccounts[0] ?? null;
  }, [filteredAccounts, selectedAccount, selectedAccountId]);

  const selectedId = selectedAccountResolved?.id ?? "";
  const selectedAccountInfo = selectedId ? accountInfoById.get(selectedId) ?? null : null;
  const selectedAccountError = selectedId ? accountInfoErrorById.get(selectedId) ?? null : null;

  const statusCounters = useMemo(() => {
    let ok = 0;
    let warn = 0;
    let bad = 0;

    for (const account of visibleAccounts) {
      const info = accountInfoById.get(account.id);
      const statusView = resolveStatusView(info?.status ?? "", account.businessStatus);
      if (statusView.key === "ok") {
        ok += 1;
      } else if (statusView.key === "warn") {
        warn += 1;
      } else {
        bad += 1;
      }
    }

    return { ok, warn, bad };
  }, [accountInfoById, visibleAccounts]);

  const anyAccountInfoFetching = accountInfoQueries.some((query) => query.isFetching);
  const modalManageAccountId = modalAccountId || selectedId;

  const refreshAccountInfo = async () => {
    await Promise.all(accountInfoQueries.map((query) => query.refetch()));
    setFeedback("Проверка аккаунтов запущена.");
  };

  const handleCreateCompleted = async () => {
    await queryClient.invalidateQueries({
      queryKey: ["accounts", apiSession.baseUrl, apiSession.token, projectId],
    });
    closeModal();
  };

  const handleManageChanged = async () => {
    await queryClient.invalidateQueries({
      queryKey: ["accounts", apiSession.baseUrl, apiSession.token, projectId],
    });
  };

  const handleManageDeleted = async () => {
    await queryClient.invalidateQueries({
      queryKey: ["accounts", apiSession.baseUrl, apiSession.token, projectId],
    });
    closeModal();
  };

  return (
    <div className="page-stack" data-testid="project-accounts-panel">
      <section className="page-head">
        <div className="page-head__row">
          <div>
            <h1 className="page-title">Аккаунты проекта</h1>
          </div>
          <div className="inline">
            <button
              type="button"
              className="button button-ghost button-small"
              onClick={refreshAccountInfo}
              disabled={anyAccountInfoFetching}
            >
              Проверить
            </button>
            {canManageLifecycle ? (
              <button
                type="button"
                className="button button-primary button-small"
                onClick={() => openModal("create")}
              >
                + Аккаунт
              </button>
            ) : null}
          </div>
        </div>
        {feedback ? <div className="status status--info">{feedback}</div> : null}
      </section>

      <section className="state-grid" aria-label="Состояния аккаунтов">
        <article className="state-card">
          <strong>{visibleAccounts.length}</strong>
          <span className="text-small text-muted">всего</span>
        </article>
        <article className="state-card">
          <strong>{statusCounters.ok}</strong>
          <span className="status status--ok">online</span>
        </article>
        <article className="state-card">
          <strong>{statusCounters.warn}</strong>
          <span className="status status--warn">нужна проверка</span>
        </article>
        <article className="state-card">
          <strong>{statusCounters.bad}</strong>
          <span className="status status--bad">ошибка</span>
        </article>
      </section>

      <section className="toolbar" aria-label="Фильтры аккаунтов">
        <input
          className="input"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Поиск: площадка, имя, worker"
          style={{ maxWidth: 320 }}
        />
        <select
          className="input"
          value={platformFilter}
          onChange={(event) => setPlatformFilter(event.target.value)}
          style={{ maxWidth: 180 }}
        >
          <option value="all">Все площадки</option>
          {platforms.map((platform) => (
            <option key={platform} value={platform}>{platform}</option>
          ))}
        </select>
        <select
          className="input"
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value)}
          style={{ maxWidth: 180 }}
        >
          <option value="all">Все статусы</option>
          <option value="ok">Online</option>
          <option value="warn">Warning</option>
          <option value="error">Error</option>
        </select>
      </section>

      <section className="accounts-layout">
        <div className="table-wrap">
          <table className="table w-full">
            <thead>
              <tr>
                <th>Аккаунт</th>
                <th>Статус</th>
                <th>Worker</th>
                <th>Данные</th>
                <th>Действия</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <tr>
                  <td colSpan={5}>Загружаем аккаунты...</td>
                </tr>
              ) : null}

              {!isLoading && filteredAccounts.length === 0 ? (
                <tr>
                  <td colSpan={5}>Аккаунты не найдены.</td>
                </tr>
              ) : null}

              {filteredAccounts.map((account) => {
                const info = accountInfoById.get(account.id);
                const infoError = accountInfoErrorById.get(account.id);
                const statusView = resolveStatusView(info?.status ?? "", account.businessStatus);
                const isSelected = account.id === selectedId;

                return (
                  <tr
                    key={account.id}
                    className={isSelected ? "is-selected" : ""}
                    data-selectable-row
                    onClick={() => setSelectedAccountId(account.id)}
                  >
                    <td>
                      <div className="account-line">
                        <strong>{info?.displayName || account.displayName}</strong>
                        <span className="badge">{info?.platform || account.platform}</span>
                      </div>
                      <div className="text-small text-muted">
                        {info?.nickname || "No nickname"}
                        {infoError ? ` · ${infoError}` : ""}
                      </div>
                    </td>
                    <td>
                      <span className={`status ${statusView.tone}`}>
                        {info?.status || account.businessStatus}
                      </span>
                    </td>
                    <td>{info?.workerMachineName || "worker: n/a"}</td>
                    <td>
                      {(info?.productsCount || "0")}
                      {" "}товаров ·
                      {" "}
                      {(info?.unreadCount || "0")}
                      {" "}unread
                    </td>
                    <td>
                      {canManageLifecycle ? (
                        <button
                          type="button"
                          className="button button-ghost button-small"
                          onClick={(event) => {
                            event.stopPropagation();
                            openModal("manage", { accountId: account.id });
                          }}
                        >
                          Edit
                        </button>
                      ) : (
                        <button
                          type="button"
                          className="button button-ghost button-small"
                          onClick={(event) => {
                            event.stopPropagation();
                            setSelectedAccountId(account.id);
                          }}
                        >
                          View
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        <aside className="card stack details-panel" aria-label="Детали выбранного аккаунта">
          <div className="card__head mb-0">
            <h2 className="card__title">Account details</h2>
            <span className={`status ${resolveStatusView(selectedAccountInfo?.status ?? "", selectedAccountResolved?.businessStatus ?? "").tone}`}>
              {selectedAccountInfo?.status || selectedAccountResolved?.businessStatus || "n/a"}
            </span>
          </div>

          {!selectedAccountResolved ? (
            <p className="text-small text-muted">Выберите аккаунт в таблице.</p>
          ) : (
            <>
              <div><div className="label">Name</div><strong>{selectedAccountInfo?.displayName || selectedAccountResolved.displayName}</strong></div>
              <div><div className="label">Platform</div><span className="badge">{selectedAccountInfo?.platform || selectedAccountResolved.platform}</span></div>
              <div><div className="label">Worker</div>{selectedAccountInfo?.workerMachineName || "n/a"}</div>
              <div><div className="label">Proxy</div>{selectedAccountInfo?.proxyName || "n/a"}</div>
              <div><div className="label">Last sync</div>{selectedAccountInfo?.workerStartedAtUtc || "n/a"}</div>
              <div><div className="label">Balance</div>{selectedAccountInfo?.balance || "n/a"}</div>
              {selectedAccountError ? <p className="route-error">{selectedAccountError}</p> : null}

              <div className="split">
                <Link className="button button-ghost button-small" href={`/projects/${projectId}/products`}>
                  Товары
                </Link>
                <Link className="button button-ghost button-small" href={`/projects/${projectId}/messages`}>
                  Сообщения
                </Link>
              </div>

              {canManageLifecycle ? (
                <button
                  type="button"
                  className="button button-primary button-small"
                  onClick={() => openModal("manage", { accountId: selectedAccountResolved.id })}
                >
                  Управлять
                </button>
              ) : null}
            </>
          )}
        </aside>
      </section>

      {error ? <p className="route-error">{error.message}</p> : null}

      {!isLoading && visibleAccounts.length === 0 ? (
        <section className="empty-state" aria-label="Пустое состояние">
          В проекте нет аккаунтов площадок. Steam перенесён в раздел интеграций.
        </section>
      ) : null}

      {!isLoading && visibleAccounts.length > 0 && filteredAccounts.length === 0 ? (
        <section className="empty-state" aria-label="Пустое состояние">
          Аккаунты не найдены по текущему фильтру.
        </section>
      ) : null}

      {!canRevealProxy || !canUpdateProxy ? (
        <p className="route-hint">
          Proxy reveal/update доступны только ролям owner/admin.
        </p>
      ) : null}

      <RouteModalHost
        isOpen={modal === "create"}
        title="Добавить аккаунт"
        description="Выберите тип аккаунта из каталога Accounts Manager и заполните форму подключения."
        onClose={closeModal}
      >
        <ProjectAccountCreatePanel
          apiSession={apiSession}
          projectId={projectId}
          activeRole={activeRole}
          canOpenIntegrationGrants={canOpenIntegrationGrants}
          mode="modal"
          onCancel={closeModal}
          onCompleted={handleCreateCompleted}
        />
      </RouteModalHost>

      <RouteModalHost
        isOpen={modal === "manage"}
        title="Управление аккаунтом"
        description="Изменение параметров, proxy credentials и lifecycle."
        onClose={closeModal}
      >
        <ProjectAccountManagePanel
          apiSession={apiSession}
          projectId={projectId}
          accountId={modalManageAccountId}
          activeRole={activeRole}
          mode="modal"
          onCancel={closeModal}
          onUpdated={handleManageChanged}
          onDeleted={handleManageDeleted}
        />
      </RouteModalHost>
    </div>
  );
}
