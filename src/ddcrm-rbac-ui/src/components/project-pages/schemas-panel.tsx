"use client";

import { useQueries } from "@tanstack/react-query";
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

interface SchemaWithAccount {
  accountId: string;
  row: Record<string, unknown>;
}

export function ProjectSchemasPanel({
  apiSession,
  projectId,
}: ProjectSchemasPanelProps) {
  const [accountFilterId, setAccountFilterId] = useState("all");
  const [schemaSearch, setSchemaSearch] = useState("");
  const [selectedSchemaKey, setSelectedSchemaKey] = useState("");

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const accountNameById = useMemo(() => {
    return new Map(accounts.map((account) => [account.id, account.displayName]));
  }, [accounts]);

  const effectiveFilterId = useMemo(() => {
    if (accountFilterId === "all") {
      return "all";
    }

    return accounts.some((account) => account.id === accountFilterId)
      ? accountFilterId
      : "all";
  }, [accountFilterId, accounts]);

  const scopedAccountIds = useMemo(() => {
    if (effectiveFilterId !== "all") {
      return [effectiveFilterId];
    }

    return accounts.map((account) => account.id);
  }, [accounts, effectiveFilterId]);

  const schemasQueries = useQueries({
    queries: scopedAccountIds.map((accountId) => ({
      queryKey: [
        "products.schemas.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        accountId,
      ] as const,
      queryFn: () =>
        runAccountActionRequest(apiSession, accountId, "products.schemas.list", {}),
      enabled: Boolean(accountId),
      refetchInterval: 60_000,
      staleTime: 15_000,
    })),
  });

  const schemaRows = useMemo<SchemaWithAccount[]>(() => {
    return scopedAccountIds.flatMap((accountId, index) => {
      const query = schemasQueries[index];
      const rows = extractObjectRows(query?.data ?? null, [
        "items",
        "schemas",
      ]);

      return rows.map((row) => ({
        accountId,
        row,
      }));
    });
  }, [schemasQueries, scopedAccountIds]);

  const filteredSchemas = useMemo(() => {
    const query = schemaSearch.trim().toLowerCase();
    if (!query) {
      return schemaRows;
    }

    return schemaRows.filter(({ accountId, row }) => {
      const id = readFirstString(row, ["schemaId", "id"]).toLowerCase();
      const title = readFirstString(row, ["title", "name"]).toLowerCase();
      const provider = toReadableValue(row.provider ?? row.platform ?? "").toLowerCase();
      const accountName = (accountNameById.get(accountId) ?? "").toLowerCase();

      return (
        id.includes(query)
        || title.includes(query)
        || provider.includes(query)
        || accountName.includes(query)
      );
    });
  }, [accountNameById, schemaRows, schemaSearch]);

  const effectiveSelectedSchemaKey = useMemo(() => {
    const hasSelection = filteredSchemas.some(({ accountId, row }) => {
      const schemaId = readFirstString(row, ["schemaId", "id"]);
      return `${accountId}:${schemaId}` === selectedSchemaKey;
    });

    if (hasSelection) {
      return selectedSchemaKey;
    }

    if (filteredSchemas.length === 0) {
      return "";
    }

    const first = filteredSchemas[0];
    return `${first.accountId}:${readFirstString(first.row, ["schemaId", "id"])}`;
  }, [filteredSchemas, selectedSchemaKey]);

  const selectedSchema =
    filteredSchemas.find(({ accountId, row }) => {
      const schemaId = readFirstString(row, ["schemaId", "id"]);
      return `${accountId}:${schemaId}` === effectiveSelectedSchemaKey;
    }) ?? null;

  const providerCount = useMemo(() => {
    const providers = new Set<string>();
    for (const { row } of filteredSchemas) {
      const provider = toReadableValue(row.provider ?? row.platform ?? "");
      if (provider) {
        providers.add(provider);
      }
    }

    return providers.size;
  }, [filteredSchemas]);

  const anyPending = schemasQueries.some((query) => query.isPending);
  const anyFetching = schemasQueries.some((query) => query.isFetching);
  const firstError = schemasQueries.find((query) => query.error)?.error;

  const refreshSchemas = async () => {
    await Promise.all(schemasQueries.map((query) => query.refetch()));
  };

  return (
    <div className="page-stack" data-testid="project-schemas-panel">
      <header className="page-section-header">
        <h2>Схемы товаров</h2>
        <p>
          Каталог схем собирается по всем аккаунтам проекта, чтобы видеть различия между
          воркерами и площадками в одном месте.
        </p>
      </header>

      <section className="summary-grid">
        <article className="summary-card">
          <p>Схем в каталоге</p>
          <strong>{filteredSchemas.length}</strong>
          <small>По текущему scope и фильтру</small>
        </article>
        <article className="summary-card">
          <p>Провайдеры</p>
          <strong>{providerCount}</strong>
          <small>Разные источники схем</small>
        </article>
        <article className="summary-card">
          <p>Аккаунтов в выборке</p>
          <strong>{scopedAccountIds.length}</strong>
          <small>
            {effectiveFilterId === "all"
              ? "Отображаем все аккаунты"
              : "Выбран конкретный аккаунт"}
          </small>
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
          <span>Источник данных</span>
          <select
            className="input"
            value={effectiveFilterId}
            onChange={(event) => setAccountFilterId(event.target.value)}
          >
            <option value="all">Все аккаунты проекта</option>
            {accounts.map((account) => (
              <option key={account.id} value={account.id}>
                {account.displayName} · {account.platform}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Поиск схем</span>
          <input
            className="input"
            value={schemaSearch}
            onChange={(event) => setSchemaSearch(event.target.value)}
            placeholder="ID, название, провайдер, аккаунт"
          />
        </label>
      </div>

      {scopedAccountIds.length === 0 ? (
        <p className="route-hint">Добавьте аккаунт в проект, чтобы загрузить схемы.</p>
      ) : anyPending ? (
        <p className="route-hint">Загружаем схемы по аккаунтам...</p>
      ) : firstError ? (
        <p className="route-error">
          {firstError instanceof Error
            ? firstError.message
            : "Не удалось загрузить схемы по одному из аккаунтов."}
        </p>
      ) : filteredSchemas.length === 0 ? (
        <p className="route-hint">Схемы для выбранной выборки аккаунтов не найдены.</p>
      ) : (
        <div className="split-grid">
          <section className="panel-card">
            <div className="panel-title-row">
              <h3>Список схем</h3>
              <button
                type="button"
                className="button button-ghost"
                onClick={refreshSchemas}
                disabled={anyFetching || scopedAccountIds.length === 0}
              >
                Обновить
              </button>
            </div>
            <ul className="entity-list">
              {filteredSchemas.map(({ accountId, row }, index) => {
                const schemaId = readFirstString(row, ["schemaId", "id"]);
                const key = `${accountId}:${schemaId}`;
                const isActive = key === effectiveSelectedSchemaKey;
                const accountName = accountNameById.get(accountId) ?? accountId;

                return (
                  <li key={`${key || index}`}>
                    <button
                      type="button"
                      className={`list-select ${isActive ? "is-active" : ""}`}
                      onClick={() => setSelectedSchemaKey(key)}
                    >
                      <strong>{toReadableValue(schemaId || "unknown")}</strong>
                      <small>{toReadableValue(row.provider ?? row.platform ?? "")}</small>
                      <p>{toReadableValue(row.title ?? row.name ?? "Без описания")}</p>
                      <small>Аккаунт: {accountName}</small>
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
                    <dt>Account</dt>
                    <dd>
                      {accountNameById.get(selectedSchema.accountId) ?? selectedSchema.accountId}
                    </dd>
                  </div>
                  <div>
                    <dt>ID</dt>
                    <dd>{toReadableValue(selectedSchema.row.schemaId ?? selectedSchema.row.id)}</dd>
                  </div>
                  <div>
                    <dt>Название</dt>
                    <dd>{toReadableValue(selectedSchema.row.title ?? selectedSchema.row.name)}</dd>
                  </div>
                  <div>
                    <dt>Версия</dt>
                    <dd>{toReadableValue(selectedSchema.row.version ?? "n/a")}</dd>
                  </div>
                  <div>
                    <dt>Провайдер</dt>
                    <dd>
                      {toReadableValue(selectedSchema.row.provider ?? selectedSchema.row.platform)}
                    </dd>
                  </div>
                </dl>

                <div className="panel-card panel-soft">
                  <h3>Raw schema payload</h3>
                  <pre className="json-preview">
                    {JSON.stringify(selectedSchema.row, null, 2)}
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

