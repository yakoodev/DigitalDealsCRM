"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { AccountSelector } from "@/components/account-selector";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import type { ProxyCredentialsMasked } from "@/generated/external-api";
import {
  deleteAccountRequest,
  type ApiSession,
  getMaskedProxyCredentialsRequest,
  revealProxyCredentialsRequest,
  updateAccountRequest,
  updateProxyCredentialsRequest,
} from "@/lib/api-client";
import { hasPermission, projectPermissions, type ProjectRole } from "@/lib/rbac";

interface ProjectAccountManagePanelProps {
  apiSession: ApiSession;
  projectId: string;
  accountId: string;
  activeRole: ProjectRole;
  mode?: "page" | "modal";
  onUpdated?: () => void;
  onDeleted?: () => void;
  onCancel?: () => void;
}

interface AccountDraft {
  displayName: string;
  businessStatus: string;
  proxyHost: string;
  proxyPort: string;
  proxyLogin: string;
  proxyPassword: string;
  proxyReason: string;
  revealReason: string;
}

function buildAccountDraft(displayName: string, businessStatus: string): AccountDraft {
  return {
    displayName,
    businessStatus: businessStatus || "active",
    proxyHost: "",
    proxyPort: "1508",
    proxyLogin: "",
    proxyPassword: "",
    proxyReason: "manual update from UI",
    revealReason: "manual diagnostics in UI",
  };
}

function readRequiredValue(value: string, fieldName: string): string {
  const normalized = value.trim();
  if (!normalized) {
    throw new Error(`Поле «${fieldName}» обязательно.`);
  }

  return normalized;
}

export function ProjectAccountManagePanel({
  apiSession,
  projectId,
  accountId,
  activeRole,
  mode = "page",
  onUpdated,
  onDeleted,
  onCancel,
}: ProjectAccountManagePanelProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState("Выберите аккаунт и внесите изменения.");
  const [draftsByAccountId, setDraftsByAccountId] = useState<Record<string, AccountDraft>>(
    {},
  );
  const [revealedProxyByAccountId, setRevealedProxyByAccountId] = useState<
    Record<string, { host: string; port: number; login: string; password: string }>
  >({});

  const {
    accounts,
    selectedAccountId,
    selectedAccount,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);
  const canManageLifecycle = hasPermission(
    activeRole,
    projectPermissions.accountsLifecycleManage,
  );
  const canUpdateProxy = hasPermission(
    activeRole,
    projectPermissions.proxyCredentialsUpdate,
  );
  const canRevealProxy = hasPermission(
    activeRole,
    projectPermissions.proxyCredentialsReveal,
  );

  useEffect(() => {
    if (accountId && selectedAccountId !== accountId) {
      setSelectedAccountId(accountId);
    }
  }, [accountId, selectedAccountId, setSelectedAccountId]);

  const activeAccount =
    selectedAccount
    ?? accounts.find((account) => account.id === accountId)
    ?? null;
  const activeAccountId = activeAccount?.id ?? "";
  const maskedProxyQueryKey = [
    "account-proxy-masked",
    apiSession.baseUrl,
    apiSession.token,
    projectId,
    activeAccountId,
  ] as const;
  const activeDraft = useMemo(() => {
    if (!activeAccount) {
      return buildAccountDraft("", "active");
    }

    return (
      draftsByAccountId[activeAccount.id]
      ?? buildAccountDraft(activeAccount.displayName, activeAccount.businessStatus)
    );
  }, [activeAccount, draftsByAccountId]);

  const maskedProxyQuery = useQuery<ProxyCredentialsMasked>({
    queryKey: maskedProxyQueryKey,
    enabled: Boolean(activeAccountId),
    queryFn: () =>
      getMaskedProxyCredentialsRequest(apiSession, projectId, activeAccountId),
  });

  const updateDraft = (field: keyof AccountDraft, value: string) => {
    if (!activeAccountId) {
      return;
    }

    setDraftsByAccountId((previous) => ({
      ...previous,
      [activeAccountId]: {
        ...(previous[activeAccountId]
          ?? buildAccountDraft(
            activeAccount?.displayName ?? "",
            activeAccount?.businessStatus ?? "active",
          )),
        [field]: value,
      },
    }));
  };

  const updateAccountMutation = useMutation({
    mutationFn: async () => {
      if (!activeAccountId) {
        throw new Error("Аккаунт не выбран.");
      }

      const displayName = readRequiredValue(activeDraft.displayName, "Название аккаунта");
      const businessStatus = readRequiredValue(activeDraft.businessStatus, "Статус");

      return updateAccountRequest(apiSession, projectId, activeAccountId, {
        displayName,
        businessStatus,
      });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["accounts", apiSession.baseUrl, apiSession.token, projectId],
      });
      setStatus("Изменения аккаунта сохранены.");
      onUpdated?.();
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось обновить аккаунт.");
    },
  });

  const updateProxyMutation = useMutation({
    mutationFn: async () => {
      if (!activeAccountId) {
        throw new Error("Аккаунт не выбран.");
      }

      const host = readRequiredValue(activeDraft.proxyHost, "Proxy host");
      const portRaw = readRequiredValue(activeDraft.proxyPort, "Proxy port");
      const login = readRequiredValue(activeDraft.proxyLogin, "Proxy login");
      const password = readRequiredValue(activeDraft.proxyPassword, "Proxy password");
      const reason = readRequiredValue(activeDraft.proxyReason, "Причина обновления");
      const port = Number(portRaw);

      if (!Number.isInteger(port) || port < 1 || port > 65535) {
        throw new Error("Proxy port должен быть целым числом от 1 до 65535.");
      }

      return updateProxyCredentialsRequest(apiSession, projectId, activeAccountId, {
        reason,
        proxyConfig: {
          host,
          port,
          login,
          password,
        },
      });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: maskedProxyQueryKey });
      setStatus("Proxy credentials обновлены.");
      onUpdated?.();
    },
    onError: (error) => {
      setStatus(
        error instanceof Error
          ? error.message
          : "Не удалось обновить proxy credentials.",
      );
    },
  });

  const revealProxyMutation = useMutation({
    mutationFn: async () => {
      if (!activeAccountId) {
        throw new Error("Аккаунт не выбран.");
      }

      const reason = readRequiredValue(activeDraft.revealReason, "Причина reveal");
      return revealProxyCredentialsRequest(apiSession, projectId, activeAccountId, { reason });
    },
    onSuccess: (proxyConfig) => {
      if (!activeAccountId) {
        return;
      }

      setRevealedProxyByAccountId((previous) => ({
        ...previous,
        [activeAccountId]: proxyConfig,
      }));
      setStatus("Полные proxy credentials загружены.");
    },
    onError: (error) => {
      setStatus(
        error instanceof Error
          ? error.message
          : "Не удалось получить полные proxy credentials.",
      );
    },
  });

  const deleteAccountMutation = useMutation({
    mutationFn: async () => {
      if (!activeAccountId) {
        throw new Error("Аккаунт не выбран.");
      }

      const confirmText = `Удалить аккаунт ${activeAccountId}? Это действие необратимо.`;
      if (!window.confirm(confirmText)) {
        throw new Error("Удаление отменено.");
      }

      return deleteAccountRequest(apiSession, projectId, activeAccountId);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["accounts", apiSession.baseUrl, apiSession.token, projectId],
      });
      setStatus("Аккаунт удалён.");
      if (onDeleted) {
        onDeleted();
        return;
      }

      router.push(`/projects/${projectId}/accounts`);
    },
    onError: (error) => {
      if (error instanceof Error && error.message === "Удаление отменено.") {
        setStatus("Удаление отменено.");
        return;
      }

      setStatus(error instanceof Error ? error.message : "Не удалось удалить аккаунт.");
    },
  });

  const revealedProxy = activeAccountId
    ? revealedProxyByAccountId[activeAccountId] ?? null
    : null;

  if (!canManageLifecycle) {
    return (
      <div className="page-stack" data-testid="project-account-manage-panel-forbidden">
        <section className="panel-card">
          <p className="route-error">
            Недостаточно прав для изменения или удаления аккаунта.
          </p>
          {mode === "page" ? (
            <Link href={`/projects/${projectId}/accounts`} className="button button-ghost">
              Назад к аккаунтам
            </Link>
          ) : onCancel ? (
            <button type="button" className="button button-ghost" onClick={onCancel}>
              Закрыть
            </button>
          ) : null}
        </section>
      </div>
    );
  }

  return (
    <div className="page-stack" data-testid="project-account-manage-panel">
      {mode === "page" ? (
        <>
          <header className="page-section-header">
            <h2>Управление аккаунтом</h2>
            <p>
              Отдельная страница для ключевых операций аккаунта: параметры, proxy credentials
              и удаление.
            </p>
          </header>

          <div className="panel-actions">
            <Link href={`/projects/${projectId}/accounts`} className="button button-ghost">
              Назад к аккаунтам
            </Link>
          </div>
        </>
      ) : null}

      <section className="panel-card">
        <h3>Выбор аккаунта</h3>
        {accountsError ? <p className="route-error">{accountsError.message}</p> : null}
        <AccountSelector
          accounts={accounts}
          selectedAccountId={selectedAccountId}
          onChange={setSelectedAccountId}
          isLoading={accountsLoading}
        />
      </section>

      {!activeAccount && !accountsLoading ? (
        <section className="panel-card">
          <p className="route-hint">Аккаунт не найден. Вернитесь к списку аккаунтов.</p>
        </section>
      ) : null}

      {activeAccount ? (
        <>
          <section className="panel-card">
            <h3>Основные параметры</h3>
            <div className="grid-2">
              <label className="field">
                <span>Display name</span>
                <input
                  className="input"
                  value={activeDraft.displayName}
                  onChange={(event) => updateDraft("displayName", event.target.value)}
                  placeholder="Название аккаунта"
                />
              </label>
              <label className="field">
                <span>Business status</span>
                <select
                  className="input"
                  value={activeDraft.businessStatus}
                  onChange={(event) => updateDraft("businessStatus", event.target.value)}
                >
                  <option value="active">active</option>
                  <option value="paused">paused</option>
                  <option value="blocked">blocked</option>
                  <option value="archived">archived</option>
                </select>
              </label>
            </div>
            <div className="panel-actions">
              <button
                type="button"
                className="button button-primary"
                disabled={updateAccountMutation.isPending}
                onClick={() => updateAccountMutation.mutate()}
              >
                Сохранить параметры аккаунта
              </button>
            </div>
          </section>

          <section className="panel-card">
            <h3>Proxy credentials</h3>
            {maskedProxyQuery.isPending ? (
              <p className="route-hint">Загружаем маскированные credentials...</p>
            ) : null}
            {maskedProxyQuery.error ? (
              <p className="route-error">
                {maskedProxyQuery.error instanceof Error
                  ? maskedProxyQuery.error.message
                  : "Не удалось получить mask proxy credentials."}
              </p>
            ) : null}
            {maskedProxyQuery.data ? (
              <dl className="kv-list">
                <div>
                  <dt>Configured</dt>
                  <dd>{maskedProxyQuery.data.configured ? "yes" : "no"}</dd>
                </div>
                <div>
                  <dt>Host masked</dt>
                  <dd>{maskedProxyQuery.data.hostMasked || "n/a"}</dd>
                </div>
                <div>
                  <dt>Login masked</dt>
                  <dd>{maskedProxyQuery.data.loginMasked || "n/a"}</dd>
                </div>
              </dl>
            ) : null}

            <div className="stacked-block">
              <div className="grid-2">
                <label className="field">
                  <span>Proxy host</span>
                  <input
                    className="input"
                    value={activeDraft.proxyHost}
                    onChange={(event) => updateDraft("proxyHost", event.target.value)}
                    placeholder="45.88.208.237"
                  />
                </label>
                <label className="field">
                  <span>Proxy port</span>
                  <input
                    className="input"
                    type="number"
                    min={1}
                    max={65535}
                    step={1}
                    value={activeDraft.proxyPort}
                    onChange={(event) => updateDraft("proxyPort", event.target.value)}
                    placeholder="1508"
                  />
                </label>
                <label className="field">
                  <span>Proxy login</span>
                  <input
                    className="input"
                    value={activeDraft.proxyLogin}
                    onChange={(event) => updateDraft("proxyLogin", event.target.value)}
                    placeholder="login"
                  />
                </label>
                <label className="field">
                  <span>Proxy password</span>
                  <input
                    className="input"
                    type="password"
                    value={activeDraft.proxyPassword}
                    onChange={(event) => updateDraft("proxyPassword", event.target.value)}
                    placeholder="password"
                  />
                </label>
              </div>
              <label className="field">
                <span>Причина обновления credentials</span>
                <input
                  className="input"
                  value={activeDraft.proxyReason}
                  onChange={(event) => updateDraft("proxyReason", event.target.value)}
                  placeholder="manual update from UI"
                />
              </label>
              <div className="panel-actions">
                <button
                  type="button"
                  className="button button-primary"
                  disabled={updateProxyMutation.isPending || !canUpdateProxy}
                  onClick={() => updateProxyMutation.mutate()}
                >
                  Обновить proxy credentials
                </button>
                {!canUpdateProxy ? (
                  <p className="route-hint">
                    Обновление proxy credentials доступно только owner/admin.
                  </p>
                ) : null}
              </div>
            </div>

            <section className="panel-card panel-soft">
              <h3>Reveal proxy credentials</h3>
              <label className="field">
                <span>Причина reveal</span>
                <input
                  className="input"
                  value={activeDraft.revealReason}
                  onChange={(event) => updateDraft("revealReason", event.target.value)}
                  placeholder="manual diagnostics in UI"
                />
              </label>
              <div className="panel-actions">
                <button
                  type="button"
                  className="button button-ghost"
                  disabled={revealProxyMutation.isPending || !canRevealProxy}
                  onClick={() => revealProxyMutation.mutate()}
                >
                  Показать полные credentials
                </button>
                {!canRevealProxy ? (
                  <p className="route-hint">
                    Reveal proxy credentials доступен только owner/admin.
                  </p>
                ) : null}
              </div>
              {revealedProxy ? (
                <dl className="kv-list">
                  <div>
                    <dt>Host</dt>
                    <dd>{revealedProxy.host}</dd>
                  </div>
                  <div>
                    <dt>Port</dt>
                    <dd>{revealedProxy.port}</dd>
                  </div>
                  <div>
                    <dt>Login</dt>
                    <dd>{revealedProxy.login}</dd>
                  </div>
                  <div>
                    <dt>Password</dt>
                    <dd>{revealedProxy.password}</dd>
                  </div>
                </dl>
              ) : null}
            </section>
          </section>

          <section className="panel-card">
            <h3>Удаление аккаунта</h3>
            <p className="route-hint">
              Удаление приведёт к очистке lifecycle binding в Accounts Manager.
            </p>
            <div className="panel-actions">
              <button
                type="button"
                className="button button-ghost"
                disabled={deleteAccountMutation.isPending}
                onClick={() => deleteAccountMutation.mutate()}
              >
                Удалить аккаунт
              </button>
            </div>
          </section>
        </>
      ) : null}

      <p className="route-hint">{status}</p>
      {mode === "modal" && onCancel ? (
        <div className="panel-actions">
          <button type="button" className="button button-ghost" onClick={onCancel}>
            Закрыть
          </button>
        </div>
      ) : null}
    </div>
  );
}
