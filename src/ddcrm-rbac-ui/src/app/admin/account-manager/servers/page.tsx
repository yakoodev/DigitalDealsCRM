"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AdminLayout } from "@/components/layout/admin-layout";
import { RouteModalHost } from "@/components/layout/route-modal-host";
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
  const [serverSearch, setServerSearch] = useState("");
  const [serverStatusFilter, setServerStatusFilter] = useState("all");
  const [serverHealthFilter, setServerHealthFilter] = useState("all");
  const [selectedServerId, setSelectedServerId] = useState("");
  const [serverModalOpen, setServerModalOpen] = useState(false);

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
  const filteredItems = items.filter((server) => {
    const query = serverSearch.trim().toLowerCase();
    const statusToken = serverStatusFilter.trim().toLowerCase();
    const healthToken = serverHealthFilter.trim().toLowerCase();

    if (statusToken !== "all" && server.status.toLowerCase() !== statusToken) {
      return false;
    }

    if (healthToken !== "all" && server.health.toLowerCase() !== healthToken) {
      return false;
    }

    if (!query) {
      return true;
    }

    const haystack = [
      server.serverId,
      server.baseUrlTemplate,
      server.dockerHost ?? "",
      server.dockerNetwork ?? "",
    ].join(" ").toLowerCase();

    return haystack.includes(query);
  });

  const effectiveSelectedServerId = filteredItems.some((server) => server.serverId === selectedServerId)
    ? selectedServerId
    : (filteredItems[0]?.serverId ?? "");
  const selectedServer = filteredItems.find((server) => server.serverId === effectiveSelectedServerId) ?? null;

  const applyServerToForm = (server: AdminWorkerServer) => {
    setSelectedServerId(server.serverId);
    setServerId(server.serverId);
    setBaseUrlTemplate(server.baseUrlTemplate);
    setStatusValue(server.status);
    setHealthValue(server.health);
    setCapacity(String(server.capacity));
    setCurrentLoad(String(server.currentLoad));
    setDockerHost(server.dockerHost ?? "");
    setDockerNetwork(server.dockerNetwork ?? "");
    setRegistryEnabled(server.registry.enabled);
    setRegistryUsername(server.registry.username ?? "");
    setRegistryToken("");
    setClearRegistryToken(false);
    setStatus(`Сервер ${server.serverId} загружен в форму.`);
    setServerModalOpen(true);
  };

  const resetFormForNewServer = () => {
    setServerId("srv-new");
    setBaseUrlTemplate("http://{workerId}:{workerPort}");
    setStatusValue("active");
    setHealthValue("healthy");
    setCapacity("100");
    setCurrentLoad("0");
    setDockerHost("unix:///var/run/docker.sock");
    setDockerNetwork("ddcrm_ddcrm");
    setRegistryEnabled(true);
    setRegistryUsername("");
    setRegistryToken("");
    setClearRegistryToken(false);
    setStatus("Форма очищена для нового server.");
    setServerModalOpen(true);
  };

  return (
    <AdminLayout session={session} activeTab="servers" onLogout={logout}>
      <div className="page-stack">
        <section className="page-head">
          <div className="page-head__row">
            <div>
              <h1 className="page-title">Worker servers</h1>
              <p className="route-hint">Placement/autospawn пулы, health и registry-конфигурация.</p>
            </div>
            <div className="inline">
              <button
                type="button"
                className="button button-ghost button-small"
                disabled={serversQuery.isFetching}
                onClick={() => serversQuery.refetch()}
              >
                Обновить
              </button>
              <button
                type="button"
                className="button button-primary button-small"
                onClick={resetFormForNewServer}
              >
                ＋ Добавить сервер
              </button>
            </div>
          </div>
          <div className={`status ${status.toLowerCase().includes("не удалось") ? "status--bad" : "status--info"}`}>
            {status}
          </div>
        </section>

        <section className="state-grid">
          <article className="state-card">
            <span className="label">Серверов</span>
            <strong>{items.length}</strong>
          </article>
          <article className="state-card">
            <span className="label">Capacity</span>
            <strong>{items.reduce((sum, item) => sum + item.capacity, 0)}</strong>
          </article>
          <article className="state-card">
            <span className="label">Текущий load</span>
            <strong>{items.reduce((sum, item) => sum + item.currentLoad, 0)}</strong>
          </article>
          <article className="state-card">
            <span className="label">Требуют внимания</span>
            <strong>{items.filter((item) => item.health !== "healthy").length}</strong>
          </article>
        </section>

        <section className="card">
          <div className="server-toolbar">
            <label className="field">
              <span>Поиск</span>
              <input
                className="input"
                value={serverSearch}
                onChange={(event) => setServerSearch(event.target.value)}
                placeholder="server id, host, network"
              />
            </label>
            <label className="field">
              <span>Статус</span>
              <select
                className="input"
                value={serverStatusFilter}
                onChange={(event) => setServerStatusFilter(event.target.value)}
              >
                <option value="all">Все статусы</option>
                <option value="active">active</option>
                <option value="draining">draining</option>
                <option value="inactive">inactive</option>
              </select>
            </label>
            <label className="field">
              <span>Health</span>
              <select
                className="input"
                value={serverHealthFilter}
                onChange={(event) => setServerHealthFilter(event.target.value)}
              >
                <option value="all">Любой</option>
                <option value="healthy">healthy</option>
                <option value="degraded">degraded</option>
                <option value="unhealthy">unhealthy</option>
              </select>
            </label>
            <button
              type="button"
              className="button button-small"
              onClick={() => setStatus("Фильтры применены.")}
            >
              Применить
            </button>
          </div>
        </section>

        <section className="server-layout">
          <div className="server-list">
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
            ) : null}
            {!serversQuery.isPending && !serversQuery.error && items.length > 0 && filteredItems.length === 0 ? (
              <p className="route-hint">По фильтру не найдено ни одного server.</p>
            ) : null}
            {filteredItems.map((server) => (
              <article
                key={server.serverId}
                className={`server-card ${selectedServer?.serverId === server.serverId ? "is-active" : ""}`}
              >
                <div className="server-card__head">
                  <div>
                    <h2 className="server-title">{server.serverId}</h2>
                    <div className="server-meta">{server.baseUrlTemplate}</div>
                  </div>
                  <div className="chip-row">
                    <span className="status status--info">{server.status}</span>
                    <span className={`status ${server.health === "healthy" ? "status--ok" : "status--warn"}`}>
                      {server.health}
                    </span>
                  </div>
                </div>
                <div className="server-stats">
                  <div className="stat-box"><b>{server.capacity}</b><span>capacity</span></div>
                  <div className="stat-box"><b>{server.currentLoad}</b><span>load</span></div>
                  <div className="stat-box"><b>{server.dockerNetwork || "-"}</b><span>network</span></div>
                  <div className="stat-box"><b>{server.registry.enabled ? "on" : "off"}</b><span>registry</span></div>
                </div>
                <div className="row-split">
                  <div className="chip-row">
                    <span className="chip">
                      {server.registry.hasToken ? "token configured" : "token missing"}
                    </span>
                    <span className="chip">
                      {server.registry.username ? `user: ${server.registry.username}` : "user: n/a"}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="button button-ghost button-small"
                    onClick={() => applyServerToForm(server)}
                  >
                    Изменить
                  </button>
                </div>
              </article>
            ))}
          </div>

          <aside className="detail-panel">
            <section className="details-card">
              {selectedServer ? (
                <>
                  <div className="details-head">
                    <div>
                      <h2 className="card__title">{selectedServer.serverId}</h2>
                      <div className="route-hint">Информация по выбранному серверу</div>
                    </div>
                    <span className={`status ${selectedServer.health === "healthy" ? "status--ok" : "status--warn"}`}>
                      {selectedServer.health}
                    </span>
                  </div>
                  <div className="summary-list">
                    <div className="summary-line"><span>Статус</span><strong>{selectedServer.status}</strong></div>
                    <div className="summary-line"><span>Base URL</span><strong>{selectedServer.baseUrlTemplate}</strong></div>
                    <div className="summary-line"><span>Capacity</span><strong>{selectedServer.capacity}</strong></div>
                    <div className="summary-line"><span>Current load</span><strong>{selectedServer.currentLoad}</strong></div>
                    <div className="summary-line"><span>Docker network</span><strong>{selectedServer.dockerNetwork || "-"}</strong></div>
                    <div className="summary-line"><span>Registry</span><strong>{selectedServer.registry.host}</strong></div>
                  </div>
                  <div className="inline">
                    <button
                      type="button"
                      className="button button-primary button-small"
                      onClick={() => applyServerToForm(selectedServer)}
                    >
                      Изменить
                    </button>
                  </div>
                </>
              ) : (
                <p className="route-hint">Выберите server из списка.</p>
              )}
            </section>
            <section className="details-card">
              <h3>Конфигурация</h3>
              <p className="route-hint">
                Создание и редактирование server вынесено в модальное окно для компактного layout.
              </p>
              <div className="inline">
                <button type="button" className="button button-primary button-small" onClick={resetFormForNewServer}>
                  Добавить сервер
                </button>
                <button
                  type="button"
                  className="button button-ghost button-small"
                  disabled={!selectedServer}
                  onClick={() => {
                    if (selectedServer) {
                      applyServerToForm(selectedServer);
                    }
                  }}
                >
                  Редактировать выбранный
                </button>
              </div>
            </section>
          </aside>
        </section>
      </div>

      <RouteModalHost
        isOpen={serverModalOpen}
        title="Server form"
        description="Создание/редактирование worker server и registry-конфигурации."
        onClose={() => setServerModalOpen(false)}
      >
        <section className="page-stack">
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
          <div className="inline">
            <button
              type="button"
              className="button button-primary"
              disabled={upsertMutation.isPending || !serverId.trim() || !baseUrlTemplate.trim()}
              onClick={async () => {
                await upsertMutation.mutateAsync();
                setServerModalOpen(false);
              }}
            >
              Сохранить
            </button>
            <button type="button" className="button button-ghost" onClick={() => setServerModalOpen(false)}>
              Отмена
            </button>
          </div>
        </section>
      </RouteModalHost>
    </AdminLayout>
  );
}
