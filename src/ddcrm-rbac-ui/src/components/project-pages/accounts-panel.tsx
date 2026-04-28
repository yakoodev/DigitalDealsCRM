"use client";

import { useQueries, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AccountSelector } from "@/components/account-selector";
import { ModulePageShell } from "@/components/layout/module-page-shell";
import { RouteModalHost } from "@/components/layout/route-modal-host";
import { ProjectAccountCreatePanel } from "@/components/project-pages/account-create-panel";
import { ProjectAccountManagePanel } from "@/components/project-pages/account-manage-panel";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import { useRouteModal } from "@/hooks/use-route-modal";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import { hasPermission, projectPermissions, type ProjectRole } from "@/lib/rbac";
import { isRecord, toReadableValue } from "@/lib/worker-result";

interface ProjectAccountsPanelProps {
  apiSession: ApiSession;
  projectId: string;
  activeRole: ProjectRole;
}

interface NormalizedAccountInfo {
  accountId: string;
  platform: string;
  nickname: string;
  status: string;
  displayName: string;
  balance: string;
  workerInstanceId: string;
  workerMachineName: string;
  workerStartedAtUtc: string;
  requestId: string;
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

function normalizeAccountInfo(payload: Record<string, unknown>): NormalizedAccountInfo {
  const account = isRecord(payload.account) ? payload.account : payload;
  const profile = isRecord(account.profile) ? account.profile : null;
  const raw = isRecord(account.raw) ? account.raw : null;

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
    workerInstanceId: toReadableValue(raw?.workerInstanceId ?? payload.workerInstanceId),
    workerMachineName: toReadableValue(raw?.workerMachineName ?? payload.workerMachineName),
    workerStartedAtUtc: toReadableValue(raw?.workerStartedAtUtc ?? payload.workerStartedAtUtc),
    requestId: toReadableValue(payload.requestId),
  };
}

export function ProjectAccountsPanel({
  apiSession,
  projectId,
  activeRole,
}: ProjectAccountsPanelProps) {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
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

  const accountInfoQueries = useQueries({
    queries: accounts.map((account) => ({
      queryKey: ["account.info", apiSession.baseUrl, apiSession.token, projectId, account.id] as const,
      queryFn: () => runAccountActionRequest(apiSession, account.id, "account.info", {}),
      enabled: Boolean(account.id),
      refetchInterval: 30_000,
      staleTime: 10_000,
    })),
  });

  const accountInfoById = useMemo(() => {
    const map = new Map<string, NormalizedAccountInfo>();
    for (let index = 0; index < accounts.length; index += 1) {
      const account = accounts[index];
      const query = accountInfoQueries[index];
      if (query?.data && isRecord(query.data)) {
        map.set(account.id, normalizeAccountInfo(query.data));
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
            ? "Не удалось получить account.info."
            : null;
      if (message) {
        map.set(account.id, message);
      }
    }

    return map;
  }, [accountInfoQueries, accounts]);

  const filteredAccounts = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) {
      return accounts;
    }

    return accounts.filter((account) => {
      const searchable = `${account.displayName} ${account.platform} ${account.id} ${account.businessStatus}`.toLowerCase();
      return searchable.includes(query);
    });
  }, [accounts, search]);

  const selectedAccountInfo = selectedAccountId ? accountInfoById.get(selectedAccountId) ?? null : null;
  const selectedAccountError = selectedAccountId ? accountInfoErrorById.get(selectedAccountId) ?? null : null;
  const anyAccountInfoFetching = accountInfoQueries.some((query) => query.isFetching);

  const activeAccountsCount = accounts.filter((account) => account.businessStatus === "active").length;
  const platformsCount = new Set(accounts.map((account) => account.platform.toLowerCase())).size;
  const modalManageAccountId = modalAccountId || selectedAccountId;

  const refreshAccountInfo = async () => {
    await Promise.all(accountInfoQueries.map((query) => query.refetch()));
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
      <ModulePageShell
        title="Аккаунты / Accounts"
        description="Подключайте аккаунты площадок, проверяйте состояние worker account и управляйте lifecycle из модальных сценариев."
        actions={
          canManageLifecycle ? (
            <button type="button" className="button button-primary" onClick={() => openModal("create")}>
              Добавить аккаунт
            </button>
          ) : null
        }
        stats={[
          { label: "Всего аккаунтов", value: String(accounts.length), hint: "В текущем проекте" },
          { label: "Активные", value: String(activeAccountsCount), hint: "Готовы к работе" },
          { label: "Платформы", value: String(platformsCount), hint: "Уникальные provider'ы" },
        ]}
        main={(
          <section className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Список аккаунтов</h3>
              <button
                type="button"
                className="button button-ghost"
                onClick={refreshAccountInfo}
                disabled={anyAccountInfoFetching}
              >
                Обновить
              </button>
            </div>

            <label className="field">
              <span>Поиск</span>
              <input
                className="input"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="display name / platform / status / id"
              />
            </label>

            {error ? <p className="route-error">{error.message}</p> : null}
            {isLoading ? <p className="route-hint">Загружаем аккаунты...</p> : null}
            {!isLoading && filteredAccounts.length === 0 ? (
              <p className="route-hint">Аккаунты не найдены. Создайте новый или измените фильтр.</p>
            ) : null}

            <ul className="entity-list">
              {filteredAccounts.map((account) => {
                const info = accountInfoById.get(account.id);
                const infoError = accountInfoErrorById.get(account.id);
                const isSelected = account.id === selectedAccountId;

                return (
                  <li
                    key={account.id}
                    className={`entity-list-item ${isSelected ? "is-selected" : ""}`}
                  >
                    <div>
                      <strong>{info?.displayName || account.displayName}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{info?.platform || account.platform}</span>
                        <span className="entity-pill">{info?.status || account.businessStatus}</span>
                        <span className="entity-pill">{account.id}</span>
                      </div>
                      {infoError ? <small className="route-error">{infoError}</small> : null}
                    </div>

                    <div className="inline-actions">
                      <button
                        type="button"
                        className="button button-ghost"
                        onClick={() => setSelectedAccountId(account.id)}
                      >
                        Открыть
                      </button>
                      {canManageLifecycle ? (
                        <button
                          type="button"
                          className="button button-ghost"
                          onClick={() => openModal("manage", { accountId: account.id })}
                        >
                          Управлять
                        </button>
                      ) : null}
                    </div>
                  </li>
                );
              })}
            </ul>
          </section>
        )}
        side={(
          <section className="glass-card page-stack">
            <h3>Контекст проекта</h3>
            <AccountSelector
              accounts={accounts}
              selectedAccountId={selectedAccountId}
              onChange={setSelectedAccountId}
              isLoading={isLoading}
            />

            {!selectedAccount ? (
              <p className="route-hint">Выберите аккаунт, чтобы увидеть детали и действия.</p>
            ) : (
              <>
                <dl className="kv-list">
                  <div>
                    <dt>Display name</dt>
                    <dd>{selectedAccountInfo?.displayName || selectedAccount.displayName}</dd>
                  </div>
                  <div>
                    <dt>Площадка</dt>
                    <dd>{selectedAccountInfo?.platform || selectedAccount.platform}</dd>
                  </div>
                  <div>
                    <dt>Ник</dt>
                    <dd>{selectedAccountInfo?.nickname || "n/a"}</dd>
                  </div>
                  <div>
                    <dt>Баланс</dt>
                    <dd>{selectedAccountInfo?.balance || "n/a"}</dd>
                  </div>
                  <div>
                    <dt>Статус</dt>
                    <dd>{selectedAccountInfo?.status || selectedAccount.businessStatus}</dd>
                  </div>
                </dl>

                {selectedAccountError ? <p className="route-error">{selectedAccountError}</p> : null}

                <details className="details-block">
                  <summary>Technical details</summary>
                  <dl className="kv-list">
                    <div>
                      <dt>Account ID</dt>
                      <dd>{selectedAccount.id}</dd>
                    </div>
                    <div>
                      <dt>Worker instance</dt>
                      <dd>{selectedAccountInfo?.workerInstanceId || "n/a"}</dd>
                    </div>
                    <div>
                      <dt>Worker host</dt>
                      <dd>{selectedAccountInfo?.workerMachineName || "n/a"}</dd>
                    </div>
                    <div>
                      <dt>Worker started at</dt>
                      <dd>{selectedAccountInfo?.workerStartedAtUtc || "n/a"}</dd>
                    </div>
                    <div>
                      <dt>Request ID</dt>
                      <dd>{selectedAccountInfo?.requestId || "n/a"}</dd>
                    </div>
                  </dl>
                </details>

                {canManageLifecycle ? (
                  <button
                    type="button"
                    className="button button-primary"
                    onClick={() => openModal("manage", { accountId: selectedAccount.id })}
                  >
                    Управлять выбранным аккаунтом
                  </button>
                ) : (
                  <p className="route-hint">
                    `moderator` работает только в режиме просмотра без lifecycle-операций.
                  </p>
                )}

                {!canRevealProxy || !canUpdateProxy ? (
                  <p className="route-hint">
                    Proxy reveal/update доступны только ролям owner/admin.
                  </p>
                ) : null}
              </>
            )}
          </section>
        )}
      />

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
