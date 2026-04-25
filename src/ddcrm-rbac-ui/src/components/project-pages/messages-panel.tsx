"use client";

import {
  useMutation,
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
  const [conversationSearch, setConversationSearch] = useState("");
  const [selectedConversationByAccount, setSelectedConversationByAccount] =
    useState<Record<string, string>>({});
  const [outgoingMessage, setOutgoingMessage] = useState("");
  const [status, setStatus] = useState("Выберите переписку, чтобы загрузить историю.");

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const selectedConversationId = selectedAccountId
    ? selectedConversationByAccount[selectedAccountId] ?? ""
    : "";

  const conversationsListKey = useMemo(
    () =>
      [
        "conversations.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        selectedAccountId,
      ] as const,
    [apiSession.baseUrl, apiSession.token, projectId, selectedAccountId],
  );

  const conversationsQuery = useQuery({
    queryKey: conversationsListKey,
    enabled: Boolean(selectedAccountId),
    queryFn: () =>
      runAccountActionRequest(apiSession, selectedAccountId, "conversations.list", {
        limit: 100,
      }),
  });

  const messagesListKey = useMemo(
    () =>
      [
        "conversations.messages.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        selectedAccountId,
        selectedConversationId,
      ] as const,
    [
      apiSession.baseUrl,
      apiSession.token,
      projectId,
      selectedAccountId,
      selectedConversationId,
    ],
  );

  const messagesQuery = useQuery({
    queryKey: messagesListKey,
    enabled: Boolean(selectedAccountId && selectedConversationId),
    queryFn: () =>
      runAccountActionRequest(
        apiSession,
        selectedAccountId,
        "conversations.messages.list",
        {
          conversationId: selectedConversationId,
          limit: 200,
        },
      ),
  });

  const sendMessageMutation = useMutation({
    mutationFn: () =>
      runAccountActionRequest(
        apiSession,
        selectedAccountId,
        "conversations.messages.send",
        {
          conversationId: selectedConversationId,
          text: outgoingMessage.trim(),
        },
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: conversationsListKey });
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

  const conversations = extractObjectRows(conversationsQuery.data ?? null, [
    "items",
    "conversations",
    "messages",
  ]);

  const filteredConversations = useMemo(() => {
    const query = conversationSearch.trim().toLowerCase();
    if (!query) {
      return conversations;
    }

    return conversations.filter((conversation) => {
      const title = readFirstString(conversation, [
        "title",
        "subject",
        "counterparty",
        "peer",
      ]).toLowerCase();
      const id = readFirstString(conversation, ["conversationId", "id"]).toLowerCase();
      const preview = toReadableValue(
        conversation.lastMessage ?? conversation.preview ?? "",
      ).toLowerCase();
      return title.includes(query) || id.includes(query) || preview.includes(query);
    });
  }, [conversationSearch, conversations]);

  const messages = extractObjectRows(messagesQuery.data ?? null, [
    "items",
    "messages",
  ]);

  const unreadConversations = conversations.filter(
    (conversation) => readUnreadCount(conversation) > 0,
  ).length;

  const quickReplies = [
    "Здравствуйте! Проверяю ваш запрос и скоро вернусь с ответом.",
    "Спасибо за сообщение. Сейчас уточню детали по товару.",
    "Принято. Могу предложить альтернативный вариант прямо сейчас.",
  ];

  return (
    <div className="page-stack" data-testid="project-messages-panel">
      <header className="page-section-header">
        <h2>Сообщения</h2>
        <p>
          Список переписок загружается автоматически, а история чата запрашивается
          только после выбора конкретной переписки.
        </p>
      </header>

      <section className="summary-grid">
        <article className="summary-card">
          <p>Всего переписок</p>
          <strong>{conversations.length}</strong>
          <small>Для выбранного аккаунта</small>
        </article>
        <article className="summary-card">
          <p>Непрочитанные</p>
          <strong>{unreadConversations}</strong>
          <small>Требуют ответа оператора</small>
        </article>
        <article className="summary-card">
          <p>Режим загрузки</p>
          <strong>Lazy thread</strong>
          <small>История чата грузится только после выбора</small>
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
          <span>Поиск переписок</span>
          <input
            className="input"
            value={conversationSearch}
            onChange={(event) => setConversationSearch(event.target.value)}
            placeholder="Тема, контрагент, ID"
          />
        </label>
      </div>

      {!selectedAccountId ? (
        <p className="route-hint">Выберите аккаунт, чтобы увидеть переписки.</p>
      ) : null}

      {selectedAccountId && conversationsQuery.isPending ? (
        <p className="route-hint">Загружаем список переписок...</p>
      ) : null}

      {selectedAccountId && conversationsQuery.error ? (
        <p className="route-error">
          {conversationsQuery.error instanceof Error
            ? conversationsQuery.error.message
            : "Не удалось получить список переписок."}
        </p>
      ) : null}

      {selectedAccountId && !conversationsQuery.isPending && !conversationsQuery.error ? (
        <div className="split-grid">
          <section className="panel-card">
            <h3>Переписки</h3>
            {filteredConversations.length === 0 ? (
              <p className="route-hint">Переписки не найдены.</p>
            ) : (
              <ul className="entity-list">
                {filteredConversations.map((conversation, index) => {
                  const conversationId = readFirstString(conversation, [
                    "conversationId",
                    "id",
                  ]);
                  const title = readFirstString(conversation, [
                    "title",
                    "subject",
                    "counterparty",
                    "peer",
                  ]);
                  const preview = toReadableValue(
                    conversation.lastMessage ?? conversation.preview ?? "",
                  );
                  const unreadCount = readUnreadCount(conversation);
                  const isActive = conversationId === selectedConversationId;

                  return (
                    <li key={conversationId || `conversation-${index}`}>
                      <button
                        type="button"
                        className={`list-select ${isActive ? "is-active" : ""}`}
                        disabled={!conversationId}
                        onClick={() => {
                          if (conversationId) {
                            setSelectedConversationByAccount((previous) => ({
                              ...previous,
                              [selectedAccountId]: conversationId,
                            }));
                            setStatus("Загружаем историю переписки.");
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
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </section>

          <section className="panel-card">
            <h3>История чата</h3>
            {!selectedConversationId ? (
              <p className="route-hint">
                Выберите переписку слева. До выбора история не загружается.
              </p>
            ) : messagesQuery.isPending ? (
              <p className="route-hint">Загружаем историю...</p>
            ) : messagesQuery.error ? (
              <p className="route-error">
                {messagesQuery.error instanceof Error
                  ? messagesQuery.error.message
                  : "Не удалось получить историю переписки."}
              </p>
            ) : messages.length === 0 ? (
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
                    normalizedDirection.includes("out") ||
                    normalizedDirection.includes("me") ||
                    normalizedDirection.includes("seller");

                  return (
                    <li
                      key={`${selectedConversationId}-${index}`}
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
            )}

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
                  sendMessageMutation.isPending ||
                  !selectedConversationId ||
                  !outgoingMessage.trim()
                }
                onClick={() => sendMessageMutation.mutate()}
              >
                Отправить сообщение
              </button>
              <p className="route-hint">{status}</p>
            </div>
          </section>
        </div>
      ) : null}
    </div>
  );
}
