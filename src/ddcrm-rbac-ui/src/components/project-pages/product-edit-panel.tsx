"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import { extractObjectRows, isRecord, readFirstString } from "@/lib/worker-result";

interface ProjectProductEditPanelProps {
  apiSession: ApiSession;
  projectId: string;
  accountId: string;
  productId: string;
  mode?: "page" | "modal";
  onCompleted?: () => void;
  onCancel?: () => void;
}

function readFirstNumber(row: Record<string, unknown>, keys: readonly string[]) {
  for (const key of keys) {
    const candidate = row[key];
    if (typeof candidate === "number" && Number.isFinite(candidate)) {
      return candidate;
    }

    if (typeof candidate === "string") {
      const parsed = Number(candidate);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }
  }

  return null;
}

function formatNumber(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(2);
}

function resolveProductPriceForEdit(row: Record<string, unknown>) {
  const direct = readFirstNumber(row, ["price", "amount", "cost"]);
  if (direct !== null) {
    return formatNumber(direct);
  }

  if (isRecord(row.price)) {
    const amount = readFirstNumber(row.price, ["amount", "value", "price"]);
    if (amount !== null) {
      return formatNumber(amount);
    }
  }

  return "";
}

function resolveProductCurrency(row: Record<string, unknown>) {
  if (isRecord(row.price)) {
    const currency = readFirstString(row.price, ["currency", "code"]);
    if (currency) {
      return currency.toUpperCase();
    }
  }

  return "RUB";
}

function normalizeCurrency(value: string, fallback: string) {
  const normalized = value.trim().toUpperCase();
  return normalized || fallback || "RUB";
}

export function ProjectProductEditPanel({
  apiSession,
  projectId,
  accountId,
  productId,
  mode = "page",
  onCompleted,
  onCancel,
}: ProjectProductEditPanelProps) {
  const router = useRouter();
  const queryClient = useQueryClient();

  const [title, setTitle] = useState("");
  const [priceAmount, setPriceAmount] = useState("");
  const [priceCurrency, setPriceCurrency] = useState("RUB");
  const [status, setStatus] = useState(
    "Измените поля и сохраните. Отправляем только реально изменённые значения.",
  );

  const prefilledKeyRef = useRef("");

  const productsQuery = useQuery({
    queryKey: [
      "products.list",
      apiSession.baseUrl,
      apiSession.token,
      projectId,
      accountId,
      "edit-form",
    ] as const,
    queryFn: async () => {
      if (!accountId) {
        return null;
      }

      const result = await runAccountActionRequest(apiSession, accountId, "products.list", {
        limit: 200,
      });

      return isRecord(result) ? result : null;
    },
    enabled: Boolean(accountId),
    staleTime: 10_000,
  });

  const products = useMemo(
    () => extractObjectRows(productsQuery.data ?? null, ["items", "products", "listings"]),
    [productsQuery.data],
  );

  const targetProduct = useMemo(
    () =>
      products.find((row) => readFirstString(row, ["productId", "id"]) === productId) ?? null,
    [productId, products],
  );

  const originalTitle = readFirstString(targetProduct ?? {}, ["title", "name", "displayName"]);
  const originalPrice = targetProduct ? resolveProductPriceForEdit(targetProduct) : "";
  const originalCurrency = targetProduct ? resolveProductCurrency(targetProduct) : "RUB";

  useEffect(() => {
    if (!targetProduct) {
      return;
    }

    const key = `${accountId}:${productId}`;
    if (prefilledKeyRef.current === key) {
      return;
    }

    prefilledKeyRef.current = key;
    setTitle(originalTitle);
    setPriceAmount(originalPrice);
    setPriceCurrency(originalCurrency);
  }, [accountId, originalCurrency, originalPrice, originalTitle, productId, targetProduct]);

  const updateProductMutation = useMutation({
    mutationFn: async () => {
      if (!accountId || !productId) {
        throw new Error("Не удалось определить товар для редактирования.");
      }

      const changes: Record<string, unknown> = {};

      const normalizedTitle = title.trim();
      if (normalizedTitle && normalizedTitle !== originalTitle) {
        changes.title = normalizedTitle;
      }

      const normalizedPriceInput = priceAmount.trim();
      if (normalizedPriceInput) {
        const amount = Number(normalizedPriceInput);
        if (!Number.isFinite(amount) || amount < 0) {
          throw new Error("Цена должна быть неотрицательным числом.");
        }

        changes.price = {
          amount,
          currency: normalizeCurrency(priceCurrency, originalCurrency),
        };
      }

      if (Object.keys(changes).length === 0) {
        throw new Error("Нет изменений для сохранения.");
      }

      return runAccountActionRequest(apiSession, accountId, "products.update", {
        productId,
        changes,
      });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: [
          "products.list",
          apiSession.baseUrl,
          apiSession.token,
          projectId,
          accountId,
        ],
      });

      setStatus("Изменения сохранены.");
      if (onCompleted) {
        onCompleted();
        return;
      }

      router.push(`/projects/${projectId}/products`);
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось обновить товар.");
    },
  });

  if (!accountId || !productId) {
    return (
      <div className="page-stack" data-testid="project-product-edit-panel">
        <header className="page-section-header">
          <h2>Редактировать товар</h2>
          <p>Не переданы обязательные параметры `accountId` и `productId`.</p>
        </header>
        {mode === "page" ? (
          <div className="panel-actions">
            <Link href={`/projects/${projectId}/products`} className="button button-ghost">
              Назад к товарам
            </Link>
          </div>
        ) : onCancel ? (
          <div className="panel-actions">
            <button type="button" className="button button-ghost" onClick={onCancel}>
              Закрыть
            </button>
          </div>
        ) : null}
      </div>
    );
  }

  return (
    <div className="page-stack" data-testid="project-product-edit-panel">
      {mode === "page" ? (
        <>
          <header className="page-section-header">
            <h2>Редактировать товар</h2>
            <p>
              Отдельная страница редактирования. Изменения отправляются в worker через
              `products.update`.
            </p>
          </header>

          <div className="panel-actions">
            <Link href={`/projects/${projectId}/products`} className="button button-ghost">
              Назад к товарам
            </Link>
          </div>
        </>
      ) : null}

      {productsQuery.isPending ? (
        <section className="panel-card">
          <p className="route-hint">Загружаем товар...</p>
        </section>
      ) : null}

      {productsQuery.error ? (
        <section className="panel-card">
          <p className="route-error">
            {productsQuery.error instanceof Error
              ? productsQuery.error.message
              : "Не удалось получить данные товара."}
          </p>
        </section>
      ) : null}

      {!productsQuery.isPending && !productsQuery.error && !targetProduct ? (
        <section className="panel-card">
          <p className="route-error">Товар не найден в выбранном аккаунте.</p>
        </section>
      ) : null}

      {!productsQuery.isPending && !productsQuery.error && targetProduct ? (
        <section className="panel-card page-stack">
          <div className="stacked-block">
            <label className="field">
              <span>Product ID</span>
              <input className="input" value={productId} readOnly />
            </label>
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
              disabled={updateProductMutation.isPending}
              onClick={() => updateProductMutation.mutate()}
            >
              Сохранить изменения
            </button>
            {mode === "modal" && onCancel ? (
              <button type="button" className="button button-ghost" onClick={onCancel}>
                Отмена
              </button>
            ) : null}
            <p className="route-hint">{status}</p>
          </div>
        </section>
      ) : null}
    </div>
  );
}
