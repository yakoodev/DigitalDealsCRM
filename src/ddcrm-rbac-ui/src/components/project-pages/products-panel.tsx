"use client";

import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
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

interface ProjectProductsPanelProps {
  apiSession: ApiSession;
  projectId: string;
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

export function ProjectProductsPanel({
  apiSession,
  projectId,
}: ProjectProductsPanelProps) {
  const queryClient = useQueryClient();
  const [productSearch, setProductSearch] = useState("");
  const [newProductTitle, setNewProductTitle] = useState("Новый товар");
  const [newProductPrice, setNewProductPrice] = useState("100");
  const [editProductId, setEditProductId] = useState("");
  const [editProductTitle, setEditProductTitle] = useState("");
  const [editProductPrice, setEditProductPrice] = useState("");
  const [status, setStatus] = useState("Товары загружаются автоматически.");

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const productsQueryKey = useMemo(
    () =>
      [
        "products.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        selectedAccountId,
      ] as const,
    [apiSession.baseUrl, apiSession.token, projectId, selectedAccountId],
  );

  const productsQuery = useQuery({
    queryKey: productsQueryKey,
    enabled: Boolean(selectedAccountId),
    queryFn: () =>
      runAccountActionRequest(apiSession, selectedAccountId, "products.list", {
        limit: 100,
      }),
  });

  const createProductMutation = useMutation({
    mutationFn: () => {
      const parsedPrice = Number(newProductPrice);
      if (!Number.isFinite(parsedPrice) || parsedPrice < 0) {
        throw new Error("Цена товара должна быть неотрицательным числом.");
      }

      if (!newProductTitle.trim()) {
        throw new Error("Название товара обязательно.");
      }

      return runAccountActionRequest(apiSession, selectedAccountId, "products.create", {
        title: newProductTitle.trim(),
        price: parsedPrice,
      });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: productsQueryKey });
      setStatus("Товар добавлен.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось добавить товар.");
    },
  });

  const updateProductMutation = useMutation({
    mutationFn: () => {
      if (!editProductId.trim()) {
        throw new Error("Выберите товар для обновления.");
      }

      const payload: Record<string, unknown> = {
        productId: editProductId.trim(),
      };

      if (editProductTitle.trim()) {
        payload.title = editProductTitle.trim();
      }

      if (editProductPrice.trim()) {
        const parsedPrice = Number(editProductPrice);
        if (!Number.isFinite(parsedPrice) || parsedPrice < 0) {
          throw new Error("Цена товара должна быть неотрицательным числом.");
        }

        payload.price = parsedPrice;
      }

      return runAccountActionRequest(apiSession, selectedAccountId, "products.update", payload);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: productsQueryKey });
      setStatus("Товар обновлён.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось обновить товар.");
    },
  });

  const deleteProductMutation = useMutation({
    mutationFn: (productId: string) =>
      runAccountActionRequest(apiSession, selectedAccountId, "products.delete", {
        productId,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: productsQueryKey });
      setStatus("Товар удалён.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось удалить товар.");
    },
  });

  const productRows = extractObjectRows(productsQuery.data ?? null, [
    "items",
    "products",
    "listings",
  ]);

  const filteredRows = useMemo(() => {
    const query = productSearch.trim().toLowerCase();
    if (!query) {
      return productRows;
    }

    return productRows.filter((row) => {
      const id = readFirstString(row, ["productId", "id"]).toLowerCase();
      const title = readFirstString(row, ["title", "name", "displayName"]).toLowerCase();
      return id.includes(query) || title.includes(query);
    });
  }, [productRows, productSearch]);

  const pricedItems = filteredRows
    .map((row) => readFirstNumber(row, ["price", "amount", "cost"]))
    .filter((value): value is number => typeof value === "number");

  const averagePrice =
    pricedItems.length === 0
      ? null
      : pricedItems.reduce((sum, value) => sum + value, 0) / pricedItems.length;

  return (
    <div className="page-stack" data-testid="project-products-panel">
      <header className="page-section-header">
        <h2>Товары</h2>
        <p>
          Список товаров загружается автоматически при открытии вкладки и при смене
          аккаунта.
        </p>
      </header>

      <section className="summary-grid">
        <article className="summary-card">
          <p>Найдено товаров</p>
          <strong>{filteredRows.length}</strong>
          <small>По текущему фильтру и аккаунту</small>
        </article>
        <article className="summary-card">
          <p>Средняя цена</p>
          <strong>{averagePrice === null ? "n/a" : averagePrice.toFixed(2)}</strong>
          <small>Рассчитано по доступным значениям цены</small>
        </article>
        <article className="summary-card">
          <p>Автосинхронизация</p>
          <strong>Enabled</strong>
          <small>Список обновляется при смене аккаунта</small>
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
          <span>Поиск по товарам</span>
          <input
            className="input"
            value={productSearch}
            onChange={(event) => setProductSearch(event.target.value)}
            placeholder="ID или название"
          />
        </label>
      </div>

      {!selectedAccountId ? (
        <p className="route-hint">Выберите аккаунт, чтобы увидеть товары.</p>
      ) : null}

      {selectedAccountId && productsQuery.isPending ? (
        <p className="route-hint">Загружаем товары...</p>
      ) : null}

      {selectedAccountId && productsQuery.error ? (
        <p className="route-error">
          {productsQuery.error instanceof Error
            ? productsQuery.error.message
            : "Не удалось загрузить товары."}
        </p>
      ) : null}

      {selectedAccountId && !productsQuery.isPending && !productsQuery.error ? (
        <div className="split-grid">
          <section className="panel-card">
            <h3>Список товаров</h3>
            {filteredRows.length === 0 ? (
              <p className="route-hint">Товары не найдены для выбранного аккаунта.</p>
            ) : (
              <ul className="entity-list">
                {filteredRows.map((row, index) => {
                  const productId = readFirstString(row, ["productId", "id"]);
                  const title = readFirstString(row, ["title", "name", "displayName"]);
                  const price = toReadableValue(row.price ?? row.amount ?? row.cost ?? "");

                  return (
                    <li key={productId || `row-${index}`} className="entity-list-item">
                      <div>
                        <strong>{title || "Без названия"}</strong>
                        <p>ID: {productId || "n/a"}</p>
                        <p>Цена: {price || "n/a"}</p>
                      </div>
                      <div className="inline-actions">
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={!productId}
                          onClick={() => {
                            setEditProductId(productId || "");
                            setEditProductTitle(title || "");
                            setEditProductPrice(price || "");
                            setStatus("Товар загружен в форму редактирования.");
                          }}
                        >
                          Редактировать
                        </button>
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={!productId || deleteProductMutation.isPending}
                          onClick={() => {
                            if (productId) {
                              deleteProductMutation.mutate(productId);
                            }
                          }}
                        >
                          Удалить
                        </button>
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </section>

          <section className="panel-card page-stack">
            <div>
              <h3>Добавить товар</h3>
              <p className="route-hint">Создаёт новый товар через `products.create`.</p>
            </div>
            <div className="stacked-block">
              <label className="field">
                <span>Название</span>
                <input
                  className="input"
                  value={newProductTitle}
                  onChange={(event) => setNewProductTitle(event.target.value)}
                  placeholder="Название товара"
                />
              </label>
              <label className="field">
                <span>Цена</span>
                <input
                  className="input"
                  value={newProductPrice}
                  onChange={(event) => setNewProductPrice(event.target.value)}
                  placeholder="100"
                />
              </label>
              <button
                type="button"
                className="button button-primary"
                disabled={createProductMutation.isPending || !selectedAccountId}
                onClick={() => createProductMutation.mutate()}
              >
                Добавить товар
              </button>
            </div>

            <div className="panel-card panel-soft">
              <h3>Редактор товара</h3>
              <div className="stacked-block">
                <label className="field">
                  <span>Product ID</span>
                  <input
                    className="input"
                    value={editProductId}
                    onChange={(event) => setEditProductId(event.target.value)}
                    placeholder="Выберите товар из списка"
                  />
                </label>
                <label className="field">
                  <span>Новое название</span>
                  <input
                    className="input"
                    value={editProductTitle}
                    onChange={(event) => setEditProductTitle(event.target.value)}
                    placeholder="Название товара"
                  />
                </label>
                <label className="field">
                  <span>Новая цена</span>
                  <input
                    className="input"
                    value={editProductPrice}
                    onChange={(event) => setEditProductPrice(event.target.value)}
                    placeholder="100"
                  />
                </label>
                <button
                  type="button"
                  className="button button-primary"
                  disabled={updateProductMutation.isPending || !selectedAccountId}
                  onClick={() => updateProductMutation.mutate()}
                >
                  Обновить товар
                </button>
              </div>
            </div>

            <p className="route-hint">{status}</p>
          </section>
        </div>
      ) : null}
    </div>
  );
}
