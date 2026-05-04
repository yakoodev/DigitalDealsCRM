"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AdminLayout } from "@/components/layout/admin-layout";
import {
  listAdminProjectIntegrationGrantsRequest,
  listAdminTelegramProxyProfilesRequest,
  revokeAdminProjectIntegrationGrantRequest,
  upsertAdminProjectIntegrationGrantRequest,
  upsertAdminTelegramProxyProfileRequest,
  type ApiSession,
} from "@/lib/api-client";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function AccountManagerIntegrationsPage() {
  const queryClient = useQueryClient();
  const { session, logout } = useSessionGuard();

  const [projectId, setProjectId] = useState("");
  const [integrationKey, setIntegrationKey] = useState("platform.funpay");
  const [scopes, setScopes] = useState("use");
  const [grantStatus, setGrantStatus] = useState("Выдайте grant проекту и настройте Telegram proxy.");

  const [proxyId, setProxyId] = useState(() =>
    typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
      ? crypto.randomUUID()
      : "00000000-0000-0000-0000-000000000000",
  );
  const [proxyName, setProxyName] = useState("tg-proxy-1");
  const [proxyType, setProxyType] = useState<"http" | "https" | "socks5">("http");
  const [proxyHost, setProxyHost] = useState("127.0.0.1");
  const [proxyPort, setProxyPort] = useState("8080");
  const [proxySetActive, setProxySetActive] = useState(true);
  const [proxyClearCredentials, setProxyClearCredentials] = useState(false);
  const [proxyLogin, setProxyLogin] = useState("");
  const [proxyPassword, setProxyPassword] = useState("");
  const [proxyStatus, setProxyStatus] = useState("Telegram proxy-профили управляются только системным админом.");

  const apiSession = useMemo<ApiSession>(
    () => ({
      token: session?.token ?? "",
      baseUrl: session?.baseUrl ?? "",
    }),
    [session?.baseUrl, session?.token],
  );

  const grantsQuery = useQuery({
    queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listAdminProjectIntegrationGrantsRequest(apiSession, projectId.trim()),
    enabled: Boolean(session?.profile.isSystemAdmin && projectId.trim()),
  });

  const proxiesQuery = useQuery({
    queryKey: ["admin-telegram-proxies", apiSession.baseUrl, apiSession.token],
    queryFn: () => listAdminTelegramProxyProfilesRequest(apiSession),
    enabled: Boolean(session?.profile.isSystemAdmin),
  });

  const grantMutation = useMutation({
    mutationFn: () => upsertAdminProjectIntegrationGrantRequest(apiSession, projectId.trim(), integrationKey.trim(), {
      scopes: scopes
        .split(",")
        .map((value) => value.trim())
        .filter(Boolean),
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token, projectId],
      });
      setGrantStatus("Grant сохранён.");
    },
    onError: (error) => {
      setGrantStatus(error instanceof Error ? error.message : "Не удалось сохранить grant.");
    },
  });

  const revokeMutation = useMutation({
    mutationFn: () => revokeAdminProjectIntegrationGrantRequest(apiSession, projectId.trim(), integrationKey.trim()),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-integration-grants", apiSession.baseUrl, apiSession.token, projectId],
      });
      setGrantStatus("Grant отозван.");
    },
    onError: (error) => {
      setGrantStatus(error instanceof Error ? error.message : "Не удалось отозвать grant.");
    },
  });

  const proxyMutation = useMutation({
    mutationFn: () => upsertAdminTelegramProxyProfileRequest(apiSession, proxyId.trim(), {
      name: proxyName.trim(),
      proxyType,
      host: proxyHost.trim(),
      port: Number(proxyPort),
      setActive: proxySetActive,
      clearCredentials: proxyClearCredentials,
      login: proxyLogin.trim() || undefined,
      password: proxyPassword.trim() || undefined,
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-telegram-proxies", apiSession.baseUrl, apiSession.token],
      });
      setProxyPassword("");
      setProxyStatus("Telegram proxy-профиль сохранён.");
    },
    onError: (error) => {
      setProxyStatus(error instanceof Error ? error.message : "Не удалось сохранить proxy-профиль.");
    },
  });

  if (!session) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>Проверяем сессию...</h1>
        </section>
      </main>
    );
  }

  if (!session.profile.isSystemAdmin) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>403 · System admin required</h1>
          <p className="route-error">Недостаточно системных прав для управления integration bus.</p>
        </section>
      </main>
    );
  }

  const grantItems = grantsQuery.data ?? [];
  const proxyItems = proxiesQuery.data ?? [];

  return (
    <AdminLayout session={session} activeTab="integrations" onLogout={logout}>
      <section className="module-board">
        <section className="module-main-column">
          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Project grants</h3>
            </div>
            <label className="field">
              <span>Project ID</span>
              <input
                className="input"
                value={projectId}
                onChange={(event) => setProjectId(event.target.value)}
                placeholder="uuid проекта"
              />
            </label>
            {grantsQuery.isPending ? <p className="route-hint">Загрузка grant-ов...</p> : null}
            {grantsQuery.error ? (
              <p className="route-error">
                {grantsQuery.error instanceof Error ? grantsQuery.error.message : "Не удалось загрузить grant-ы."}
              </p>
            ) : null}
            {!grantsQuery.isPending && !grantsQuery.error && grantItems.length === 0 ? (
              <p className="route-hint">Для выбранного проекта grant-ы пока не выданы.</p>
            ) : (
              <ul className="entity-list">
                {grantItems.map((item) => (
                  <li key={item.integrationKey} className="entity-list-item">
                    <div>
                      <strong>{item.integrationKey}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{item.status}</span>
                        <span className="entity-pill">{item.scopes.join(", ")}</span>
                        <span className="entity-pill">{item.credentialStatus ?? "no-credential"}</span>
                        <span className="entity-pill">{item.credentialMasked ?? "write-only"}</span>
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </article>

          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Telegram proxy profiles</h3>
            </div>
            {proxiesQuery.isPending ? <p className="route-hint">Загрузка proxy-профилей...</p> : null}
            {proxiesQuery.error ? (
              <p className="route-error">
                {proxiesQuery.error instanceof Error ? proxiesQuery.error.message : "Не удалось загрузить proxy-профили."}
              </p>
            ) : null}
            {!proxiesQuery.isPending && !proxiesQuery.error && proxyItems.length === 0 ? (
              <p className="route-hint">Список proxy-профилей пуст.</p>
            ) : (
              <ul className="entity-list">
                {proxyItems.map((item) => (
                  <li key={item.id} className="entity-list-item">
                    <div>
                      <strong>{item.name}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{item.proxyType}</span>
                        <span className="entity-pill">{item.host}:{item.port}</span>
                        <span className="entity-pill">{item.isActive ? "active" : "inactive"}</span>
                        <span className="entity-pill">{item.hasCredentials ? "credentials:on" : "credentials:off"}</span>
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </article>
        </section>

        <aside className="module-side-column">
          <article className="glass-card page-stack panel-card-sticky">
            <div className="panel-title-row">
              <h3>Grant upsert/revoke</h3>
            </div>
            <label className="field">
              <span>Integration key</span>
              <input className="input" value={integrationKey} onChange={(event) => setIntegrationKey(event.target.value)} />
            </label>
            <label className="field">
              <span>Scopes (csv)</span>
              <input className="input" value={scopes} onChange={(event) => setScopes(event.target.value)} />
            </label>
            <div className="hero-actions">
              <button
                type="button"
                className="button button-primary"
                disabled={grantMutation.isPending || !projectId.trim() || !integrationKey.trim()}
                onClick={() => grantMutation.mutate()}
              >
                Выдать/обновить
              </button>
              <button
                type="button"
                className="button button-ghost"
                disabled={revokeMutation.isPending || !projectId.trim() || !integrationKey.trim()}
                onClick={() => revokeMutation.mutate()}
              >
                Отозвать
              </button>
            </div>
            <p className="route-hint">{grantStatus}</p>

            <div className="panel-title-row">
              <h3>Telegram proxy upsert</h3>
            </div>
            <label className="field">
              <span>Proxy ID (uuid)</span>
              <input className="input" value={proxyId} onChange={(event) => setProxyId(event.target.value)} />
            </label>
            <label className="field">
              <span>Name</span>
              <input className="input" value={proxyName} onChange={(event) => setProxyName(event.target.value)} />
            </label>
            <div className="grid-2">
              <label className="field">
                <span>Type</span>
                <select
                  className="input"
                  value={proxyType}
                  onChange={(event) => setProxyType(event.target.value as "http" | "https" | "socks5")}
                >
                  <option value="http">http</option>
                  <option value="https">https</option>
                  <option value="socks5">socks5</option>
                </select>
              </label>
              <label className="field">
                <span>Port</span>
                <input className="input" value={proxyPort} onChange={(event) => setProxyPort(event.target.value)} />
              </label>
            </div>
            <label className="field">
              <span>Host</span>
              <input className="input" value={proxyHost} onChange={(event) => setProxyHost(event.target.value)} />
            </label>
            <label className="field">
              <span>Login (optional)</span>
              <input className="input" value={proxyLogin} onChange={(event) => setProxyLogin(event.target.value)} />
            </label>
            <label className="field">
              <span>Password (write-only)</span>
              <input
                className="input"
                type="password"
                value={proxyPassword}
                onChange={(event) => setProxyPassword(event.target.value)}
              />
            </label>
            <label className="field field-inline">
              <span>Set active</span>
              <input type="checkbox" checked={proxySetActive} onChange={(event) => setProxySetActive(event.target.checked)} />
            </label>
            <label className="field field-inline">
              <span>Clear credentials</span>
              <input
                type="checkbox"
                checked={proxyClearCredentials}
                onChange={(event) => setProxyClearCredentials(event.target.checked)}
              />
            </label>
            <button
              type="button"
              className="button button-primary"
              disabled={proxyMutation.isPending || !proxyId.trim() || !proxyName.trim() || !proxyHost.trim()}
              onClick={() => proxyMutation.mutate()}
            >
              Сохранить proxy
            </button>
            <p className="route-hint">{proxyStatus}</p>
          </article>
        </aside>
      </section>
    </AdminLayout>
  );
}
