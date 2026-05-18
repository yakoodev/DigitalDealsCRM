"use client";
/* eslint-disable react-hooks/set-state-in-effect */

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ApiSession } from "@/lib/api-client";
import {
  createProjectIntegrationInstanceUiSessionRequest,
  listProjectIntegrationInstancesRequest,
  listProjectIntegrationsStatusRequest,
} from "@/lib/api-client";

interface ProjectSteamPanelProps {
  apiSession: ApiSession;
  projectId: string;
  integrationKey?: string;
  instanceId?: string;
}

function trimTrailingSlash(value: string) {
  return value.endsWith("/") ? value.slice(0, -1) : value;
}

function resolveIframeUrl(baseUrl: string, iframeUrl: string) {
  if (/^https?:\/\//i.test(iframeUrl)) {
    return iframeUrl;
  }

  const normalizedBase = trimTrailingSlash(baseUrl);
  const baseWithoutV1 = normalizedBase.endsWith("/v1")
    ? normalizedBase.slice(0, -3)
    : normalizedBase;
  const normalizedPath = iframeUrl.startsWith("/") ? iframeUrl : `/${iframeUrl}`;
  return `${baseWithoutV1}${normalizedPath}`;
}

function resolveApiOrigin(baseUrl: string) {
  const normalizedBase = trimTrailingSlash(baseUrl);
  const baseWithoutV1 = normalizedBase.endsWith("/v1")
    ? normalizedBase.slice(0, -3)
    : normalizedBase;
  try {
    return new URL(baseWithoutV1).origin;
  } catch {
    return "";
  }
}

function formatDate(value?: string | null) {
  if (!value) {
    return "-";
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return parsed.toLocaleString("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function ProjectSteamPanel({
  apiSession,
  projectId,
  integrationKey = "steam-accounts-manager",
  instanceId,
}: ProjectSteamPanelProps) {
  const [selectedInstanceId, setSelectedInstanceId] = useState(instanceId?.trim() ?? "");
  const [iframeUrl, setIframeUrl] = useState("");
  const [iframeExpiresAtUtc, setIframeExpiresAtUtc] = useState<string | null>(null);
  const [iframeError, setIframeError] = useState("");
  const [iframeHeightPx, setIframeHeightPx] = useState(1600);
  const [isUiSessionLoading, setIsUiSessionLoading] = useState(false);
  const [statusMessage, setStatusMessage] = useState(
    "UI Steam загружается из DDCRM-Steam. Основной CRM только встраивает страницу интеграции.",
  );
  const iframeRef = useRef<HTMLIFrameElement | null>(null);
  const embeddedOrigin = useMemo(() => resolveApiOrigin(apiSession.baseUrl), [apiSession.baseUrl]);

  const integrationStatusQuery = useQuery({
    queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listProjectIntegrationsStatusRequest(apiSession, projectId),
    staleTime: 10_000,
  });

  const steamGrant = useMemo(
    () =>
      (integrationStatusQuery.data?.items ?? []).find(
        (item) => item.integrationKey === integrationKey,
      ) ?? null,
    [integrationStatusQuery.data?.items, integrationKey],
  );

  const steamInstancesQuery = useQuery({
    queryKey: ["steam-instances", apiSession.baseUrl, apiSession.token, projectId, integrationKey],
    queryFn: () => listProjectIntegrationInstancesRequest(apiSession, projectId, integrationKey),
    enabled: steamGrant?.status === "active",
    staleTime: 10_000,
  });

  const steamInstances = useMemo(
    () => steamInstancesQuery.data?.items ?? [],
    [steamInstancesQuery.data?.items],
  );

  useEffect(() => {
    if (steamInstances.length === 0) {
      if (selectedInstanceId) {
        setSelectedInstanceId("");
      }
      return;
    }

    if (selectedInstanceId && steamInstances.some((item) => item.instanceId === selectedInstanceId)) {
      return;
    }

    const preferredInstanceId = instanceId?.trim() ?? "";
    if (preferredInstanceId && steamInstances.some((item) => item.instanceId === preferredInstanceId)) {
      setSelectedInstanceId(preferredInstanceId);
      return;
    }

    const fallbackInstance = steamInstances.find((item) => item.isDefault) ?? steamInstances[0];
    setSelectedInstanceId(fallbackInstance.instanceId);
  }, [instanceId, selectedInstanceId, steamInstances]);

  const selectedInstance = useMemo(
    () => steamInstances.find((item) => item.instanceId === selectedInstanceId) ?? null,
    [selectedInstanceId, steamInstances],
  );

  const requestUiSession = useCallback(async () => {
    if (!selectedInstanceId) {
      setIframeUrl("");
      setIframeExpiresAtUtc(null);
      return;
    }

    try {
      setIsUiSessionLoading(true);
      const session = await createProjectIntegrationInstanceUiSessionRequest(
        apiSession,
        projectId,
        integrationKey,
        selectedInstanceId,
      );
      setIframeUrl(resolveIframeUrl(apiSession.baseUrl, session.iframeUrl));
      setIframeHeightPx(1600);
      setIframeExpiresAtUtc(session.expiresAtUtc);
      setIframeError("");
      setStatusMessage("Сессия встроенного UI обновлена.");
    } catch (error) {
      setIframeUrl("");
      setIframeExpiresAtUtc(null);
      setIframeError(error instanceof Error ? error.message : "Не удалось получить iframe-сессию Steam.");
    } finally {
      setIsUiSessionLoading(false);
    }
  }, [apiSession, integrationKey, projectId, selectedInstanceId]);

  useEffect(() => {
    void requestUiSession();
  }, [requestUiSession]);

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      if (!iframeRef.current?.contentWindow || event.source !== iframeRef.current.contentWindow) {
        return;
      }

      if (embeddedOrigin && event.origin !== embeddedOrigin) {
        return;
      }

      if (!event.data || typeof event.data !== "object") {
        return;
      }

      const payload = event.data as { type?: unknown; height?: unknown };
      if (payload.type !== "ddcrm.steam.embedded.height") {
        return;
      }

      if (typeof payload.height !== "number" || !Number.isFinite(payload.height)) {
        return;
      }

      const nextHeight = Math.max(720, Math.min(8000, Math.ceil(payload.height) + 8));
      setIframeHeightPx(nextHeight);
    };

    window.addEventListener("message", onMessage);
    return () => {
      window.removeEventListener("message", onMessage);
    };
  }, [embeddedOrigin]);

  return (
    <div className="page-stack" data-testid="project-steam-panel">
      <header className="page-section-header">
        <h2>Steam</h2>
        <p>
          Интерфейс загружается из сервиса интеграции через iframe-сессию.
          CRM не рендерит доменные Steam-формы локально.
        </p>
      </header>

      <div className="panel-actions">
        <button
          type="button"
          className="button button-ghost"
          disabled={integrationStatusQuery.isFetching || steamInstancesQuery.isFetching}
          onClick={() => {
            void Promise.all([integrationStatusQuery.refetch(), steamInstancesQuery.refetch()]);
          }}
        >
          Обновить статус
        </button>
      </div>

      {integrationStatusQuery.isPending ? <p className="route-hint">Загружаем статус интеграции...</p> : null}
      {integrationStatusQuery.error ? (
        <p className="route-error">
          {integrationStatusQuery.error instanceof Error
            ? integrationStatusQuery.error.message
            : "Не удалось загрузить статус интеграций."}
        </p>
      ) : null}

      {steamGrant ? (
        <div className="entity-pills">
          <span className="entity-pill">grant: {steamGrant.status}</span>
          <span className="entity-pill">runtime: {steamGrant.runtimeStatus ?? "n/a"}</span>
          <span className="entity-pill">scopes: {steamGrant.scopes.join(", ") || "n/a"}</span>
          {steamGrant.runtimeAccountId ? (
            <span className="entity-pill">rk.{steamGrant.runtimeAccountId.replaceAll("-", "")}</span>
          ) : null}
        </div>
      ) : (
        <p className="route-hint">
          Для проекта не найден grant `{integrationKey}`. Выдайте интеграцию в админке.
        </p>
      )}

      <section className="glass-card page-stack">
        <div className="panel-title-row">
          <h3>Embedded UI</h3>
          <div className="inline-actions">
            <button
              type="button"
              className="button button-ghost"
              disabled={isUiSessionLoading || !selectedInstanceId}
              onClick={() => {
                void requestUiSession();
              }}
            >
              Обновить сессию UI
            </button>
            {iframeUrl ? (
              <a className="button button-ghost" href={iframeUrl} target="_blank" rel="noreferrer">
                Открыть в новой вкладке
              </a>
            ) : null}
          </div>
        </div>

        <p className="route-hint">{statusMessage}</p>

        {steamGrant?.status !== "active" ? (
          <p className="route-hint">
            Steam grant должен быть активным перед открытием интерфейса.
          </p>
        ) : null}

        {steamInstancesQuery.error ? (
          <p className="route-error">
            {steamInstancesQuery.error instanceof Error
              ? steamInstancesQuery.error.message
              : "Не удалось загрузить Steam instances."}
          </p>
        ) : null}
        {steamInstancesQuery.isPending ? <p className="route-hint">Загрузка Steam instances...</p> : null}

        {steamInstances.length > 0 ? (
          <label className="field">
            <span>Instance</span>
            <select
              className="input"
              value={selectedInstanceId}
              onChange={(event) => setSelectedInstanceId(event.target.value)}
            >
              {steamInstances.map((item) => (
                <option key={item.instanceId} value={item.instanceId}>
                  {item.displayName} {item.isDefault ? "(default)" : ""}
                </option>
              ))}
            </select>
          </label>
        ) : null}

        {selectedInstance ? (
          <div className="entity-pills">
            <span className="entity-pill">{selectedInstance.isDefault ? "default" : "instance"}</span>
            <span className="entity-pill">runtime: {selectedInstance.runtimeStatus}</span>
            <span className="entity-pill">rk.{selectedInstance.runtimeAccountId.replaceAll("-", "")}</span>
            {selectedInstance.runtimeLastError ? <span className="entity-pill is-pill-danger">error</span> : null}
          </div>
        ) : null}
        {selectedInstance?.runtimeLastError ? (
          <p className="route-error">{selectedInstance.runtimeLastError}</p>
        ) : null}

        {iframeExpiresAtUtc ? (
          <p className="route-hint">Iframe-сессия действует до: {formatDate(iframeExpiresAtUtc)}</p>
        ) : null}
        {iframeError ? <p className="route-error">{iframeError}</p> : null}

        {iframeUrl ? (
          <iframe
            ref={iframeRef}
            src={iframeUrl}
            title="Steam integration UI"
            scrolling="no"
            style={{
              display: "block",
              width: "100%",
              height: `${iframeHeightPx}px`,
              border: "1px solid rgba(255,255,255,0.12)",
              borderRadius: "16px",
              background: "#05070f",
            }}
          />
        ) : (
          <p className="route-hint">
            Не удалось открыть embedded UI. Проверьте runtime instance и повторите попытку.
          </p>
        )}
      </section>

      <p className="route-hint">
        Управление интеграциями доступно во вкладке{" "}
        <Link href={`/projects/${projectId}/integrations`}>Integrations</Link>.
      </p>
    </div>
  );
}
