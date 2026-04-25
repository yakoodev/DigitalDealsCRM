"use client";

import { useQueries } from "@tanstack/react-query";
import Link from "next/link";
import { useMemo } from "react";
import { AccountSelector } from "@/components/account-selector";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import { toReadableValue } from "@/lib/worker-result";

interface ProjectAccountsPanelProps {
  apiSession: ApiSession;
  projectId: string;
}

export function ProjectAccountsPanel({
  apiSession,
  projectId,
}: ProjectAccountsPanelProps) {
  const status = "Загрузите или создайте аккаунт проекта.";

  const {
    accounts,
    selectedAccountId,
    selectedAccount,
    isLoading,
    error,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const accountInfoQueries = useQueries({
    queries: accounts.map((account) => ({
      queryKey: [
        "account.info",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        account.id,
      ] as const,
      queryFn: () =>
        runAccountActionRequest(apiSession, account.id, "account.info", {}),
      enabled: Boolean(account.id),
      refetchInterval: 30_000,
      staleTime: 10_000,
    })),
  });

  const accountInfoById = useMemo(() => {
    const map = new Map<string, Record<string, unknown>>();
    for (let index = 0; index < accounts.length; index += 1) {
      const account = accounts[index];
      const query = accountInfoQueries[index];
      if (query?.data && typeof query.data === "object") {
        map.set(account.id, query.data as Record<string, unknown>);
      }
    }

    return map;
  }, [accountInfoQueries, accounts]);

  const accountInfoErrorById = useMemo(() => {
    const map = new Map<string, string>();
    for (let index = 0; index < accounts.length; index += 1) {
      const account = accounts[index];
      const query = accountInfoQueries[index];
      const message =
        query?.error instanceof Error
          ? query.error.message
          : query?.error
            ? "Не удалось загрузить account.info."
            : null;
      if (message) {
        map.set(account.id, message);
      }
    }

    return map;
  }, [accountInfoQueries, accounts]);

  const selectedAccountInfo = selectedAccountId
    ? accountInfoById.get(selectedAccountId) ?? null
    : null;
  const selectedAccountInfoError = selectedAccountId
    ? accountInfoErrorById.get(selectedAccountId) ?? null
    : null;

  const anyAccountInfoFetching = accountInfoQueries.some((query) => query.isFetching);
  const anyAccountInfoPending = accountInfoQueries.some((query) => query.isPending);

  const activeAccountsCount = accounts.filter(
    (account) => account.businessStatus === "active",
  ).length;

  const topPlatforms = useMemo(() => {
    const counters = new Map<string, number>();
    for (const account of accounts) {
      const key = account.platform.trim().toLowerCase() || "unknown";
      counters.set(key, (counters.get(key) ?? 0) + 1);
    }

    return [...counters.entries()]
      .sort((left, right) => right[1] - left[1])
      .slice(0, 4);
  }, [accounts]);

  const refreshAccountInfo = async () => {
    await Promise.all(accountInfoQueries.map((query) => query.refetch()));
  };

  return (
    <div className="page-stack" data-testid="project-accounts-panel">
      <header className="page-section-header">
        <h2>Аккаунты проекта</h2>
        <p>
          В этой вкладке подключаются площадки проекта: создаём аккаунт, проверяем
          статус и фиксируем прокси-конфигурацию.
        </p>
      </header>

      <section className="summary-grid">
        <article className="summary-card">
          <p>Всего аккаунтов</p>
          <strong>{accounts.length}</strong>
          <small>Подключено к текущему проекту</small>
        </article>
        <article className="summary-card">
          <p>Активные</p>
          <strong>{activeAccountsCount}</strong>
          <small>Готовы к операциям воркера</small>
        </article>
        <article className="summary-card">
          <p>Площадки</p>
          <strong>{topPlatforms.length}</strong>
          <small>
            {topPlatforms.length === 0
              ? "Нет данных"
              : topPlatforms.map(([platform, count]) => `${platform}: ${count}`).join(" · ")}
          </small>
        </article>
      </section>

      <div className="split-grid">
        <section className="panel-card">
          <h3>Доступные аккаунты</h3>
          {error ? <p className="route-error">{error.message}</p> : null}
          {isLoading ? <p className="route-hint">Загружаем аккаунты...</p> : null}
          {!isLoading && accounts.length === 0 ? (
            <p className="route-hint">
              Аккаунтов пока нет. Откройте отдельную страницу «Добавить аккаунт».
            </p>
          ) : null}

          <AccountSelector
            accounts={accounts}
            selectedAccountId={selectedAccountId}
            onChange={setSelectedAccountId}
            isLoading={isLoading}
          />

          {selectedAccount ? (
            <dl className="kv-list">
              <div>
                <dt>ID</dt>
                <dd>{selectedAccount.id}</dd>
              </div>
              <div>
                <dt>Platform</dt>
                <dd>{selectedAccount.platform}</dd>
              </div>
              <div>
                <dt>Status</dt>
                <dd>{selectedAccount.businessStatus}</dd>
              </div>
              <div>
                <dt>Display Name</dt>
                <dd>{selectedAccount.displayName}</dd>
              </div>
            </dl>
          ) : null}

          {selectedAccountId ? (
            <section className="panel-card panel-soft">
              <div className="panel-title-row">
                <h3>Состояние выбранного worker account</h3>
                <button
                  type="button"
                  className="button button-ghost"
                  onClick={refreshAccountInfo}
                  disabled={anyAccountInfoFetching}
                >
                  Обновить
                </button>
              </div>
              {anyAccountInfoPending ? (
                <p className="route-hint">Проверяем account.info...</p>
              ) : selectedAccountInfoError ? (
                <p className="route-error">{selectedAccountInfoError}</p>
              ) : !selectedAccountInfo ? (
                <p className="route-error">Не удалось получить account.info.</p>
              ) : (
                <dl className="kv-list">
                  {Object.entries(selectedAccountInfo).map(([key, value]) => (
                    <div key={key}>
                      <dt>{key}</dt>
                      <dd>{toReadableValue(value) || "n/a"}</dd>
                    </div>
                  ))}
                </dl>
              )}
            </section>
          ) : null}

          {!isLoading && accounts.length > 0 ? (
            <section className="panel-card panel-soft">
              <div className="panel-title-row">
                <h3>Состояние всех worker account</h3>
                <button
                  type="button"
                  className="button button-ghost"
                  onClick={refreshAccountInfo}
                  disabled={anyAccountInfoFetching}
                >
                  Обновить
                </button>
              </div>
              <ul className="entity-list compact-list">
                {accounts.map((account) => {
                  const accountInfo = accountInfoById.get(account.id);
                  const accountInfoError = accountInfoErrorById.get(account.id);
                  const infoStatus = toReadableValue(
                    accountInfo?.status ?? account.businessStatus,
                  ) || "n/a";
                  const workerInstanceId = toReadableValue(
                    (accountInfo?.raw as Record<string, unknown> | undefined)
                      ?.workerInstanceId,
                  );

                  return (
                    <li key={`worker-info-${account.id}`} className="entity-list-item">
                      <div>
                        <strong>{account.displayName}</strong>
                        <p>{account.platform}</p>
                        <small>Status: {infoStatus}</small>
                        <small>
                          Worker: {workerInstanceId || "ожидаем account.info"}
                        </small>
                        {accountInfoError ? (
                          <small className="route-error">{accountInfoError}</small>
                        ) : null}
                      </div>
                      <button
                        type="button"
                        className="button button-ghost"
                        onClick={() => setSelectedAccountId(account.id)}
                      >
                        Открыть
                      </button>
                    </li>
                  );
                })}
              </ul>
            </section>
          ) : null}

          {!isLoading && accounts.length > 0 ? (
            <ul className="entity-list compact-list">
              {accounts.map((account) => (
                <li key={account.id}>
                  <button
                    type="button"
                    className={`list-select ${selectedAccountId === account.id ? "is-active" : ""}`}
                    onClick={() => setSelectedAccountId(account.id)}
                  >
                    <strong>{account.displayName}</strong>
                    <small>{account.platform}</small>
                    <p>{account.businessStatus}</p>
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </section>

        <section className="panel-card">
          <h3>Добавление аккаунта</h3>
          <p className="route-hint">
            Создание вынесено в отдельный route: сначала выбираем тип аккаунта из
            каталога Accounts Manager, затем заполняем форму.
          </p>
          <div className="panel-actions">
            <Link
              href={`/projects/${projectId}/accounts/new`}
              className="button button-primary"
            >
              Добавить аккаунт
            </Link>
          </div>
          <p className="route-hint">{status}</p>
        </section>
      </div>
    </div>
  );
}
