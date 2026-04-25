"use client";

import { useQuery } from "@tanstack/react-query";
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

interface ProjectSchemasPanelProps {
  apiSession: ApiSession;
  projectId: string;
}

export function ProjectSchemasPanel({
  apiSession,
  projectId,
}: ProjectSchemasPanelProps) {
  const [schemaSearch, setSchemaSearch] = useState("");
  const [selectedSchemaId, setSelectedSchemaId] = useState("");

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const schemasQueryKey = useMemo(
    () =>
      [
        "products.schemas.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        selectedAccountId,
      ] as const,
    [apiSession.baseUrl, apiSession.token, projectId, selectedAccountId],
  );

  const schemasQuery = useQuery({
    queryKey: schemasQueryKey,
    enabled: Boolean(selectedAccountId),
    queryFn: () =>
      runAccountActionRequest(apiSession, selectedAccountId, "products.schemas.list", {}),
  });

  const schemaRows = extractObjectRows(schemasQuery.data ?? null, [
    "items",
    "schemas",
  ]);

  const filteredSchemas = useMemo(() => {
    const query = schemaSearch.trim().toLowerCase();
    if (!query) {
      return schemaRows;
    }

    return schemaRows.filter((schema) => {
      const id = readFirstString(schema, ["schemaId", "id"]).toLowerCase();
      const title = readFirstString(schema, ["title", "name"]).toLowerCase();
      const provider = toReadableValue(schema.provider ?? schema.platform ?? "").toLowerCase();
      return id.includes(query) || title.includes(query) || provider.includes(query);
    });
  }, [schemaRows, schemaSearch]);

  const effectiveSelectedSchemaId = useMemo(() => {
    const hasSelection = filteredSchemas.some((schema) => {
      const candidateId = readFirstString(schema, ["schemaId", "id"]);
      return candidateId === selectedSchemaId;
    });

    if (hasSelection) {
      return selectedSchemaId;
    }

    if (filteredSchemas.length === 0) {
      return "";
    }

    return readFirstString(filteredSchemas[0], ["schemaId", "id"]);
  }, [filteredSchemas, selectedSchemaId]);

  const selectedSchema =
    filteredSchemas.find(
      (schema) => readFirstString(schema, ["schemaId", "id"]) === effectiveSelectedSchemaId,
    ) ?? null;

  const providerCount = useMemo(() => {
    const providers = new Set<string>();
    for (const schema of filteredSchemas) {
      const provider = toReadableValue(schema.provider ?? schema.platform ?? "");
      if (provider) {
        providers.add(provider);
      }
    }

    return providers.size;
  }, [filteredSchemas]);

  return (
    <div className="page-stack" data-testid="project-schemas-panel">
      <header className="page-section-header">
        <h2>Схемы товаров</h2>
        <p>Схемы загружаются автоматически при открытии вкладки и при смене аккаунта.</p>
      </header>

      <section className="summary-grid">
        <article className="summary-card">
          <p>Схем в каталоге</p>
          <strong>{filteredSchemas.length}</strong>
          <small>По текущему аккаунту и фильтру</small>
        </article>
        <article className="summary-card">
          <p>Провайдеры</p>
          <strong>{providerCount}</strong>
          <small>Разные источники схем</small>
        </article>
        <article className="summary-card">
          <p>Текущий режим</p>
          <strong>Schema-driven</strong>
          <small>Подготовка payload для add/update товара</small>
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
          <span>Поиск схем</span>
          <input
            className="input"
            value={schemaSearch}
            onChange={(event) => setSchemaSearch(event.target.value)}
            placeholder="ID, название или провайдер"
          />
        </label>
      </div>

      {!selectedAccountId ? (
        <p className="route-hint">Выберите аккаунт, чтобы увидеть schema catalog.</p>
      ) : schemasQuery.isPending ? (
        <p className="route-hint">Загружаем схемы...</p>
      ) : schemasQuery.error ? (
        <p className="route-error">
          {schemasQuery.error instanceof Error
            ? schemasQuery.error.message
            : "Не удалось загрузить схемы."}
        </p>
      ) : filteredSchemas.length === 0 ? (
        <p className="route-hint">Схемы для выбранного аккаунта не найдены.</p>
      ) : (
        <div className="split-grid">
          <section className="panel-card">
            <h3>Список схем</h3>
            <ul className="entity-list">
              {filteredSchemas.map((schema, index) => {
                const schemaId = readFirstString(schema, ["schemaId", "id"]);
                const isActive = schemaId === effectiveSelectedSchemaId;

                return (
                  <li key={toReadableValue(schemaId || index)}>
                    <button
                      type="button"
                      className={`list-select ${isActive ? "is-active" : ""}`}
                      onClick={() => setSelectedSchemaId(schemaId)}
                    >
                      <strong>{toReadableValue(schemaId || "unknown")}</strong>
                      <small>{toReadableValue(schema.provider ?? schema.platform ?? "")}</small>
                      <p>{toReadableValue(schema.title ?? schema.name ?? "Без описания")}</p>
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>

          <section className="panel-card">
            <h3>Детали схемы</h3>
            {!selectedSchema ? (
              <p className="route-hint">Выберите схему из списка слева.</p>
            ) : (
              <div className="page-stack">
                <dl className="kv-list">
                  <div>
                    <dt>ID</dt>
                    <dd>{toReadableValue(selectedSchema.schemaId ?? selectedSchema.id)}</dd>
                  </div>
                  <div>
                    <dt>Название</dt>
                    <dd>{toReadableValue(selectedSchema.title ?? selectedSchema.name)}</dd>
                  </div>
                  <div>
                    <dt>Версия</dt>
                    <dd>{toReadableValue(selectedSchema.version ?? "n/a")}</dd>
                  </div>
                  <div>
                    <dt>Провайдер</dt>
                    <dd>{toReadableValue(selectedSchema.provider ?? selectedSchema.platform)}</dd>
                  </div>
                </dl>

                <div className="panel-card panel-soft">
                  <h3>Raw schema payload</h3>
                  <pre className="json-preview">
                    {JSON.stringify(selectedSchema, null, 2)}
                  </pre>
                </div>
              </div>
            )}
          </section>
        </div>
      )}
    </div>
  );
}
