"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AdminLayout } from "@/components/layout/admin-layout";
import {
  listAdminAccountTypesRequest,
  upsertAdminAccountTypeRequest,
  type AdminAccountTypeUpsertPayload,
  type ApiSession,
} from "@/lib/api-client";
import { useSessionGuard } from "@/lib/use-session-guard";

type TemplateField = NonNullable<AdminAccountTypeUpsertPayload["formFields"]>[number];

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
  const [workerImage, setWorkerImage] = useState("ddcrm/funpay-worker:local");
  const [pathPrefix, setPathPrefix] = useState("/internal/v2/worker");
  const [healthPath, setHealthPath] = useState("/health");
  const [workerCommand, setWorkerCommand] = useState("python -m ddcrm_funpay_worker.main");
  const [templateSearch, setTemplateSearch] = useState("");
  const [templatePlatformFilter, setTemplatePlatformFilter] = useState("all");
  const [templateStatusFilter, setTemplateStatusFilter] = useState("all");
  const [selectedTemplateId, setSelectedTemplateId] = useState("");

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
    mutationFn: async () => {
      const baseFormFields: TemplateField[] = [
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
      ];

      const funpayFormFields: TemplateField[] = [
        {
          key: "funpayGoldenKey",
          label: "FunPay golden_key",
          inputType: "password",
          required: true,
          secret: true,
          placeholder: "Введите golden_key аккаунта FunPay",
        },
        {
          key: "funpayUserAgent",
          label: "FunPay user agent",
          inputType: "text",
          required: false,
          secret: false,
          placeholder: "Опционально: браузерный User-Agent",
        },
      ];
      const playerokFormFields: TemplateField[] = [
        {
          key: "playerokAuthScheme",
          label: "Playerok auth scheme",
          inputType: "text",
          required: true,
          secret: false,
          placeholder: "tokens или cookies",
          defaultValue: "tokens",
        },
        {
          key: "playerokToken",
          label: "Playerok token",
          inputType: "password",
          required: false,
          secret: true,
          placeholder: "Обязательно для scheme=tokens",
        },
        {
          key: "playerokDdg5",
          label: "Playerok ddg5",
          inputType: "password",
          required: false,
          secret: true,
          placeholder: "Cookie __ddg5_ (обязательно для scheme=tokens)",
        },
        {
          key: "playerokCookies",
          label: "Playerok cookies",
          inputType: "password",
          required: false,
          secret: true,
          placeholder: "Обязательно для scheme=cookies",
        },
        {
          key: "playerokUserAgent",
          label: "Playerok user agent",
          inputType: "text",
          required: false,
          secret: false,
          placeholder: "Опционально: браузерный User-Agent",
        },
      ];

      const normalizedPlatform = platform.trim().toLowerCase();
      const formFields = normalizedPlatform === "funpay"
        ? [...baseFormFields, ...funpayFormFields]
        : normalizedPlatform === "playerok"
          ? [...baseFormFields, ...playerokFormFields]
          : baseFormFields;

      return upsertAdminAccountTypeRequest(apiSession, accountTypeId.trim(), {
        platform: platform.trim(),
        displayName: displayName.trim(),
        workerProfileId: "test-worker",
        enabled,
        sortOrder: 10,
        formFields,
        runtime: {
          autospawnEnabled: true,
          workerImage: workerImage.trim(),
          workerPathPrefix: pathPrefix.trim(),
          healthPath: healthPath.trim(),
          containerPort: 0,
          environmentVariables: {
            TEST_WORKER_PROVIDER: platform.trim(),
          },
          workerCommand: workerCommand
            .split(/\s+/)
            .map((part) => part.trim())
            .filter(Boolean),
        },
      });
    },
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
  const filteredItems = items.filter((item) => {
    const query = templateSearch.trim().toLowerCase();
    const platformToken = templatePlatformFilter.trim().toLowerCase();
    const statusToken = templateStatusFilter.trim().toLowerCase();

    if (platformToken !== "all" && item.platform.toLowerCase() !== platformToken) {
      return false;
    }

    if (statusToken !== "all") {
      const enabledToken = item.enabled ? "enabled" : "disabled";
      if (enabledToken !== statusToken) {
        return false;
      }
    }

    if (!query) {
      return true;
    }

    const haystack = [
      item.accountTypeId,
      item.displayName,
      item.platform,
      item.runtime.workerImage,
    ].join(" ").toLowerCase();
    return haystack.includes(query);
  });

  const effectiveSelectedTemplateId = filteredItems.some((item) => item.accountTypeId === selectedTemplateId)
    ? selectedTemplateId
    : (filteredItems[0]?.accountTypeId ?? "");
  const selectedTemplate = filteredItems.find((item) => item.accountTypeId === effectiveSelectedTemplateId) ?? null;

  const applyTemplateToForm = (accountType: typeof items[number]) => {
    setSelectedTemplateId(accountType.accountTypeId);
    setAccountTypeId(accountType.accountTypeId);
    setPlatform(accountType.platform);
    setDisplayName(accountType.displayName);
    setEnabled(accountType.enabled);
    setWorkerImage(accountType.runtime.workerImage);
    setPathPrefix(accountType.runtime.workerPathPrefix);
    setHealthPath(accountType.runtime.healthPath);
    setWorkerCommand((accountType.runtime.workerCommand ?? []).join(" "));
    setStatus(`Template ${accountType.accountTypeId} загружен в форму.`);
  };

  return (
    <AdminLayout session={session} activeTab="templates" onLogout={logout}>
      <div className="page-stack">
        <section className="page-head">
          <div className="page-head__row">
            <div>
              <h1 className="page-title">Platform templates</h1>
              <p className="route-hint">Каталог runtime-шаблонов для account/service workers.</p>
            </div>
            <div className="inline">
              <button
                type="button"
                className="button button-ghost button-small"
                disabled={templatesQuery.isFetching}
                onClick={() => templatesQuery.refetch()}
              >
                Обновить
              </button>
              <button
                type="button"
                className="button button-primary button-small"
                onClick={() => {
                  setAccountTypeId("test-worker.new");
                  setPlatform("funpay");
                  setDisplayName("Новый template");
                  setEnabled(true);
                  setWorkerImage("ddcrm/worker:latest");
                  setPathPrefix("/internal/v2/worker");
                  setHealthPath("/health");
                  setWorkerCommand("python -m ddcrm_worker.main");
                  setStatus("Форма очищена для нового template.");
                }}
              >
                ＋ Добавить template
              </button>
            </div>
          </div>
          <div className={`status ${status.toLowerCase().includes("не удалось") ? "status--bad" : "status--info"}`}>
            {status}
          </div>
        </section>

        <section className="state-grid">
          <article className="state-card">
            <span className="label">Templates</span>
            <strong>{items.length}</strong>
          </article>
          <article className="state-card">
            <span className="label">Enabled</span>
            <strong>{items.filter((item) => item.enabled).length}</strong>
          </article>
          <article className="state-card">
            <span className="label">Disabled</span>
            <strong>{items.filter((item) => !item.enabled).length}</strong>
          </article>
          <article className="state-card">
            <span className="label">Platforms</span>
            <strong>{new Set(items.map((item) => item.platform)).size}</strong>
          </article>
        </section>

        <section className="card">
          <div className="template-toolbar">
            <label className="field">
              <span>Поиск</span>
              <input
                className="input"
                value={templateSearch}
                onChange={(event) => setTemplateSearch(event.target.value)}
                placeholder="type id, platform, image"
              />
            </label>
            <label className="field">
              <span>Platform</span>
              <select
                className="input"
                value={templatePlatformFilter}
                onChange={(event) => setTemplatePlatformFilter(event.target.value)}
              >
                <option value="all">Все платформы</option>
                {Array.from(new Set(items.map((item) => item.platform))).sort((a, b) => a.localeCompare(b)).map((platformItem) => (
                  <option key={platformItem} value={platformItem}>{platformItem}</option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Статус</span>
              <select
                className="input"
                value={templateStatusFilter}
                onChange={(event) => setTemplateStatusFilter(event.target.value)}
              >
                <option value="all">Все</option>
                <option value="enabled">enabled</option>
                <option value="disabled">disabled</option>
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

        <section className="templates-layout">
          <div className="template-list">
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
            ) : null}
            {!templatesQuery.isPending && !templatesQuery.error && items.length > 0 && filteredItems.length === 0 ? (
              <p className="route-hint">По фильтру ничего не найдено.</p>
            ) : null}
            {filteredItems.map((item) => (
              <article
                key={item.accountTypeId}
                className={`template-card ${selectedTemplate?.accountTypeId === item.accountTypeId ? "is-active" : ""}`}
              >
                <div className="template-card__head">
                  <div>
                    <h2 className="template-title">{item.displayName}</h2>
                    <div className="template-meta">{item.accountTypeId}</div>
                  </div>
                  <div className="chip-row">
                    <span className={`status ${item.enabled ? "status--ok" : "status--warn"}`}>
                      {item.enabled ? "enabled" : "disabled"}
                    </span>
                    <span className="chip">{item.platform}</span>
                  </div>
                </div>
                <div className="template-stats">
                  <div className="stat-box"><b>{item.runtime.workerImage}</b><span>image</span></div>
                  <div className="stat-box"><b>{item.runtime.workerPathPrefix}</b><span>path</span></div>
                  <div className="stat-box"><b>{item.runtime.healthPath}</b><span>health</span></div>
                  <div className="stat-box"><b>{(item.runtime.workerCommand ?? []).join(" ") || "-"}</b><span>command</span></div>
                </div>
                <div className="row-split">
                  <div className="chip-row">
                    <span className="chip">form fields: {item.formFields.length}</span>
                    <span className="chip">sort: {item.sortOrder}</span>
                  </div>
                  <button
                    type="button"
                    className="button button-ghost button-small"
                    onClick={() => applyTemplateToForm(item)}
                  >
                    В форму
                  </button>
                </div>
              </article>
            ))}
          </div>

          <aside className="details-panel page-stack">
            <section className="details-card">
              {selectedTemplate ? (
                <>
                  <div className="details-head">
                    <div>
                      <h2 className="card__title">{selectedTemplate.displayName}</h2>
                      <div className="route-hint">Информация по выбранному template</div>
                    </div>
                    <span className={`status ${selectedTemplate.enabled ? "status--ok" : "status--warn"}`}>
                      {selectedTemplate.enabled ? "enabled" : "disabled"}
                    </span>
                  </div>
                  <div className="summary-list">
                    <div className="summary-line"><span>Account type ID</span><strong>{selectedTemplate.accountTypeId}</strong></div>
                    <div className="summary-line"><span>Platform</span><strong>{selectedTemplate.platform}</strong></div>
                    <div className="summary-line"><span>Image</span><strong>{selectedTemplate.runtime.workerImage}</strong></div>
                    <div className="summary-line"><span>Command</span><strong>{(selectedTemplate.runtime.workerCommand ?? []).join(" ") || "-"}</strong></div>
                    <div className="summary-line"><span>Path prefix</span><strong>{selectedTemplate.runtime.workerPathPrefix}</strong></div>
                    <div className="summary-line"><span>Health path</span><strong>{selectedTemplate.runtime.healthPath}</strong></div>
                  </div>
                </>
              ) : (
                <p className="route-hint">Выберите template из списка.</p>
              )}
            </section>

            <article className="details-card page-stack">
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
              <label className="field">
                <span>Worker command</span>
                <input
                  className="input"
                  value={workerCommand}
                  onChange={(event) => setWorkerCommand(event.target.value)}
                  placeholder="python -m ddcrm_funpay_worker.main"
                />
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
            </article>
          </aside>
        </section>
      </div>
    </AdminLayout>
  );
}
