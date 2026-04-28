"use client";

import { useQueries } from "@tanstack/react-query";
import Link from "next/link";
import { useMemo } from "react";
import { ModulePageShell } from "@/components/layout/module-page-shell";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import { runAccountActionRequest, type ApiSession } from "@/lib/api-client";
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

  const activeCount = accounts.filter((account) => account.businessStatus === "active").length;
  const pausedCount = accounts.length - activeCount;
  const proxyConfiguredCount = accounts.filter((account) => account.proxyConfigured).length;
  const workerReadyCount = accounts.filter((account) => account.businessStatus === "active").length;

  const platformStats = useMemo(() => {
    const map = new Map<string, number>();
    for (const account of accounts) {
      const key = account.platform.toLowerCase();
      map.set(key, (map.get(key) ?? 0) + 1);
    }

    return [...map.entries()]
      .map(([platform, count]) => ({ platform, count }))
      .sort((left, right) => right.count - left.count || left.platform.localeCompare(right.platform));
  }, [accounts]);

  const recentAccounts = accounts.slice(0, 6);
  const productsQueries = useQueries({
    queries: accounts.map((account) => ({
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
    queries: accounts.map((account) => ({
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
    return accounts.map((account, index) => {
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
  }, [accounts, conversationsQueries, productsQueries]);

  const workerWarnings = useMemo<WorkerWarning[]>(() => {
    return accounts.flatMap((account, index) => {
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
  }, [accounts, conversationsQueries, productsQueries]);

  const degradedAccountIds = new Set(workerWarnings.map((warning) => warning.accountId));
  const degradedWorkersCount = degradedAccountIds.size;
  const healthyWorkersCount = Math.max(0, accounts.length - degradedWorkersCount);

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
      <ModulePageShell
        title="Обзор проекта / Overview"
        description="Краткая статистика проекта, быстрые действия и переходы по рабочим модулям."
        actions={(
          <div className="panel-actions">
            <button
              type="button"
              className="button button-ghost"
              onClick={refreshWorkerData}
              disabled={workerDataFetching || accounts.length === 0}
            >
              Обновить данные
            </button>
            <Link className="button button-primary" href={`/projects/${projectId}/accounts`}>
              Открыть аккаунты
            </Link>
            <Link className="button button-ghost" href={`/projects/${projectId}/products`}>
              Открыть товары
            </Link>
            <Link className="button button-ghost" href={`/projects/${projectId}/messages`}>
              Открыть сообщения
            </Link>
          </div>
        )}
        stats={[
          { label: "Аккаунтов", value: String(accounts.length), hint: "Всего в проекте" },
          { label: "Товаров", value: String(productsTotal), hint: "Агрегировано по воркерам" },
          { label: "Переписок", value: String(conversationsTotal), hint: "Список диалогов" },
          { label: "Непрочитанные", value: String(unreadTotal), hint: "Требуют реакции" },
        ]}
        main={(
          <section className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Сводка проекта</h3>
              <span className="pill">{activeCount} active</span>
            </div>

            {error ? <p className="route-error">{error.message}</p> : null}
            {isLoading ? <p className="route-hint">Собираем статистику проекта...</p> : null}
            {!isLoading && accounts.length > 0 && workerDataLoading ? (
              <p className="route-hint">Подтягиваем данные с воркеров (товары + переписки)...</p>
            ) : null}
            {!isLoading && accounts.length > 0 && workerWarnings.length > 0 ? (
              <section className="status-block status-warning">
                <h4>Часть воркеров ответила с ошибкой</h4>
                <ul className="entity-list compact-list">
                  {workerWarnings.map((warning) => (
                    <li
                      key={`${warning.accountId}-${warning.module}`}
                      className="entity-list-item"
                    >
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

            {!isLoading && !error && accounts.length === 0 ? (
              <p className="route-hint">
                В проекте пока нет аккаунтов. Добавьте первый аккаунт в модуле `Accounts`.
              </p>
            ) : null}

            {!isLoading && !error && platformStats.length > 0 ? (
              <>
                <h4>Платформы</h4>
                <ul className="entity-list compact-list">
                  {platformStats.map((entry) => (
                    <li key={`platform-${entry.platform}`} className="entity-list-item">
                      <div>
                        <strong>{entry.platform}</strong>
                        <p>{entry.count} аккаунт(ов)</p>
                      </div>
                    </li>
                  ))}
                </ul>
              </>
            ) : null}

            {!isLoading && !error && workerSnapshots.length > 0 ? (
              <>
                <h4>Срез по воркерам</h4>
                <ul className="entity-list">
                  {workerSnapshots.map((snapshot) => (
                    <li key={snapshot.accountId} className="entity-list-item">
                      <div>
                        <strong>{snapshot.accountName}</strong>
                        <div className="entity-pills">
                          <span className="entity-pill">{snapshot.platform}</span>
                          <span className="entity-pill">Товары: {snapshot.productsCount}</span>
                          <span className="entity-pill">Переписки: {snapshot.conversationsCount}</span>
                          <span className="entity-pill">
                            Непрочитанные: {snapshot.unreadCount}
                          </span>
                        </div>
                      </div>
                      <div className="inline-actions">
                        <Link
                          className="button button-ghost"
                          href={`/projects/${projectId}/accounts?modal=manage&accountId=${snapshot.accountId}`}
                        >
                          Управлять
                        </Link>
                      </div>
                    </li>
                  ))}
                </ul>
              </>
            ) : null}

            {!isLoading && !error && recentAccounts.length > 0 ? (
              <>
                <h4>Последние аккаунты</h4>
                <ul className="entity-list compact-list">
                  {recentAccounts.map((account) => (
                    <li key={account.id} className="entity-list-item">
                      <div>
                        <strong>{account.displayName}</strong>
                        <div className="entity-pills">
                          <span className="entity-pill">{account.platform}</span>
                          <span className="entity-pill">{account.businessStatus}</span>
                          <span className="entity-pill">{account.id}</span>
                        </div>
                      </div>
                    </li>
                  ))}
                </ul>
              </>
            ) : null}
          </section>
        )}
        side={(
          <section className="glass-card page-stack">
            <h3>Быстрые действия</h3>
            <p className="route-hint">
              Боковые модули открываются без потери контекста проекта.
            </p>

            <div className="panel-actions">
              <Link className="button button-primary" href={`/projects/${projectId}/accounts?modal=create`}>
                Добавить аккаунт
              </Link>
              <Link className="button button-ghost" href={`/projects/${projectId}/products`}>
                К товарам
              </Link>
              <Link className="button button-ghost" href={`/projects/${projectId}/messages`}>
                К сообщениям
              </Link>
            </div>

            <section className="status-block">
              <h4>Операционный статус</h4>
              <dl className="kv-list">
                <div>
                  <dt>Project ID</dt>
                  <dd>{projectId}</dd>
                </div>
                <div>
                  <dt>Активные аккаунты</dt>
                  <dd>{activeCount}</dd>
                </div>
                <div>
                  <dt>Неактивные аккаунты</dt>
                  <dd>{pausedCount}</dd>
                </div>
                <div>
                  <dt>Worker ready</dt>
                  <dd>{workerReadyCount}</dd>
                </div>
                <div>
                  <dt>Healthy workers</dt>
                  <dd>{healthyWorkersCount}</dd>
                </div>
                <div>
                  <dt>Degraded workers</dt>
                  <dd>{degradedWorkersCount}</dd>
                </div>
                <div>
                  <dt>Proxy configured</dt>
                  <dd>{proxyConfiguredCount}</dd>
                </div>
              </dl>
            </section>
          </section>
        )}
      />
    </div>
  );
}
