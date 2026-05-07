"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
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

export function ProjectMessageThreadPanel({
  apiSession,
  projectId,
  accountId,
  conversationId,
  mode = "page",
  onCancel,
}: ProjectMessageThreadPanelProps) {
  const queryClient = useQueryClient();
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

        <section className="panel-card page-stack">
          <div className="panel-title-row">
            <h3>Отправить сообщение</h3>
            {mode === "page" && onCancel ? (
              <button type="button" className="button button-ghost" onClick={onCancel}>
                Закрыть чат
              </button>
            ) : null}
          </div>
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
            disabled={sendMessageMutation.isPending || !outgoingMessage.trim()}
            onClick={() => sendMessageMutation.mutate()}
          >
            Отправить сообщение
          </button>
          {mode === "modal" && onCancel ? (
            <button type="button" className="button button-ghost" onClick={onCancel}>
              Закрыть чат
            </button>
          ) : null}
          <p className="route-hint">{status}</p>
        </section>
      </div>
    </div>
  );
}
