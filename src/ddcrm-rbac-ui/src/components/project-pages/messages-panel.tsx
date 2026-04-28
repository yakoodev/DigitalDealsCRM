"use client";

import { useQueries } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AccountSelector } from "@/components/account-selector";
import { ModulePageShell } from "@/components/layout/module-page-shell";
import { RouteModalHost } from "@/components/layout/route-modal-host";
import { ProjectMessageThreadPanel } from "@/components/project-pages/message-thread-panel";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import { useRouteModal } from "@/hooks/use-route-modal";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import { extractObjectRows, isRecord, readFirstString, toReadableValue } from "@/lib/worker-result";

interface ProjectMessagesPanelProps {
  apiSession: ApiSession;
  projectId: string;
}

interface ConversationWithAccount {
  accountId: string;
  row: Record<string, unknown>;
}

interface AccountQueryError {
  accountId: string;
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

function normalizePrimitiveText(value: unknown): string {
  if (typeof value === "string" && value.trim()) {
    return value.trim();
  }

  if (typeof value === "number" || typeof value === "boolean") {
    return String(value);
  }

  return "";
}

function readTextFromObject(value: Record<string, unknown>, keys: readonly string[]): string {
  for (const key of keys) {
    const candidate = normalizePrimitiveText(value[key]);
    if (candidate) {
      return candidate;
    }
  }

  return "";
}

function resolveConversationPreview(row: Record<string, unknown>): string {
  const direct = normalizePrimitiveText(row.lastMessagePreview ?? row.preview ?? row.lastMessage);
  if (direct) {
    return direct;
  }

  const fromLastMessage = isRecord(row.lastMessage)
    ? readTextFromObject(row.lastMessage, ["text", "message", "body", "preview"])
    : "";
  if (fromLastMessage) {
    return fromLastMessage;
  }

  if (isRecord(row.preview)) {
    return readTextFromObject(row.preview, ["text", "message", "body", "preview"]);
  }

  return "";
}

export function ProjectMessagesPanel({ apiSession, projectId }: ProjectMessagesPanelProps) {
  const [accountFilterId, setAccountFilterId] = useState("all");
  const [conversationSearch, setConversationSearch] = useState("");
  const [selectedConversationKey, setSelectedConversationKey] = useState("");
  const { modal, accountId: modalAccountId, conversationId: modalConversationId, closeModal, openModal } =
    useRouteModal();

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const accountNameById = useMemo(
    () => new Map(accounts.map((account) => [account.id, account.displayName])),
    [accounts],
  );

  const effectiveFilterId = useMemo(() => {
    if (accountFilterId === "all") {
      return "all";
    }

    return accounts.some((account) => account.id === accountFilterId) ? accountFilterId : "all";
  }, [accountFilterId, accounts]);

  const scopedAccountIds = useMemo(() => {
    if (effectiveFilterId !== "all") {
      return [effectiveFilterId];
    }

    return accounts.map((account) => account.id);
  }, [accounts, effectiveFilterId]);

  const conversationsQueries = useQueries({
    queries: scopedAccountIds.map((accountId) => ({
      queryKey: ["conversations.list", apiSession.baseUrl, apiSession.token, projectId, accountId] as const,
      queryFn: () =>
        runAccountActionRequest(apiSession, accountId, "conversations.list", {
          limit: 100,
        }),
      enabled: Boolean(accountId),
      refetchInterval: 20_000,
      staleTime: 7_000,
    })),
  });

  const conversations = useMemo<ConversationWithAccount[]>(() => {
    return scopedAccountIds.flatMap((accountId, index) => {
      const query = conversationsQueries[index];
      const rows = extractObjectRows(query?.data ?? null, ["items", "conversations", "messages"]);
      return rows.map((row) => ({ accountId, row }));
    });
  }, [conversationsQueries, scopedAccountIds]);

  const accountQueryErrors = useMemo<AccountQueryError[]>(() => {
    return scopedAccountIds.flatMap((accountId, index) => {
      const query = conversationsQueries[index];
      if (!query?.error) {
        return [];
      }

      return [
        {
          accountId,
          message:
            query.error instanceof Error ? query.error.message : "Не удалось получить переписки аккаунта.",
        },
      ];
    });
  }, [conversationsQueries, scopedAccountIds]);

  const filteredConversations = useMemo(() => {
    const query = conversationSearch.trim().toLowerCase();
    if (!query) {
      return conversations;
    }

    return conversations.filter(({ accountId, row }) => {
      const title = readFirstString(row, ["title", "subject", "counterparty", "peer"]).toLowerCase();
      const id = readFirstString(row, ["conversationId", "id"]).toLowerCase();
      const preview = resolveConversationPreview(row).toLowerCase();
      const accountName = (accountNameById.get(accountId) ?? "").toLowerCase();
      return title.includes(query) || id.includes(query) || preview.includes(query) || accountName.includes(query);
    });
  }, [accountNameById, conversationSearch, conversations]);

  const selectedConversation = useMemo(() => {
    if (!selectedConversationKey) {
      return null;
    }

    return (
      filteredConversations.find(({ accountId, row }) => {
        const conversationId = readFirstString(row, ["conversationId", "id"]);
        return `${accountId}:${conversationId}` === selectedConversationKey;
      }) ?? null
    );
  }, [filteredConversations, selectedConversationKey]);

  const unreadConversations = conversations.filter(({ row }) => readUnreadCount(row) > 0).length;
  const anyConversationsPending = conversationsQueries.some((query) => query.isPending);
  const anyConversationsFetching = conversationsQueries.some((query) => query.isFetching);

  const refreshConversations = async () => {
    await Promise.all(conversationsQueries.map((query) => query.refetch()));
  };

  return (
    <div className="page-stack" data-testid="project-messages-panel">
      <ModulePageShell
        title="Сообщения / Messages"
        description="Список переписок загружается автоматически, а история чата открывается только в route-bound модалке."
        stats={[
          { label: "Переписок", value: String(conversations.length), hint: "Для текущего scope" },
          { label: "Непрочитанных", value: String(unreadConversations), hint: "Требуют реакции" },
          { label: "Аккаунтов", value: String(scopedAccountIds.length), hint: "В выборке" },
        ]}
        main={(
          <section className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Список переписок</h3>
              <button
                type="button"
                className="button button-ghost"
                onClick={refreshConversations}
                disabled={anyConversationsFetching || scopedAccountIds.length === 0}
              >
                Обновить
              </button>
            </div>

            {scopedAccountIds.length === 0 ? (
              <p className="route-hint">Добавьте аккаунт в проект, чтобы загрузить переписки.</p>
            ) : anyConversationsPending ? (
              <p className="route-hint">Загружаем список переписок по аккаунтам...</p>
            ) : null}

            {accountQueryErrors.length > 0 ? (
              <section className="status-block status-warning">
                <h4>Часть воркеров недоступна</h4>
                <ul className="entity-list compact-list">
                  {accountQueryErrors.map((entry) => (
                    <li key={`messages-error-${entry.accountId}`} className="entity-list-item">
                      <div>
                        <strong>{accountNameById.get(entry.accountId) ?? entry.accountId}</strong>
                        <p className="route-error">{entry.message}</p>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}

            {!anyConversationsPending && filteredConversations.length === 0 ? (
              <p className="route-hint">Переписки не найдены.</p>
            ) : (
              <ul className="entity-list">
                {filteredConversations.map(({ accountId, row }, index) => {
                  const conversationId = readFirstString(row, ["conversationId", "id"]);
                  const title = readFirstString(row, ["title", "subject", "counterparty", "peer"]);
                  const preview = toReadableValue(resolveConversationPreview(row));
                  const unreadCount = readUnreadCount(row);
                  const accountName = accountNameById.get(accountId) ?? accountId;
                  const itemKey = `${accountId}:${conversationId || index}`;

                  return (
                    <li
                      key={itemKey}
                      className={`entity-list-item ${selectedConversationKey === itemKey ? "is-selected" : ""}`}
                    >
                      <button
                        type="button"
                        className="entity-hitbox"
                        onClick={() => setSelectedConversationKey(itemKey)}
                      >
                        <strong>{title || "Без названия"}</strong>
                        <div className="entity-pills">
                          <span className="entity-pill">{accountName}</span>
                          <span className="entity-pill">{conversationId || "id: n/a"}</span>
                        </div>
                        <p>{preview || "Нет превью"}</p>
                        {unreadCount > 0 ? <small className="badge badge-attention">{unreadCount} новых</small> : null}
                      </button>
                      {conversationId ? (
                        <button
                          type="button"
                          className="button button-primary"
                          data-testid={`open-thread-${accountId}-${conversationId}`}
                          onClick={() =>
                            openModal("thread", {
                              accountId,
                              conversationId,
                            })
                          }
                        >
                          Открыть чат
                        </button>
                      ) : (
                        <span className="route-hint">ID переписки недоступен</span>
                      )}
                    </li>
                  );
                })}
              </ul>
            )}
          </section>
        )}
        side={(
          <section className="glass-card page-stack">
            <h3>Фильтры и контекст</h3>
            {accountsError ? <p className="route-error">{accountsError.message}</p> : null}

            <AccountSelector
              accounts={accounts}
              selectedAccountId={selectedAccountId}
              onChange={setSelectedAccountId}
              isLoading={accountsLoading}
            />

            <label className="field">
              <span>Источник данных</span>
              <select
                className="input"
                value={effectiveFilterId}
                onChange={(event) => setAccountFilterId(event.target.value)}
              >
                <option value="all">Все аккаунты проекта</option>
                {accounts.map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.displayName} · {account.platform}
                  </option>
                ))}
              </select>
            </label>

            <label className="field">
              <span>Поиск</span>
              <input
                className="input"
                value={conversationSearch}
                onChange={(event) => setConversationSearch(event.target.value)}
                placeholder="тема / контрагент / id"
              />
            </label>

            {selectedConversation ? (
              <>
                <h4>Выбранная переписка</h4>
                <dl className="kv-list">
                  <div>
                    <dt>Аккаунт</dt>
                    <dd>
                      {accountNameById.get(selectedConversation.accountId) ?? selectedConversation.accountId}
                    </dd>
                  </div>
                  <div>
                    <dt>Тема</dt>
                    <dd>
                      {readFirstString(selectedConversation.row, [
                        "title",
                        "subject",
                        "counterparty",
                        "peer",
                      ]) || "n/a"}
                    </dd>
                  </div>
                  <div>
                    <dt>Preview</dt>
                    <dd>{resolveConversationPreview(selectedConversation.row) || "n/a"}</dd>
                  </div>
                  <div>
                    <dt>Unread</dt>
                    <dd>{String(readUnreadCount(selectedConversation.row))}</dd>
                  </div>
                </dl>
                <details className="details-block">
                  <summary>Technical details</summary>
                  <dl className="kv-list">
                    <div>
                      <dt>Conversation ID</dt>
                      <dd>{readFirstString(selectedConversation.row, ["conversationId", "id"]) || "n/a"}</dd>
                    </div>
                    <div>
                      <dt>Worker request ID</dt>
                      <dd>{toReadableValue(selectedConversation.row.requestId) || "n/a"}</dd>
                    </div>
                  </dl>
                </details>
              </>
            ) : (
              <p className="route-hint">
                Выберите переписку в списке. История сообщений грузится только после открытия чата.
              </p>
            )}
          </section>
        )}
      />

      <RouteModalHost
        isOpen={modal === "thread"}
        title="История переписки"
        description="Чат загружается по выбранному accountId + conversationId."
        onClose={closeModal}
      >
        <ProjectMessageThreadPanel
          apiSession={apiSession}
          projectId={projectId}
          accountId={modalAccountId}
          conversationId={modalConversationId}
          mode="modal"
          onCancel={closeModal}
        />
      </RouteModalHost>
    </div>
  );
}
