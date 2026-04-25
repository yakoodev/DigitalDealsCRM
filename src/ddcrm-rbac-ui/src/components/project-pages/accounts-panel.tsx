"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AccountSelector } from "@/components/account-selector";
import type { ApiSession } from "@/lib/api-client";
import { createAccountRequest } from "@/lib/api-client";
import { useProjectAccounts } from "@/hooks/use-project-accounts";

interface ProjectAccountsPanelProps {
  apiSession: ApiSession;
  projectId: string;
}

export function ProjectAccountsPanel({
  apiSession,
  projectId,
}: ProjectAccountsPanelProps) {
  const queryClient = useQueryClient();
  const [platform, setPlatform] = useState("funpay");
  const [displayName, setDisplayName] = useState("Новый marketplace аккаунт");
  const [proxyHost, setProxyHost] = useState("");
  const [proxyPort, setProxyPort] = useState("1508");
  const [proxyLogin, setProxyLogin] = useState("");
  const [proxyPassword, setProxyPassword] = useState("");
  const [status, setStatus] = useState("Загрузите или создайте аккаунт проекта.");

  const {
    accounts,
    selectedAccountId,
    selectedAccount,
    isLoading,
    error,
    queryKey,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const createAccountMutation = useMutation({
    mutationFn: async () => {
      const parsedPort = Number(proxyPort);
      if (!Number.isInteger(parsedPort) || parsedPort < 1 || parsedPort > 65535) {
        throw new Error("Proxy port должен быть целым числом от 1 до 65535.");
      }

      if (!platform.trim() || !displayName.trim()) {
        throw new Error("Platform и Display Name обязательны.");
      }

      if (!proxyHost.trim() || !proxyLogin.trim() || !proxyPassword.trim()) {
        throw new Error("Заполните все поля proxy.");
      }

      return createAccountRequest(apiSession, projectId, {
        platform: platform.trim(),
        displayName: displayName.trim(),
        proxyConfig: {
          host: proxyHost.trim(),
          port: parsedPort,
          login: proxyLogin.trim(),
          password: proxyPassword,
        },
      });
    },
    onSuccess: async (account) => {
      await queryClient.invalidateQueries({ queryKey });
      setSelectedAccountId(account.id);
      setStatus("Аккаунт создан и добавлен в проект.");
    },
    onError: (mutationError) => {
      setStatus(
        mutationError instanceof Error
          ? mutationError.message
          : "Не удалось создать аккаунт.",
      );
    },
  });

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
            <p className="route-hint">Аккаунтов пока нет. Добавьте первый аккаунт справа.</p>
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
          <h3>Добавить аккаунт</h3>
          <div className="stacked-block">
            <label className="field">
              <span>Platform</span>
              <input
                className="input"
                value={platform}
                onChange={(event) => setPlatform(event.target.value)}
                placeholder="funpay/playerok/ggsell/platimarket"
              />
            </label>

            <label className="field">
              <span>Display Name</span>
              <input
                className="input"
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
                placeholder="Название аккаунта"
              />
            </label>

            <div className="grid-4">
              <input
                className="input"
                value={proxyHost}
                onChange={(event) => setProxyHost(event.target.value)}
                placeholder="proxy host"
              />
              <input
                className="input"
                value={proxyPort}
                onChange={(event) => setProxyPort(event.target.value)}
                placeholder="port"
              />
              <input
                className="input"
                value={proxyLogin}
                onChange={(event) => setProxyLogin(event.target.value)}
                placeholder="login"
              />
              <input
                className="input"
                type="password"
                value={proxyPassword}
                onChange={(event) => setProxyPassword(event.target.value)}
                placeholder="password"
              />
            </div>

            <button
              type="button"
              className="button button-primary"
              disabled={createAccountMutation.isPending}
              onClick={() => createAccountMutation.mutate()}
            >
              Добавить аккаунт
            </button>
            <p className="route-hint">{status}</p>
          </div>
        </section>
      </div>
    </div>
  );
}
