"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AdminLayout } from "@/components/layout/admin-layout";
import {
  listAdminAccountTypesRequest,
  upsertAdminAccountTypeRequest,
  type ApiSession,
} from "@/lib/api-client";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function AccountManagerTemplatesPage() {
  const queryClient = useQueryClient();
  const { session, logout } = useSessionGuard();
  const [status, setStatus] = useState(
    "Обновите runtime профиль платформы. Порт worker управляется AccountManager автоматически.",
  );
  const [accountTypeId, setAccountTypeId] = useState("test-worker.funpay");
  const [platform, setPlatform] = useState("funpay");
  const [displayName, setDisplayName] = useState("Тестовый worker: FunPay");
  const [enabled, setEnabled] = useState(true);
  const [workerImage, setWorkerImage] = useState("ddcrm/worker-api:local");
  const [pathPrefix, setPathPrefix] = useState("/internal/v2/worker");
  const [healthPath, setHealthPath] = useState("/health");

  const apiSession = useMemo<ApiSession>(
    () => ({
      token: session?.token ?? "",
      baseUrl: session?.baseUrl ?? "",
    }),
    [session?.baseUrl, session?.token],
  );

  const templatesQuery = useQuery({
    queryKey: ["admin-account-types", apiSession.baseUrl, apiSession.token],
    queryFn: () => listAdminAccountTypesRequest(apiSession),
    enabled: Boolean(session?.profile.isSystemAdmin),
  });

  const upsertMutation = useMutation({
    mutationFn: async () =>
      upsertAdminAccountTypeRequest(apiSession, accountTypeId.trim(), {
        platform: platform.trim(),
        displayName: displayName.trim(),
        workerProfileId: "test-worker",
        enabled,
        sortOrder: 10,
        formFields: [
          {
            key: "displayName",
            label: "Название аккаунта",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "Например, FunPay Test Account",
            defaultValue: "FunPay Test Account",
          },
          {
            key: "proxyHost",
            label: "Proxy host",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "45.88.208.237",
          },
          {
            key: "proxyPort",
            label: "Proxy port",
            inputType: "number",
            required: true,
            secret: false,
            placeholder: "1508",
            defaultValue: "1508",
          },
          {
            key: "proxyLogin",
            label: "Proxy login",
            inputType: "text",
            required: true,
            secret: false,
            placeholder: "user305829",
          },
          {
            key: "proxyPassword",
            label: "Proxy password",
            inputType: "password",
            required: true,
            secret: true,
            placeholder: "Введите пароль",
          },
        ],
        runtime: {
          autospawnEnabled: true,
          workerImage: workerImage.trim(),
          workerPathPrefix: pathPrefix.trim(),
          healthPath: healthPath.trim(),
          containerPort: 0,
          environmentVariables: {
            TEST_WORKER_PROVIDER: platform.trim(),
          },
        },
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["admin-account-types", apiSession.baseUrl, apiSession.token],
      });
      setStatus("Template сохранён.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось сохранить template.");
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
          <p className="route-error">Недостаточно системных прав для управления platform templates.</p>
        </section>
      </main>
    );
  }

  const items = templatesQuery.data ?? [];

  return (
    <AdminLayout session={session} activeTab="templates" onLogout={logout}>
      <section className="module-board">
        <section className="module-main-column">
          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Platform templates</h3>
            </div>
            {templatesQuery.isPending ? <p className="route-hint">Загрузка...</p> : null}
            {templatesQuery.error ? (
              <p className="route-error">
                {templatesQuery.error instanceof Error
                  ? templatesQuery.error.message
                  : "Не удалось получить templates."}
              </p>
            ) : null}
            {!templatesQuery.isPending && !templatesQuery.error && items.length === 0 ? (
              <p className="route-hint">Каталог пуст. Добавьте template через форму справа.</p>
            ) : (
              <ul className="entity-list">
                {items.map((item) => (
                  <li key={item.accountTypeId} className="entity-list-item">
                    <div>
                      <strong>{item.displayName}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{item.platform}</span>
                        <span className="entity-pill">{item.enabled ? "enabled" : "disabled"}</span>
                        <span className="entity-pill">{item.runtime.workerImage}</span>
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
              <h3>Upsert template</h3>
            </div>
            <label className="field">
              <span>Account type ID</span>
              <input
                className="input"
                value={accountTypeId}
                onChange={(event) => setAccountTypeId(event.target.value)}
              />
            </label>
            <div className="grid-2">
              <label className="field">
                <span>Platform</span>
                <input className="input" value={platform} onChange={(event) => setPlatform(event.target.value)} />
              </label>
              <label className="field">
                <span>Enabled</span>
                <select
                  className="input"
                  value={enabled ? "true" : "false"}
                  onChange={(event) => setEnabled(event.target.value === "true")}
                >
                  <option value="true">true</option>
                  <option value="false">false</option>
                </select>
              </label>
            </div>
            <label className="field">
              <span>Display name</span>
              <input className="input" value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
            </label>
            <label className="field">
              <span>Worker image</span>
              <input className="input" value={workerImage} onChange={(event) => setWorkerImage(event.target.value)} />
            </label>
            <div className="grid-2">
              <label className="field">
                <span>Path prefix</span>
                <input className="input" value={pathPrefix} onChange={(event) => setPathPrefix(event.target.value)} />
              </label>
              <label className="field">
                <span>Health path</span>
                <input className="input" value={healthPath} onChange={(event) => setHealthPath(event.target.value)} />
              </label>
            </div>
            <button
              type="button"
              className="button button-primary"
              disabled={upsertMutation.isPending || !accountTypeId.trim() || !platform.trim()}
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
