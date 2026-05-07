"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect, useMemo, useRef, useState } from "react";
import { AccountSelector } from "@/components/account-selector";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import { extractObjectRows, isRecord, readFirstString, toReadableValue } from "@/lib/worker-result";

interface ProjectMessageThreadPanelProps {
  apiSession: ApiSession;
  projectId: string;
  accountId: string;
  conversationId: string;
  mode?: "page" | "modal" | "embedded";
  onCancel?: () => void;
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

function resolveMessageText(row: Record<string, unknown>): string {
  const direct = normalizePrimitiveText(row.text ?? row.body ?? row.message);
  if (direct) {
    return direct;
  }

  if (isRecord(row.message)) {
    const nested = readTextFromObject(row.message, ["text", "body", "message", "content"]);
    if (nested) {
      return nested;
    }
  }

  return toReadableValue(row.text ?? row.body ?? row.message);
}

function resolveMessageTimestamp(row: Record<string, unknown>): string {
  const direct = normalizePrimitiveText(row.sentAt ?? row.createdAt ?? row.timestamp);
  if (direct) {
    return direct;
  }

  if (isRecord(row.createdAt)) {
    return readTextFromObject(row.createdAt, ["value", "iso", "utc", "dateTime"]);
  }

  return "";
}

function parseMessageTimestampMs(timestamp: string): number | null {
  const normalized = timestamp.trim();
  if (!normalized) {
    return null;
  }

  if (/^\d+$/.test(normalized)) {
    const parsedNumber = Number(normalized);
    if (!Number.isFinite(parsedNumber)) {
      return null;
    }

    return parsedNumber > 1_000_000_000_000 ? parsedNumber : parsedNumber * 1000;
  }

  const parsedDate = Date.parse(normalized);
  return Number.isFinite(parsedDate) ? parsedDate : null;
}

function parseMessageSequence(row: Record<string, unknown>): number | null {
  const candidates = [
    row.messageId,
    row.id,
    row.sequence,
    row.seq,
  ];

  for (const candidate of candidates) {
    if (typeof candidate === "number" && Number.isFinite(candidate)) {
      return candidate;
    }

    if (typeof candidate === "string" && /^\d+$/.test(candidate.trim())) {
      const parsed = Number(candidate);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }
  }

  return null;
}

export function ProjectMessageThreadPanel({
  apiSession,
  projectId,
  accountId,
  conversationId,
  mode = "page",
  onCancel,
}: ProjectMessageThreadPanelProps) {
  const queryClient = useQueryClient();
  const chatListRef = useRef<HTMLUListElement | null>(null);
  const [outgoingMessage, setOutgoingMessage] = useState("");
  const [status, setStatus] = useState("Введите сообщение и отправьте его в выбранную переписку.");

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  useEffect(() => {
    if (accountId && selectedAccountId !== accountId) {
      setSelectedAccountId(accountId);
    }
  }, [accountId, selectedAccountId, setSelectedAccountId]);

  const messagesListKey = useMemo(
    () =>
      [
        "conversations.messages.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        accountId,
        conversationId,
      ] as const,
    [accountId, apiSession.baseUrl, apiSession.token, conversationId, projectId],
  );

  const messagesQuery = useQuery({
    queryKey: messagesListKey,
    enabled: Boolean(accountId && conversationId),
    queryFn: () =>
      runAccountActionRequest(
        apiSession,
        accountId,
        "conversations.messages.list",
        {
          conversationId,
          limit: 200,
        },
      ),
    refetchInterval: 12_000,
    staleTime: 5_000,
  });

  const sendMessageMutation = useMutation({
    mutationFn: () =>
      runAccountActionRequest(
        apiSession,
        accountId,
        "conversations.messages.send",
        {
          conversationId,
          text: outgoingMessage.trim(),
        },
      ),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: messagesListKey }),
        queryClient.invalidateQueries({
          queryKey: [
            "conversations.list",
            apiSession.baseUrl,
            apiSession.token,
            projectId,
            accountId,
          ],
        }),
      ]);

      setOutgoingMessage("");
      setStatus("Сообщение отправлено.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось отправить сообщение.");
    },
  });

  const messages = extractObjectRows(messagesQuery.data ?? null, ["items", "messages"]);
  const orderedMessages = useMemo(() => {
    return messages
      .map((row, index) => {
        const timestamp = resolveMessageTimestamp(row);
        return {
          row,
          index,
          timestampMs: parseMessageTimestampMs(timestamp),
          sequence: parseMessageSequence(row),
        };
      })
      .sort((left, right) => {
        if (left.timestampMs !== null && right.timestampMs !== null && left.timestampMs !== right.timestampMs) {
          return left.timestampMs - right.timestampMs;
        }

        if (left.sequence !== null && right.sequence !== null && left.sequence !== right.sequence) {
          return left.sequence - right.sequence;
        }

        // Keep worker order if no reliable time/sequence fields are available.
        return left.index - right.index;
      })
      .map((entry) => entry.row);
  }, [messages]);

  useEffect(() => {
    const node = chatListRef.current;
    if (!node) {
      return;
    }

    node.scrollTop = node.scrollHeight;
  }, [orderedMessages.length, messagesQuery.dataUpdatedAt]);

  const activeAccount =
    accounts.find((account) => account.id === accountId)
    ?? (accountId
      ? {
          id: accountId,
          displayName: accountId,
          platform: "unknown",
        }
      : null);

  const quickReplies = [
    "Здравствуйте! Проверяю ваш запрос и скоро вернусь с ответом.",
    "Спасибо за сообщение. Сейчас уточню детали по товару.",
    "Принято. Подтверждаю, что запрос взят в работу.",
  ];
  const isEmbedded = mode === "embedded";
  const showFullContext = mode !== "embedded";
  const headerSubtitle = "История и отправка сообщения вынесены в отдельный route, чтобы обзорная страница сообщений оставалась лёгкой.";

  if (!accountId || !conversationId) {
    return (
      <div className="page-stack" data-testid="project-message-thread-panel">
        <header className="page-section-header">
          <h2>Чат переписки</h2>
          <p>Чтобы открыть историю чата, выберите переписку на странице списка.</p>
        </header>
        <section className="panel-card">
          <p className="route-error">Не указан `accountId` или `conversationId`.</p>
          {mode === "page" ? (
            <Link href={`/projects/${projectId}/messages`} className="button button-primary">
              Вернуться к списку переписок
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
    <div className="page-stack" data-testid="project-message-thread-panel">
      {mode === "page" ? (
        <header className="page-section-header">
          <h2>Чат переписки</h2>
          <p>{headerSubtitle}</p>
        </header>
      ) : null}

      {!isEmbedded ? (
        <section className="summary-grid">
          <article className="summary-card">
            <p>Conversation ID</p>
            <strong>{conversationId}</strong>
            <small>Worker-thread идентификатор</small>
          </article>
          <article className="summary-card">
            <p>Аккаунт</p>
            <strong>{activeAccount?.displayName ?? accountId}</strong>
            <small>{activeAccount?.platform ?? "unknown"}</small>
          </article>
          <article className="summary-card">
            <p>Сообщений в истории</p>
            <strong>{messagesQuery.isPending ? "…" : messages.length}</strong>
            <small>С автосинхронизацией каждые 12 секунд</small>
          </article>
        </section>
      ) : null}

      {showFullContext ? (
        <section className="panel-card">
          <div className="panel-title-row">
            <h3>Контекст чата</h3>
            {mode === "page" ? (
              <Link href={`/projects/${projectId}/messages`} className="button button-ghost">
                Назад к списку переписок
              </Link>
            ) : onCancel ? (
              <button type="button" className="button button-ghost" onClick={onCancel}>
                Закрыть
              </button>
            ) : null}
          </div>
        {accountsError ? <p className="route-error">{accountsError.message}</p> : null}
        <AccountSelector
          accounts={accounts}
          selectedAccountId={selectedAccountId}
          onChange={setSelectedAccountId}
          isLoading={accountsLoading}
        />
        </section>
      ) : null}

      <div className={isEmbedded ? "thread-embedded-stack" : "split-grid"}>
        <section className="panel-card page-stack">
          <div className="panel-title-row">
            <h3>История сообщений</h3>
            <button
              type="button"
              className="button button-ghost"
              onClick={() => messagesQuery.refetch()}
              disabled={messagesQuery.isFetching}
            >
              Обновить
            </button>
          </div>
          {messagesQuery.isPending ? <p className="route-hint">Загружаем историю...</p> : null}
          {messagesQuery.error ? (
            <p className="route-error">
              {messagesQuery.error instanceof Error
                ? messagesQuery.error.message
                : "Не удалось получить историю переписки."}
            </p>
          ) : null}
          {!messagesQuery.isPending && !messagesQuery.error ? (
            orderedMessages.length === 0 ? (
              <p className="route-hint">Сообщений пока нет.</p>
            ) : (
              <ul ref={chatListRef} className="chat-list">
                {orderedMessages.map((message, index) => {
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
                      key={`${conversationId}-${index}`}
                      className={`chat-item ${isOutgoing ? "is-outgoing" : "is-incoming"}`}
                    >
                      <strong>{direction || "system"}</strong>
                      <p>{resolveMessageText(message) || "Сообщение без текста"}</p>
                      <small>{resolveMessageTimestamp(message) || "время не указано"}</small>
                    </li>
                  );
                })}
              </ul>
            )
          ) : null}
        </section>

        <div className="message-compose-inline">
          <div className="message-compose-row" title={status}>
            <div className="message-template-popover">
              <button type="button" className="button button-ghost button-small message-template-trigger">
                Шаблоны
              </button>
              <div className="message-template-menu" role="menu">
                {quickReplies.map((template, index) => (
                  <button
                    key={template}
                    type="button"
                    role="menuitem"
                    className="message-template-item"
                    onClick={() => setOutgoingMessage(template)}
                    title={template}
                  >
                    Шаблон {index + 1}
                  </button>
                ))}
              </div>
            </div>
            <input
              className="input message-compose-input"
              value={outgoingMessage}
              onChange={(event) => setOutgoingMessage(event.target.value)}
              placeholder="Введите сообщение"
              onKeyDown={(event) => {
                if (event.key === "Enter" && !event.shiftKey && !sendMessageMutation.isPending && outgoingMessage.trim()) {
                  event.preventDefault();
                  sendMessageMutation.mutate();
                }
              }}
            />
            <button
              type="button"
              className="button button-primary button-small message-send-button"
              aria-label="Отправить сообщение"
              disabled={sendMessageMutation.isPending || !outgoingMessage.trim()}
              onClick={() => sendMessageMutation.mutate()}
            >
              <span aria-hidden="true">➤</span>
              <span className="visually-hidden">Отправить сообщение</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
