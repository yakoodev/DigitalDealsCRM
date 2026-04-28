"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AdminLayout } from "@/components/layout/admin-layout";
import {
  listAdminWorkerServersRequest,
  upsertAdminWorkerServerRequest,
  type AdminWorkerServer,
  type ApiSession,
} from "@/lib/api-client";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function AccountManagerServersPage() {
  const queryClient = useQueryClient();
  const { session, logout } = useSessionGuard();
  const [status, setStatus] = useState(
    "Настройте worker servers для placement/autospawn и GHCR pull-if-missing.",
  );
  const [serverId, setServerId] = useState("srv-default");
  const [baseUrlTemplate, setBaseUrlTemplate] = useState("http://{workerId}:{workerPort}");
  const [capacity, setCapacity] = useState("100");
  const [currentLoad, setCurrentLoad] = useState("0");
  const [dockerHost, setDockerHost] = useState("unix:///var/run/docker.sock");
  const [dockerNetwork, setDockerNetwork] = useState("ddcrm_ddcrm");
  const [registryEnabled, setRegistryEnabled] = useState(true);
  const [registryHost] = useState("ghcr.io");
  const [registryUsername, setRegistryUsername] = useState("");
  const [registryToken, setRegistryToken] = useState("");
  const [clearRegistryToken, setClearRegistryToken] = useState(false);
  const [statusValue, setStatusValue] = useState<AdminWorkerServer["status"]>("active");
  const [healthValue, setHealthValue] = useState<AdminWorkerServer["health"]>("healthy");

  const apiSession = useMemo<ApiSession>(
    () => ({
      token: session?.token ?? "",
      baseUrl: session?.baseUrl ?? "",
    }),
    [session?.baseUrl, session?.token],
  );

  const serversQuery = useQuery({
    queryKey: ["admin-worker-servers", apiSession.baseUrl, apiSession.token],
    queryFn: () => listAdminWorkerServersRequest(apiSession),
    enabled: Boolean(session?.profile.isSystemAdmin),
  });

  const upsertMutation = useMutation({
    mutationFn: async () =>
      upsertAdminWorkerServerRequest(apiSession, serverId.trim(), {
        baseUrlTemplate: baseUrlTemplate.trim(),
        status: statusValue,
        health: healthValue,
        capacity: Number(capacity),
        currentLoad: Number(currentLoad),
        dockerHost: dockerHost.trim() || undefined,
        dockerNetwork: dockerNetwork.trim() || undefined,
        registry: {
          enabled: registryEnabled,
          host: registryHost,
          username: registryUsername.trim() || undefined,
          token: registryToken.trim() || undefined,
          clearToken: clearRegistryToken || undefined,
        },
        metadata: {},
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-worker-servers", apiSession.baseUrl, apiSession.token],
      });
      setRegistryToken("");
      setClearRegistryToken(false);
      setStatus("Worker server сохранён.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось сохранить worker server.");
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
          <p className="route-error">Недостаточно системных прав для управления worker servers.</p>
        </section>
      </main>
    );
  }

  const items = serversQuery.data ?? [];

  return (
    <AdminLayout session={session} activeTab="servers" onLogout={logout}>
      <section className="module-board">
        <section className="module-main-column">
          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Worker servers</h3>
            </div>
            {serversQuery.isPending ? <p className="route-hint">Загрузка...</p> : null}
            {serversQuery.error ? (
              <p className="route-error">
                {serversQuery.error instanceof Error
                  ? serversQuery.error.message
                  : "Не удалось получить список server-ов."}
              </p>
            ) : null}
            {!serversQuery.isPending && !serversQuery.error && items.length === 0 ? (
              <p className="route-hint">Список пуст. Добавьте первый server через форму справа.</p>
            ) : (
              <ul className="entity-list">
                {items.map((server) => (
                  <li key={server.serverId} className="entity-list-item">
                    <div>
                      <strong>{server.serverId}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{server.status}</span>
                        <span className="entity-pill">{server.health}</span>
                        <span className="entity-pill">
                          load {server.currentLoad}/{server.capacity}
                        </span>
                        <span className="entity-pill">
                          {server.registry.enabled ? "registry:on" : "registry:off"}
                        </span>
                        <span className="entity-pill">
                          {server.registry.hasToken ? "token configured" : "token missing"}
                        </span>
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
              <h3>Upsert server</h3>
            </div>
            <label className="field">
              <span>Server ID</span>
              <input className="input" value={serverId} onChange={(event) => setServerId(event.target.value)} />
            </label>
            <label className="field">
              <span>Base URL template</span>
              <input
                className="input"
                value={baseUrlTemplate}
                onChange={(event) => setBaseUrlTemplate(event.target.value)}
              />
            </label>
            <div className="grid-2">
              <label className="field">
                <span>Status</span>
                <select
                  className="input"
                  value={statusValue}
                  onChange={(event) => setStatusValue(event.target.value as AdminWorkerServer["status"])}
                >
                  <option value="active">active</option>
                  <option value="draining">draining</option>
                  <option value="inactive">inactive</option>
                </select>
              </label>
              <label className="field">
                <span>Health</span>
                <select
                  className="input"
                  value={healthValue}
                  onChange={(event) => setHealthValue(event.target.value as AdminWorkerServer["health"])}
                >
                  <option value="healthy">healthy</option>
                  <option value="degraded">degraded</option>
                  <option value="unhealthy">unhealthy</option>
                </select>
              </label>
            </div>
            <div className="grid-2">
              <label className="field">
                <span>Capacity</span>
                <input className="input" value={capacity} onChange={(event) => setCapacity(event.target.value)} />
              </label>
              <label className="field">
                <span>Current load</span>
                <input className="input" value={currentLoad} onChange={(event) => setCurrentLoad(event.target.value)} />
              </label>
            </div>
            <label className="field">
              <span>Docker host</span>
              <input className="input" value={dockerHost} onChange={(event) => setDockerHost(event.target.value)} />
            </label>
            <label className="field">
              <span>Docker network</span>
              <input className="input" value={dockerNetwork} onChange={(event) => setDockerNetwork(event.target.value)} />
            </label>
            <div className="panel-title-row">
              <h3>Registry (GHCR)</h3>
            </div>
            <label className="field field-inline">
              <span>Enabled</span>
              <input
                type="checkbox"
                checked={registryEnabled}
                onChange={(event) => setRegistryEnabled(event.target.checked)}
              />
            </label>
            <label className="field">
              <span>Host</span>
              <input className="input" value={registryHost} readOnly />
            </label>
            <label className="field">
              <span>Username</span>
              <input
                className="input"
                value={registryUsername}
                onChange={(event) => setRegistryUsername(event.target.value)}
                placeholder="github-user"
              />
            </label>
            <label className="field">
              <span>Token (write-only)</span>
              <input
                className="input"
                type="password"
                value={registryToken}
                onChange={(event) => setRegistryToken(event.target.value)}
                placeholder="ghp_***"
              />
            </label>
            <label className="field field-inline">
              <span>Clear token</span>
              <input
                type="checkbox"
                checked={clearRegistryToken}
                onChange={(event) => setClearRegistryToken(event.target.checked)}
              />
            </label>
            <button
              type="button"
              className="button button-primary"
              disabled={upsertMutation.isPending || !serverId.trim() || !baseUrlTemplate.trim()}
              onClick={() => upsertMutation.mutate()}
            >
              Сохранить
            </button>
            <p className="route-hint">{status}</p>
          </article>
        </aside>
      </section>
    </AdminLayout>
  );
}
