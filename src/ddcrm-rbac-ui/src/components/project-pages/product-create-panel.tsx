"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { AccountSelector } from "@/components/account-selector";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import {
  extractObjectRows,
  isRecord,
  readFirstString,
} from "@/lib/worker-result";

interface ProjectProductCreatePanelProps {
  apiSession: ApiSession;
  projectId: string;
  preferredAccountId: string;
  mode?: "page" | "modal";
  onCompleted?: () => void;
  onCancel?: () => void;
}

interface SchemaOption {
  schemaId: string;
  provider: string;
  title: string;
  isSupported: boolean;
  requiredFields: string[];
}

const supportedRequiredFields = new Set([
  "schemaId",
  "title",
  "price.amount",
  "price.currency",
]);

function normalizeCurrency(value: string) {
  const normalized = value.trim().toUpperCase();
  return normalized || "RUB";
}

function readSchemaRequiredFields(row: Record<string, unknown>) {
  const fields = Array.isArray(row.fields) ? row.fields : [];
  return fields
    .filter(isRecord)
    .filter((field) => field.required === true)
    .map((field) => readFirstString(field, ["key"]))
    .filter((key) => key.length > 0);
}

function buildSchemaOptions(payload: Record<string, unknown> | null): SchemaOption[] {
  if (!payload) {
    return [];
  }

  const rows = extractObjectRows(payload, ["items", "schemas"]);
  return rows.map((row) => {
    const requiredFields = readSchemaRequiredFields(row);
    return {
      schemaId: readFirstString(row, ["schemaId", "id"]),
      provider: readFirstString(row, ["provider", "platform"]),
      title: readFirstString(row, ["title", "name"]),
      requiredFields,
      isSupported: requiredFields.every((key) => supportedRequiredFields.has(key)),
    };
  });
}

function pickDefaultSchemaId(options: SchemaOption[]) {
  const supported = options.filter((option) => option.isSupported);
  if (supported.length === 0) {
    return "digital_goods.v1";
  }

  const digital = supported.find((option) => option.schemaId === "digital_goods.v1");
  return (digital ?? supported[0]).schemaId;
}

export function ProjectProductCreatePanel({
  apiSession,
  projectId,
  preferredAccountId,
  mode = "page",
  onCompleted,
  onCancel,
}: ProjectProductCreatePanelProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [title, setTitle] = useState("Новый товар");
  const [priceAmount, setPriceAmount] = useState("100");
  const [priceCurrency, setPriceCurrency] = useState("RUB");
  const [schemaId, setSchemaId] = useState("digital_goods.v1");
  const [status, setStatus] = useState(
    "Заполните форму. Создание товара выполняется через worker `products.create`.",
  );

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  useEffect(() => {
    if (!preferredAccountId) {
      return;
    }

    if (!accounts.some((account) => account.id === preferredAccountId)) {
      return;
    }

    if (selectedAccountId === preferredAccountId) {
      return;
    }

    setSelectedAccountId(preferredAccountId);
  }, [accounts, preferredAccountId, selectedAccountId, setSelectedAccountId]);

  const schemasQuery = useQuery({
    queryKey: [
      "products.schemas.list",
      apiSession.baseUrl,
      apiSession.token,
      projectId,
      selectedAccountId,
    ] as const,
    queryFn: async () => {
      if (!selectedAccountId) {
        return null;
      }

      const result = await runAccountActionRequest(
        apiSession,
        selectedAccountId,
        "products.schemas.list",
        {},
      );

      return isRecord(result) ? result : null;
    },
    enabled: Boolean(selectedAccountId),
    staleTime: 10_000,
  });

  const schemaOptions = useMemo(
    () => buildSchemaOptions(schemasQuery.data ?? null),
    [schemasQuery.data],
  );

  const supportedSchemaOptions = useMemo(
    () => schemaOptions.filter((option) => option.isSupported && option.schemaId),
    [schemaOptions],
  );

  const effectiveSchemaId = useMemo(() => {
    if (supportedSchemaOptions.length === 0) {
      return "digital_goods.v1";
    }

    if (supportedSchemaOptions.some((option) => option.schemaId === schemaId)) {
      return schemaId;
    }

    return pickDefaultSchemaId(supportedSchemaOptions);
  }, [schemaId, supportedSchemaOptions]);

  const createProductMutation = useMutation({
    mutationFn: async () => {
      if (!selectedAccountId) {
        throw new Error("Выберите аккаунт для создания товара.");
      }

      if (!title.trim()) {
        throw new Error("Название товара обязательно.");
      }

      const amount = Number(priceAmount);
      if (!Number.isFinite(amount) || amount < 0) {
        throw new Error("Цена товара должна быть неотрицательным числом.");
      }

      const selectedSchema = supportedSchemaOptions.find(
        (option) => option.schemaId === effectiveSchemaId,
      );
      if (!selectedSchema && supportedSchemaOptions.length > 0) {
        throw new Error("Выберите поддерживаемую схему товара.");
      }

      return runAccountActionRequest(apiSession, selectedAccountId, "products.create", {
        schemaId: selectedSchema?.schemaId ?? effectiveSchemaId,
        title: title.trim(),
        price: {
          amount,
          currency: normalizeCurrency(priceCurrency),
        },
        status: "active",
      });
    },
    onSuccess: async () => {
      if (selectedAccountId) {
        await queryClient.invalidateQueries({
          queryKey: [
            "products.list",
            apiSession.baseUrl,
            apiSession.token,
            projectId,
            selectedAccountId,
          ],
        });
      }

      setStatus("Товар создан.");
      if (onCompleted) {
        onCompleted();
        return;
      }

      router.push(`/projects/${projectId}/products`);
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось создать товар.");
    },
  });

  const unsupportedSchemasCount = schemaOptions.length - supportedSchemaOptions.length;

  return (
    <div className="page-stack" data-testid="project-product-create-panel">
      {mode === "page" ? (
        <>
          <header className="page-section-header">
            <h2>Создать товар</h2>
            <p>
              Отдельная страница создания товара. Поддерживаются схемы, которые не требуют
              дополнительных provider-specific полей.
            </p>
          </header>

          <div className="panel-actions">
            <Link href={`/projects/${projectId}/products`} className="button button-ghost">
              Назад к товарам
            </Link>
          </div>
        </>
      ) : null}

      {accountsError ? <p className="route-error">{accountsError.message}</p> : null}
      <AccountSelector
        accounts={accounts}
        selectedAccountId={selectedAccountId}
        onChange={setSelectedAccountId}
        isLoading={accountsLoading}
      />

      {!selectedAccountId ? (
        <section className="panel-card">
          <p className="route-hint">Выберите аккаунт, чтобы продолжить создание товара.</p>
        </section>
      ) : (
        <section className="panel-card page-stack">
          <div className="stacked-block">
            <label className="field">
              <span>Схема товара</span>
              <select
                className="input"
                value={effectiveSchemaId}
                onChange={(event) => setSchemaId(event.target.value)}
                disabled={schemasQuery.isPending || supportedSchemaOptions.length === 0}
              >
                {supportedSchemaOptions.length === 0 ? (
                  <option value="digital_goods.v1">digital_goods.v1 (fallback)</option>
                ) : (
                  supportedSchemaOptions.map((option) => (
                    <option key={option.schemaId} value={option.schemaId}>
                      {option.schemaId}
                      {option.provider ? ` · ${option.provider}` : ""}
                      {option.title ? ` · ${option.title}` : ""}
                    </option>
                  ))
                )}
              </select>
            </label>

            {schemasQuery.isPending ? (
              <p className="route-hint">Загружаем доступные схемы...</p>
            ) : null}
            {schemasQuery.error ? (
              <p className="route-error">
                {schemasQuery.error instanceof Error
                  ? schemasQuery.error.message
                  : "Не удалось получить схемы товара."}
              </p>
            ) : null}
            {unsupportedSchemasCount > 0 ? (
              <p className="route-hint">
                {unsupportedSchemasCount} схем(ы) скрыто: для них нужны дополнительные поля.
              </p>
            ) : null}

            <label className="field">
              <span>Название</span>
              <input
                className="input"
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                placeholder="Название товара"
              />
            </label>

            <div className="grid-2">
              <label className="field">
                <span>Цена</span>
                <input
                  className="input"
                  value={priceAmount}
                  onChange={(event) => setPriceAmount(event.target.value)}
                  placeholder="100"
                />
              </label>
              <label className="field">
                <span>Валюта</span>
                <input
                  className="input"
                  value={priceCurrency}
                  onChange={(event) => setPriceCurrency(event.target.value)}
                  placeholder="RUB"
                />
              </label>
            </div>
          </div>

          <div className="panel-actions">
            <button
              type="button"
              className="button button-primary"
              disabled={createProductMutation.isPending || !selectedAccountId}
              onClick={() => createProductMutation.mutate()}
            >
              Создать товар
            </button>
            {mode === "modal" && onCancel ? (
              <button type="button" className="button button-ghost" onClick={onCancel}>
                Отмена
              </button>
            ) : null}
            <p className="route-hint">{status}</p>
          </div>
        </section>
      )}
    </div>
  );
}
