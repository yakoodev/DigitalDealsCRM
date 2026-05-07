"use client";

import { useQueries } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { RouteModalHost } from "@/components/layout/route-modal-host";
import { ProjectMessageThreadPanel } from "@/components/project-pages/message-thread-panel";
import { EmptyState } from "@/components/ui/page-primitives";
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

function resolveConversationTitle(row: Record<string, unknown>): string {
  const direct = readFirstString(row, [
    "title",
    "subject",
    "counterparty",
    "peer",
    "peerName",
    "nickname",
    "displayName",
    "username",
    "userName",
  ]);
  if (direct) {
    return direct;
  }

  const nestedPeer = isRecord(row.peer) ? readTextFromObject(row.peer, [
    "name",
    "nickname",
    "displayName",
    "username",
    "title",
  ]) : "";
  if (nestedPeer) {
    return nestedPeer;
  }

  const conversationId = readFirstString(row, ["conversationId", "id"]);
  if (conversationId) {
    return `Диалог ${conversationId}`;
  }

  return "Диалог";
}

function resolveConversationAvatarUrl(row: Record<string, unknown>): string {
  const direct = readFirstString(row, [
    "avatarUrl",
    "avatar",
    "avatar_url",
    "photoUrl",
    "imageUrl",
    "peerAvatarUrl",
    "peerAvatar",
  ]);
  if (direct) {
    return direct;
  }

  if (isRecord(row.peer)) {
    return readTextFromObject(row.peer, [
      "avatarUrl",
      "avatar",
      "avatar_url",
      "photoUrl",
      "imageUrl",
    ]);
  }

  return "";
}

function resolveAvatarFallback(title: string, conversationId: string): string {
  const source = title.trim() || conversationId.trim() || "?";
  const first = Array.from(source)[0] ?? "?";
  return first.toUpperCase();
}

export function ProjectMessagesPanel({ apiSession, projectId }: ProjectMessagesPanelProps) {
  const [accountFilterId, setAccountFilterId] = useState("all");
  const [conversationSearch, setConversationSearch] = useState("");
  const [selectedConversationKey, setSelectedConversationKey] = useState("");
  const { modal, accountId: modalAccountId, conversationId: modalConversationId, closeModal, openModal } =
    useRouteModal();

  const {
    accounts,
    isLoading: accountsLoading,
    error: accountsError,
  } = useProjectAccounts(apiSession, projectId);

  const modalConversationKey =
    modal === "thread" && modalAccountId && modalConversationId
      ? `${modalAccountId}:${modalConversationId}`
      : "";
  const effectiveSelectedConversationKey = modalConversationKey || selectedConversationKey;

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
      const title = resolveConversationTitle(row).toLowerCase();
      const id = readFirstString(row, ["conversationId", "id"]).toLowerCase();
      const preview = resolveConversationPreview(row).toLowerCase();
      const accountName = (accountNameById.get(accountId) ?? "").toLowerCase();
      return title.includes(query) || id.includes(query) || preview.includes(query) || accountName.includes(query);
    });
  }, [accountNameById, conversationSearch, conversations]);

  const selectedConversation = useMemo(() => {
    if (!effectiveSelectedConversationKey) {
      return null;
    }

    return (
      conversations.find(({ accountId, row }) => {
        const conversationId = readFirstString(row, ["conversationId", "id"]);
        return `${accountId}:${conversationId}` === effectiveSelectedConversationKey;
      }) ?? null
    );
  }, [conversations, effectiveSelectedConversationKey]);

  const unreadConversations = conversations.filter(({ row }) => readUnreadCount(row) > 0).length;
  const anyConversationsPending = conversationsQueries.some((query) => query.isPending);
  const anyConversationsFetching = conversationsQueries.some((query) => query.isFetching);
  const selectedConversationId = selectedConversation
    ? readFirstString(selectedConversation.row, ["conversationId", "id"])
    : "";
  const selectedConversationTitle = selectedConversation
    ? resolveConversationTitle(selectedConversation.row)
    : "";
  const selectedConversationPreview = selectedConversation
    ? resolveConversationPreview(selectedConversation.row)
    : "";
  const selectedUnread = selectedConversation
    ? readUnreadCount(selectedConversation.row)
    : 0;
  const selectedAccountName = selectedConversation
    ? accountNameById.get(selectedConversation.accountId) ?? selectedConversation.accountId
    : "";

  const refreshConversations = async () => {
    await Promise.all(conversationsQueries.map((query) => query.refetch()));
  };

  return (
    <div className="page-stack messages-page" data-testid="project-messages-panel">
      <section className="page-head">
        <div className="page-head__row">
          <div>
            <h1 className="page-title">Сообщения</h1>
          </div>
          <div className="inline">
            <span className="status status--info">Непрочитано: {unreadConversations}</span>
            <button
              type="button"
              className="button button-ghost button-small"
              onClick={refreshConversations}
              disabled={anyConversationsFetching || scopedAccountIds.length === 0}
            >
              Обновить
            </button>
          </div>
        </div>
      </section>

      <section className="messages-shell">
        <section className="panel-card dialogs-panel">
          <div className="dialogs-toolbar">
            <div className="panel-title-row">
              <h3>Диалоги</h3>
              {anyConversationsPending ? <small className="route-hint">Загрузка...</small> : null}
            </div>
            <div className="dialogs-filters">
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
              <input
                className="input"
                value={conversationSearch}
                onChange={(event) => setConversationSearch(event.target.value)}
                placeholder="тема / контрагент / id"
              />
            </div>
          </div>
          <div className="dialogs-body">
            {accountsError ? <p className="route-error">{accountsError.message}</p> : null}
            {accountsLoading ? <p className="route-hint">Загружаем аккаунты...</p> : null}

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

            {scopedAccountIds.length === 0 ? (
              <EmptyState
                title="Нет источников переписок"
                description="Добавьте аккаунт в проект, чтобы загрузить диалоги."
              />
            ) : null}

            {scopedAccountIds.length > 0 && !anyConversationsPending && filteredConversations.length === 0 ? (
              <EmptyState
                title="Переписки не найдены"
                description="Проверьте фильтр и поисковый запрос."
              />
            ) : null}

            {filteredConversations.length > 0 ? (
              <ul className="dialogs-list">
                {filteredConversations.map(({ accountId, row }, index) => {
                  const conversationId = readFirstString(row, ["conversationId", "id"]);
                  const title = resolveConversationTitle(row);
                  const avatarUrl = resolveConversationAvatarUrl(row);
                  const preview = toReadableValue(resolveConversationPreview(row));
                  const unreadCount = readUnreadCount(row);
                  const accountName = accountNameById.get(accountId) ?? accountId;
                  const itemKey = `${accountId}:${conversationId || index}`;
                  const avatarFallback = resolveAvatarFallback(title, conversationId || "");

                  return (
                    <li
                      key={itemKey}
                      className={`dialog-item ${effectiveSelectedConversationKey === itemKey ? "is-active" : ""}`}
                    >
                      <button
                        type="button"
                        className="entity-hitbox"
                        onClick={() => setSelectedConversationKey(itemKey)}
                      >
                        <div className="dialog-main">
                          <span className="dialog-avatar" aria-hidden="true">
                            {avatarUrl ? (
                              <img
                                src={avatarUrl}
                                alt=""
                                className="dialog-avatar-image"
                                loading="lazy"
                                referrerPolicy="no-referrer"
                              />
                            ) : (
                              <span className="dialog-avatar-fallback">{avatarFallback}</span>
                            )}
                          </span>
                          <div className="dialog-content">
                            <strong>{title}</strong>
                            <div className="dialog-tags">
                              <span className="chip">{accountName}</span>
                              <span className="chip">{conversationId || "id: n/a"}</span>
                              {unreadCount > 0 ? <span className="unread-badge">{unreadCount}</span> : null}
                            </div>
                            <p className="dialog-preview">{preview || "Нет превью"}</p>
                          </div>
                        </div>
                      </button>
                      {conversationId ? (
                        <button
                          type="button"
                          className="button button-ghost button-small"
                          data-testid={`open-thread-${accountId}-${conversationId}`}
                          onClick={() =>
                            openModal("thread", {
                              accountId,
                              conversationId,
                            })
                          }
                        >
                          Открыть
                        </button>
                      ) : null}
                    </li>
                  );
                })}
              </ul>
            ) : null}
          </div>
        </section>

        <section className="panel-card chat-panel">
          {!selectedConversation || !selectedConversationId ? (
            <EmptyState
              title="Переписка не выбрана"
              description="Выберите диалог слева, чтобы открыть историю и отправить сообщение."
            />
          ) : (
            <>
              <div className="chat-head">
                <div className="chat-head__main">
                  <h2 className="chat-head__title">{selectedConversationTitle || "Чат переписки"}</h2>
                  <p className="dialog-meta">{selectedAccountName}</p>
                </div>
                <div className="chat-actions">
                  <button
                    type="button"
                    className="button button-ghost button-small"
                    onClick={() =>
                      openModal("thread", {
                        accountId: selectedConversation.accountId,
                        conversationId: selectedConversationId,
                      })
                    }
                  >
                    Открыть в модалке
                  </button>
                </div>
              </div>

              <div className="chat-context">
                <div className="context-grid">
                  <div className="context-card">
                    <span className="context-label">Account</span>
                    <span className="context-value">{selectedAccountName}</span>
                  </div>
                  <div className="context-card">
                    <span className="context-label">Conversation ID</span>
                    <span className="context-value">{selectedConversationId}</span>
                  </div>
                  <div className="context-card">
                    <span className="context-label">Unread</span>
                    <span className="context-value">{selectedUnread}</span>
                  </div>
                  <div className="context-card">
                    <span className="context-label">Preview</span>
                    <span className="context-value">{selectedConversationPreview || "n/a"}</span>
                  </div>
                </div>
              </div>

              <ProjectMessageThreadPanel
                apiSession={apiSession}
                projectId={projectId}
                accountId={selectedConversation.accountId}
                conversationId={selectedConversationId}
                mode="embedded"
              />
            </>
          )}
        </section>
      </section>

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
