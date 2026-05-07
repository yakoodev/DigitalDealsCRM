"use client";

import { useQueries, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useMemo } from "react";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import {
  listProjectIntegrationsStatusRequest,
  runAccountActionRequest,
  type ApiSession,
} from "@/lib/api-client";
import { extractObjectRows, isRecord } from "@/lib/worker-result";

interface ProjectOverviewPanelProps {
  apiSession: ApiSession;
  projectId: string;
}

interface AccountWorkerSnapshot {
  accountId: string;
  accountName: string;
  platform: string;
  productsCount: number;
  conversationsCount: number;
  unreadCount: number;
}

interface WorkerWarning {
  accountId: string;
  accountName: string;
  module: "products" | "messages";
  message: string;
}

function readUnreadCount(conversation: Record<string, unknown>) {
  const raw = conversation.unreadCount ?? conversation.unread ?? conversation.newMessages;
  if (typeof raw === "number" && Number.isFinite(raw)) {
    return raw;
  }

  if (typeof raw === "string") {
    const parsed = Number(raw);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
  }

  return 0;
}

export function ProjectOverviewPanel({ apiSession, projectId }: ProjectOverviewPanelProps) {
  const { accounts, isLoading, error } = useProjectAccounts(apiSession, projectId);

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

  const activeCount = visibleAccounts.filter((account) => account.businessStatus === "active").length;
  const proxyConfiguredCount = visibleAccounts.filter((account) => account.proxyConfigured).length;

  const platformStats = useMemo(() => {
    const map = new Map<string, number>();
    for (const account of visibleAccounts) {
      const key = account.platform.toLowerCase();
      map.set(key, (map.get(key) ?? 0) + 1);
    }

    return [...map.entries()]
      .map(([platform, count]) => ({ platform, count }))
      .sort((left, right) => right.count - left.count || left.platform.localeCompare(right.platform));
  }, [visibleAccounts]);

  const recentAccounts = visibleAccounts.slice(0, 6);
  const productsQueries = useQueries({
    queries: visibleAccounts.map((account) => ({
      queryKey: [
        "overview-products.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        account.id,
      ] as const,
      queryFn: () =>
        runAccountActionRequest(apiSession, account.id, "products.list", {
          limit: 100,
        }),
      enabled: Boolean(account.id),
      staleTime: 12_000,
      refetchInterval: 30_000,
    })),
  });

  const conversationsQueries = useQueries({
    queries: visibleAccounts.map((account) => ({
      queryKey: [
        "overview-conversations.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        account.id,
      ] as const,
      queryFn: () =>
        runAccountActionRequest(apiSession, account.id, "conversations.list", {
          limit: 100,
        }),
      enabled: Boolean(account.id),
      staleTime: 12_000,
      refetchInterval: 30_000,
    })),
  });

  const workerSnapshots = useMemo<AccountWorkerSnapshot[]>(() => {
    return visibleAccounts.map((account, index) => {
      const productPayload = productsQueries[index]?.data;
      const conversationPayload = conversationsQueries[index]?.data;

      const productRows = isRecord(productPayload)
        ? extractObjectRows(productPayload, ["items", "products", "listings"])
        : [];
      const conversationRows = isRecord(conversationPayload)
        ? extractObjectRows(conversationPayload, ["items", "conversations", "messages"])
        : [];
      const unreadCount = conversationRows.reduce(
        (sum, conversation) => sum + readUnreadCount(conversation),
        0,
      );

      return {
        accountId: account.id,
        accountName: account.displayName,
        platform: account.platform,
        productsCount: productRows.length,
        conversationsCount: conversationRows.length,
        unreadCount,
      };
    });
  }, [visibleAccounts, conversationsQueries, productsQueries]);

  const workerWarnings = useMemo<WorkerWarning[]>(() => {
    return visibleAccounts.flatMap((account, index) => {
      const nextWarnings: WorkerWarning[] = [];

      const productsError = productsQueries[index]?.error;
      if (productsError) {
        nextWarnings.push({
          accountId: account.id,
          accountName: account.displayName,
          module: "products",
          message:
            productsError instanceof Error
              ? productsError.message
              : "Не удалось получить products.list.",
        });
      }

      const conversationsError = conversationsQueries[index]?.error;
      if (conversationsError) {
        nextWarnings.push({
          accountId: account.id,
          accountName: account.displayName,
          module: "messages",
          message:
            conversationsError instanceof Error
              ? conversationsError.message
              : "Не удалось получить conversations.list.",
        });
      }

      return nextWarnings;
    });
  }, [visibleAccounts, conversationsQueries, productsQueries]);

  const degradedAccountIds = new Set(workerWarnings.map((warning) => warning.accountId));
  const degradedWorkersCount = degradedAccountIds.size;
  const healthyWorkersCount = Math.max(0, visibleAccounts.length - degradedWorkersCount);

  const productsTotal = workerSnapshots.reduce((sum, entry) => sum + entry.productsCount, 0);
  const conversationsTotal = workerSnapshots.reduce(
    (sum, entry) => sum + entry.conversationsCount,
    0,
  );
  const unreadTotal = workerSnapshots.reduce((sum, entry) => sum + entry.unreadCount, 0);
  const workerDataLoading =
    productsQueries.some((query) => query.isPending) || conversationsQueries.some((query) => query.isPending);
  const workerDataFetching =
    productsQueries.some((query) => query.isFetching) || conversationsQueries.some((query) => query.isFetching);

  const refreshWorkerData = async () => {
    await Promise.all([
      ...productsQueries.map((query) => query.refetch()),
      ...conversationsQueries.map((query) => query.refetch()),
    ]);
  };

  return (
    <div className="page-stack" data-testid="project-overview-panel">
      <section className="page-head">
        <div className="page-head__row">
          <div>
            <h1 className="page-title">Обзор проекта</h1>
          </div>
          <div className="inline">
            <button
              type="button"
              className="button button-ghost button-small"
              onClick={refreshWorkerData}
              disabled={workerDataFetching || visibleAccounts.length === 0}
            >
              Обновить данные
            </button>
            <Link className="button button-primary button-small" href={`/projects/${projectId}/accounts?modal=create`}>
              ＋ Аккаунт
            </Link>
          </div>
        </div>
      </section>

      <section aria-label="Сводка проекта" className="card workspace-card mb-4">
        <div className="project-summary">
          <div>
            <div className="inline mb-2">
              {platformStats.slice(0, 4).map((entry) => (
                <span key={`badge-${entry.platform}`} className="badge">
                  {entry.platform}
                </span>
              ))}
              <span className={`status ${workerWarnings.length === 0 ? "status--ok" : "status--warn"}`}>
                {workerWarnings.length === 0 ? "Все сервисы online" : "Есть предупреждения"}
              </span>
            </div>
            <div className="project-meta">
              <span className="chip">Команда: {Math.max(1, visibleAccounts.length)}</span>
              <span className="chip">Воркеры: {healthyWorkersCount}</span>
              <span className="chip">{workerDataFetching ? "Синхронизация..." : "Данные обновлены"}</span>
            </div>
          </div>
          <div className="stack">
            <div>
              <div className="label">Project ID</div>
              <div className="project-id">{projectId}</div>
            </div>
            <div className="inline">
              <Link className="button button-ghost button-small" href={`/projects/${projectId}/accounts`}>
                Открыть аккаунты
              </Link>
              <Link className="button button-ghost button-small" href={`/projects/${projectId}/messages`}>
                Сообщения
              </Link>
            </div>
          </div>
        </div>
      </section>

      <section aria-label="Ключевые метрики" className="compact-kpi-grid project-overview-kpi-grid">
        <article className="compact-kpi">
          <div className="compact-kpi__icon">◉</div>
          <div className="compact-kpi__value">{visibleAccounts.length}</div>
          <div className="compact-kpi__label">аккаунтов</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__icon">▣</div>
          <div className="compact-kpi__value">{productsTotal}</div>
          <div className="compact-kpi__label">товаров</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__icon">◇</div>
          <div className="compact-kpi__value">{workerSnapshots.length}</div>
          <div className="compact-kpi__label">воркер-срезов</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__icon">✉</div>
          <div className="compact-kpi__value">{conversationsTotal}</div>
          <div className="compact-kpi__label">сообщения</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__icon">!</div>
          <div className="compact-kpi__value">{unreadTotal}</div>
          <div className="compact-kpi__label">непрочитано</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__icon">⚙</div>
          <div className="compact-kpi__value">{healthyWorkersCount}</div>
          <div className="compact-kpi__label">healthy workers</div>
        </article>
      </section>

      <section className="layout-grid">
        <div className="col-12 stack">
          <article className="card">
            <div className="card__head">
              <div>
                <h2 className="card__title">Аккаунты</h2>
                <div className="card__meta">Краткий статус. Полный список — на отдельной странице.</div>
              </div>
              <Link className="button button-ghost button-small" href={`/projects/${projectId}/accounts`}>
                Открыть
              </Link>
            </div>
            <div className="mini-stat-list">
              <div className="mini-stat"><span>Online</span><strong>{activeCount}</strong></div>
              <div className="mini-stat"><span>Warning</span><strong>{workerWarnings.length}</strong></div>
              <div className="mini-stat"><span>Error</span><strong>{degradedWorkersCount}</strong></div>
              <div className="mini-stat"><span>Proxy configured</span><strong>{proxyConfiguredCount}</strong></div>
            </div>
          </article>

          <article className="card">
            <div className="card__head">
              <div>
                <h2 className="card__title">Срез по воркерам</h2>
                <div className="card__meta">Текущая активность по товарам и перепискам.</div>
              </div>
              <Link className="button button-ghost button-small" href={`/projects/${projectId}/messages`}>
                Сообщения
              </Link>
            </div>

            {error ? <p className="route-error">{error.message}</p> : null}
            {isLoading ? <p className="route-hint">Собираем статистику проекта...</p> : null}
            {!isLoading && visibleAccounts.length > 0 && workerDataLoading ? (
              <p className="route-hint">Подтягиваем данные с воркеров (товары + переписки)...</p>
            ) : null}
            {!isLoading && visibleAccounts.length > 0 && workerWarnings.length > 0 ? (
              <section className="status-block status-warning">
                <h4>Часть воркеров ответила с ошибкой</h4>
                <ul className="entity-list compact-list">
                  {workerWarnings.map((warning) => (
                    <li key={`${warning.accountId}-${warning.module}`} className="entity-list-item">
                      <div>
                        <strong>
                          {warning.accountName} · {warning.module}
                        </strong>
                        <p className="route-error">{warning.message}</p>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}

            {!isLoading && !error && visibleAccounts.length === 0 ? (
              <p className="route-hint">
                В проекте пока нет аккаунтов. Добавьте первый аккаунт в модуле `Accounts`.
              </p>
            ) : null}

            {!isLoading && !error && workerSnapshots.length > 0 ? (
              <div className="item-list">
                {workerSnapshots.map((snapshot) => (
                  <article key={snapshot.accountId} className="item-row">
                    <div className="item-row__main">
                      <div className="item-row__head">
                        <strong>{snapshot.accountName}</strong>
                        <span className="badge">{snapshot.platform}</span>
                      </div>
                      <div className="item-row__stats">
                        <span className="chip">Товары: {snapshot.productsCount}</span>
                        <span className="chip">Переписки: {snapshot.conversationsCount}</span>
                        <span className="chip">Непрочитанные: {snapshot.unreadCount}</span>
                      </div>
                    </div>
                    <div className="inline-actions">
                      <Link
                        className="button button-ghost button-small"
                        href={`/projects/${projectId}/accounts?modal=manage&accountId=${snapshot.accountId}`}
                      >
                        Управлять
                      </Link>
                    </div>
                  </article>
                ))}
              </div>
            ) : null}

            {!isLoading && !error && recentAccounts.length > 0 ? (
              <div className="hint-list">
                {recentAccounts.map((account) => (
                  <div key={account.id} className="hint-item">
                    <div className="hint-row">
                      <strong>{account.displayName}</strong>
                      <span className="chip">{account.businessStatus}</span>
                    </div>
                    <div className="list-note">{account.id}</div>
                  </div>
                ))}
              </div>
            ) : null}
          </article>

          <article className="card">
            <div className="card__head">
              <div>
                <h2 className="card__title">Последняя активность</h2>
                <div className="card__meta">События и быстрые переходы по модулям проекта.</div>
              </div>
              <div className="inline">
                <Link className="button button-ghost button-small" href={`/projects/${projectId}/offers`}>
                  Офферы
                </Link>
                <Link className="button button-ghost button-small" href={`/projects/${projectId}/products`}>
                  Товары
                </Link>
              </div>
            </div>

            {workerWarnings.length > 0 ? (
              <div className="activity-list">
                {workerWarnings.slice(0, 4).map((warning) => (
                  <div key={`activity-warning-${warning.accountId}-${warning.module}`} className="activity-item">
                    <div className="activity-row">
                      <strong>{warning.accountName}</strong>
                      <span className="status status--warn">{warning.module}</span>
                    </div>
                    <div className="list-note">{warning.message}</div>
                  </div>
                ))}
              </div>
            ) : null}

            {workerWarnings.length === 0 && workerSnapshots.length > 0 ? (
              <div className="activity-list">
                {workerSnapshots.slice(0, 4).map((snapshot) => (
                  <div key={`activity-snapshot-${snapshot.accountId}`} className="activity-item">
                    <div className="activity-row">
                      <strong>{snapshot.accountName}</strong>
                      <span className="status status--ok">online</span>
                    </div>
                    <div className="list-note">
                      Товары: {snapshot.productsCount} · Переписки: {snapshot.conversationsCount} · Непрочитанные: {snapshot.unreadCount}
                    </div>
                  </div>
                ))}
              </div>
            ) : null}

            {!isLoading && visibleAccounts.length === 0 ? (
              <p className="route-hint">
                Пока нет событий: добавьте первый аккаунт и откройте модуль Offers/Flow для запуска сценариев.
              </p>
            ) : null}
          </article>
        </div>
      </section>
    </div>
  );
}
