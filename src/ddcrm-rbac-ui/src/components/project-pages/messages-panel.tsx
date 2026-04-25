"use client";

import {
  useMutation,
  useQueries,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AccountSelector } from "@/components/account-selector";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import {
  extractObjectRows,
  readFirstString,
  toReadableValue,
} from "@/lib/worker-result";

interface ProjectMessagesPanelProps {
  apiSession: ApiSession;
  projectId: string;
}

interface ConversationWithAccount {
  accountId: string;
  row: Record<string, unknown>;
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

export function ProjectMessagesPanel({
  apiSession,
  projectId,
}: ProjectMessagesPanelProps) {
  const queryClient = useQueryClient();
  const [accountFilterId, setAccountFilterId] = useState("all");
  const [conversationSearch, setConversationSearch] = useState("");
  const [selectedThread, setSelectedThread] = useState<{
    accountId: string;
    conversationId: string;
  } | null>(null);
  const [outgoingMessage, setOutgoingMessage] = useState("");
  const [status, setStatus] = useState("Выберите переписку, чтобы загрузить историю.");

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const accountNameById = useMemo(() => {
    return new Map(accounts.map((account) => [account.id, account.displayName]));
  }, [accounts]);

  const effectiveFilterId = useMemo(() => {
    if (accountFilterId === "all") {
      return "all";
    }

    return accounts.some((account) => account.id === accountFilterId)
      ? accountFilterId
      : "all";
  }, [accountFilterId, accounts]);

  const scopedAccountIds = useMemo(() => {
    if (effectiveFilterId !== "all") {
      return [effectiveFilterId];
    }

    return accounts.map((account) => account.id);
  }, [accounts, effectiveFilterId]);

  const conversationsQueries = useQueries({
    queries: scopedAccountIds.map((accountId) => ({
      queryKey: [
        "conversations.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        accountId,
      ] as const,
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
      const rows = extractObjectRows(query?.data ?? null, [
        "items",
        "conversations",
        "messages",
      ]);

      return rows.map((row) => ({
        accountId,
        row,
      }));
    });
  }, [conversationsQueries, scopedAccountIds]);

  const filteredConversations = useMemo(() => {
    const query = conversationSearch.trim().toLowerCase();
    if (!query) {
      return conversations;
    }

    return conversations.filter(({ accountId, row }) => {
      const title = readFirstString(row, [
        "title",
        "subject",
        "counterparty",
        "peer",
      ]).toLowerCase();
      const id = readFirstString(row, ["conversationId", "id"]).toLowerCase();
      const preview = toReadableValue(
        row.lastMessage ?? row.preview ?? "",
      ).toLowerCase();
      const accountName = (accountNameById.get(accountId) ?? "").toLowerCase();
      return (
        title.includes(query)
        || id.includes(query)
        || preview.includes(query)
        || accountName.includes(query)
      );
    });
  }, [accountNameById, conversationSearch, conversations]);

  const selectedThreadStillVisible = useMemo(() => {
    if (!selectedThread) {
      return false;
    }

    return filteredConversations.some(({ accountId, row }) => {
      const conversationId = readFirstString(row, ["conversationId", "id"]);
      return accountId === selectedThread.accountId && conversationId === selectedThread.conversationId;
    });
  }, [filteredConversations, selectedThread]);

  const effectiveSelectedThread = selectedThreadStillVisible ? selectedThread : null;

  const messagesListKey = useMemo(
    () =>
      [
        "conversations.messages.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        effectiveSelectedThread?.accountId ?? "",
        effectiveSelectedThread?.conversationId ?? "",
      ] as const,
    [
      apiSession.baseUrl,
      apiSession.token,
      projectId,
      effectiveSelectedThread?.accountId,
      effectiveSelectedThread?.conversationId,
    ],
  );

  const messagesQuery = useQuery({
    queryKey: messagesListKey,
    enabled: Boolean(effectiveSelectedThread),
    queryFn: () =>
      runAccountActionRequest(
        apiSession,
        effectiveSelectedThread!.accountId,
        "conversations.messages.list",
        {
          conversationId: effectiveSelectedThread!.conversationId,
          limit: 200,
        },
      ),
    refetchInterval: effectiveSelectedThread ? 12_000 : false,
    staleTime: 5_000,
  });

  const sendMessageMutation = useMutation({
    mutationFn: () => {
      if (!effectiveSelectedThread) {
        throw new Error("Выберите переписку для отправки сообщения.");
      }

      return runAccountActionRequest(
        apiSession,
        effectiveSelectedThread.accountId,
        "conversations.messages.send",
        {
          conversationId: effectiveSelectedThread.conversationId,
          text: outgoingMessage.trim(),
        },
      );
    },
    onSuccess: async () => {
      if (effectiveSelectedThread) {
        await queryClient.invalidateQueries({
          queryKey: [
            "conversations.list",
            apiSession.baseUrl,
            apiSession.token,
            projectId,
            effectiveSelectedThread.accountId,
          ],
        });
      }

      await queryClient.invalidateQueries({ queryKey: messagesListKey });
      setOutgoingMessage("");
      setStatus("Сообщение отправлено.");
    },
    onError: (error) => {
      setStatus(
        error instanceof Error
          ? error.message
          : "Не удалось отправить сообщение.",
      );
    },
  });

  const messages = extractObjectRows(messagesQuery.data ?? null, [
    "items",
    "messages",
  ]);

  const unreadConversations = conversations.filter(
    ({ row }) => readUnreadCount(row) > 0,
  ).length;

  const anyConversationsPending = conversationsQueries.some((query) => query.isPending);
  const anyConversationsFetching = conversationsQueries.some((query) => query.isFetching);
  const firstConversationsError = conversationsQueries.find((query) => query.error)?.error;

  const quickReplies = [
    "Здравствуйте! Проверяю ваш запрос и скоро вернусь с ответом.",
    "Спасибо за сообщение. Сейчас уточню детали по товару.",
    "Принято. Могу предложить альтернативный вариант прямо сейчас.",
  ];

  const refreshConversations = async () => {
    await Promise.all(conversationsQueries.map((query) => query.refetch()));
  };

  const selectedThreadAccountName = effectiveSelectedThread
    ? accountNameById.get(effectiveSelectedThread.accountId) ?? effectiveSelectedThread.accountId
    : "";

  return (
    <div className="page-stack" data-testid="project-messages-panel">
      <header className="page-section-header">
        <h2>Сообщения</h2>
        <p>
          Список переписок собирается по всем аккаунтам проекта. История конкретного
          чата загружается только после выбора переписки.
        </p>
      </header>

      <section className="summary-grid">
        <article className="summary-card">
          <p>Всего переписок</p>
          <strong>{conversations.length}</strong>
          <small>Для выбранного scope аккаунтов</small>
        </article>
        <article className="summary-card">
          <p>Непрочитанные</p>
          <strong>{unreadConversations}</strong>
          <small>Требуют ответа оператора</small>
        </article>
        <article className="summary-card">
          <p>Аккаунтов в выборке</p>
          <strong>{scopedAccountIds.length}</strong>
          <small>
            {effectiveFilterId === "all"
              ? "Отображаем все аккаунты"
              : "Выбран конкретный аккаунт"}
          </small>
        </article>
      </section>

      <div className="stacked-block">
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
          <span>Поиск переписок</span>
          <input
            className="input"
            value={conversationSearch}
            onChange={(event) => setConversationSearch(event.target.value)}
            placeholder="Тема, контрагент, ID, аккаунт"
          />
        </label>
      </div>

      {scopedAccountIds.length === 0 ? (
        <p className="route-hint">Добавьте аккаунт в проект, чтобы загрузить переписки.</p>
      ) : anyConversationsPending ? (
        <p className="route-hint">Загружаем список переписок по аккаунтам...</p>
      ) : firstConversationsError ? (
        <p className="route-error">
          {firstConversationsError instanceof Error
            ? firstConversationsError.message
            : "Не удалось получить список переписок."}
        </p>
      ) : (
        <div className="split-grid">
          <section className="panel-card">
            <div className="panel-title-row">
              <h3>Переписки</h3>
              <button
                type="button"
                className="button button-ghost"
                onClick={refreshConversations}
                disabled={anyConversationsFetching || scopedAccountIds.length === 0}
              >
                Обновить
              </button>
            </div>
            {anyConversationsFetching ? (
              <p className="route-hint">Синхронизируем переписки...</p>
            ) : null}
            {filteredConversations.length === 0 ? (
              <p className="route-hint">Переписки не найдены.</p>
            ) : (
              <ul className="entity-list">
                {filteredConversations.map(({ accountId, row }, index) => {
                  const conversationId = readFirstString(row, [
                    "conversationId",
                    "id",
                  ]);
                  const title = readFirstString(row, [
                    "title",
                    "subject",
                    "counterparty",
                    "peer",
                  ]);
                  const preview = toReadableValue(
                    row.lastMessage ?? row.preview ?? "",
                  );
                  const unreadCount = readUnreadCount(row);
                  const accountName = accountNameById.get(accountId) ?? accountId;
                  const isActive =
                    accountId === effectiveSelectedThread?.accountId
                    && conversationId === effectiveSelectedThread?.conversationId;

                  return (
                    <li key={`${accountId}:${conversationId || index}`}>
                      <button
                        type="button"
                        className={`list-select ${isActive ? "is-active" : ""}`}
                        disabled={!conversationId}
                        onClick={() => {
                          if (conversationId) {
                            setSelectedThread({
                              accountId,
                              conversationId,
                            });
                            setStatus(`Загружаем историю переписки (${accountName}).`);
                          }
                        }}
                      >
                        <div className="list-select-head">
                          <strong>{title || "Без названия"}</strong>
                          {unreadCount > 0 ? (
                            <span className="badge badge-attention">{unreadCount}</span>
                          ) : null}
                        </div>
                        <small>{conversationId || "id недоступен"}</small>
                        <p>{preview || "Нет превью"}</p>
                        <small>Аккаунт: {accountName}</small>
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </section>

          <section className="panel-card">
            <div className="panel-title-row">
              <h3>История чата</h3>
              <button
                type="button"
                className="button button-ghost"
                onClick={() => messagesQuery.refetch()}
                disabled={!effectiveSelectedThread || messagesQuery.isFetching}
              >
                Обновить
              </button>
            </div>
            {!effectiveSelectedThread ? (
              <p className="route-hint">
                Выберите переписку слева. До выбора история не загружается.
              </p>
            ) : (
              <p className="route-hint">
                Аккаунт переписки: <strong>{selectedThreadAccountName}</strong>
              </p>
            )}
            {effectiveSelectedThread && messagesQuery.isPending ? (
              <p className="route-hint">Загружаем историю...</p>
            ) : null}
            {effectiveSelectedThread && messagesQuery.error ? (
              <p className="route-error">
                {messagesQuery.error instanceof Error
                  ? messagesQuery.error.message
                  : "Не удалось получить историю переписки."}
              </p>
            ) : null}
            {effectiveSelectedThread && !messagesQuery.isPending && !messagesQuery.error ? (
              messages.length === 0 ? (
                <p className="route-hint">Сообщений пока нет.</p>
              ) : (
                <ul className="chat-list">
                  {messages.map((message, index) => {
                    const direction = readFirstString(message, [
                      "direction",
                      "author",
                      "from",
                    ]);
                    const normalizedDirection = direction.toLowerCase();
                    const isOutgoing =
                      normalizedDirection.includes("out")
                      || normalizedDirection.includes("me")
                      || normalizedDirection.includes("seller");

                    return (
                      <li
                        key={`${effectiveSelectedThread.conversationId}-${index}`}
                        className={`chat-item ${isOutgoing ? "is-outgoing" : "is-incoming"}`}
                      >
                        <strong>{direction || "system"}</strong>
                        <p>{toReadableValue(message.text ?? message.body ?? message.message)}</p>
                        <small>
                          {toReadableValue(
                            message.sentAt ?? message.createdAt ?? message.timestamp,
                          )}
                        </small>
                      </li>
                    );
                  })}
                </ul>
              )
            ) : null}

            <div className="stacked-block">
              <div className="quick-replies">
                {quickReplies.map((template, index) => (
                  <button
                    key={template}
                    type="button"
                    className="button button-ghost"
                    onClick={() => setOutgoingMessage(template)}
                  >
                    Шаблон {index + 1}
                  </button>
                ))}
              </div>
              <label className="field">
                <span>Новое сообщение</span>
                <textarea
                  className="input textarea"
                  value={outgoingMessage}
                  onChange={(event) => setOutgoingMessage(event.target.value)}
                  placeholder="Введите сообщение"
                />
              </label>
              <button
                type="button"
                className="button button-primary"
                disabled={
                  sendMessageMutation.isPending
                  || !effectiveSelectedThread
                  || !outgoingMessage.trim()
                }
                onClick={() => sendMessageMutation.mutate()}
              >
                Отправить сообщение
              </button>
              <p className="route-hint">{status}</p>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}
