"use client";

import {
  useMutation,
  useQueries,
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

interface ProductRowWithAccount {
  accountId: string;
  row: Record<string, unknown>;
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
  const [accountFilterId, setAccountFilterId] = useState("all");
  const [productSearch, setProductSearch] = useState("");
  const [newProductTitle, setNewProductTitle] = useState("Новый товар");
  const [newProductPrice, setNewProductPrice] = useState("100");
  const [editProductAccountId, setEditProductAccountId] = useState("");
  const [editProductId, setEditProductId] = useState("");
  const [editProductTitle, setEditProductTitle] = useState("");
  const [editProductPrice, setEditProductPrice] = useState("");
  const [status, setStatus] = useState(
    "Товары загружаются по всем аккаунтам проекта. При необходимости включите фильтр аккаунта.",
  );

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

  const productsQueries = useQueries({
    queries: scopedAccountIds.map((accountId) => ({
      queryKey: [
        "products.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        accountId,
      ] as const,
      queryFn: () =>
        runAccountActionRequest(apiSession, accountId, "products.list", {
          limit: 100,
        }),
      enabled: Boolean(accountId),
      refetchInterval: 30_000,
      staleTime: 10_000,
    })),
  });

  const targetAccountId = useMemo(() => {
    if (effectiveFilterId !== "all") {
      return effectiveFilterId;
    }

    return selectedAccountId;
  }, [effectiveFilterId, selectedAccountId]);

  const createProductMutation = useMutation({
    mutationFn: () => {
      const parsedPrice = Number(newProductPrice);
      if (!Number.isFinite(parsedPrice) || parsedPrice < 0) {
        throw new Error("Цена товара должна быть неотрицательным числом.");
      }

      if (!newProductTitle.trim()) {
        throw new Error("Название товара обязательно.");
      }

      if (!targetAccountId) {
        throw new Error("Выберите аккаунт для операции добавления.");
      }

      return runAccountActionRequest(apiSession, targetAccountId, "products.create", {
        title: newProductTitle.trim(),
        price: parsedPrice,
      });
    },
    onSuccess: async () => {
      if (targetAccountId) {
        await queryClient.invalidateQueries({
          queryKey: [
            "products.list",
            apiSession.baseUrl,
            apiSession.token,
            projectId,
            targetAccountId,
          ],
        });
      }

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

      const accountId = editProductAccountId.trim() || targetAccountId;
      if (!accountId) {
        throw new Error("Не удалось определить аккаунт для обновления товара.");
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

      return runAccountActionRequest(apiSession, accountId, "products.update", payload);
    },
    onSuccess: async () => {
      const accountId = editProductAccountId.trim() || targetAccountId;
      if (accountId) {
        await queryClient.invalidateQueries({
          queryKey: [
            "products.list",
            apiSession.baseUrl,
            apiSession.token,
            projectId,
            accountId,
          ],
        });
      }

      setStatus("Товар обновлён.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось обновить товар.");
    },
  });

  const deleteProductMutation = useMutation({
    mutationFn: (variables: { accountId: string; productId: string }) =>
      runAccountActionRequest(apiSession, variables.accountId, "products.delete", {
        productId: variables.productId,
      }),
    onSuccess: async (_result, variables) => {
      await queryClient.invalidateQueries({
        queryKey: [
          "products.list",
          apiSession.baseUrl,
          apiSession.token,
          projectId,
          variables.accountId,
        ],
      });

      setStatus("Товар удалён.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось удалить товар.");
    },
  });

  const aggregatedRows = useMemo<ProductRowWithAccount[]>(() => {
    return scopedAccountIds.flatMap((accountId, index) => {
      const query = productsQueries[index];
      const rows = extractObjectRows(query?.data ?? null, [
        "items",
        "products",
        "listings",
      ]);

      return rows.map((row) => ({
        accountId,
        row,
      }));
    });
  }, [productsQueries, scopedAccountIds]);

  const filteredRows = useMemo(() => {
    const query = productSearch.trim().toLowerCase();
    if (!query) {
      return aggregatedRows;
    }

    return aggregatedRows.filter(({ accountId, row }) => {
      const id = readFirstString(row, ["productId", "id"]).toLowerCase();
      const title = readFirstString(row, ["title", "name", "displayName"]).toLowerCase();
      const accountName = (accountNameById.get(accountId) ?? "").toLowerCase();
      return id.includes(query) || title.includes(query) || accountName.includes(query);
    });
  }, [accountNameById, aggregatedRows, productSearch]);

  const pricedItems = filteredRows
    .map(({ row }) => readFirstNumber(row, ["price", "amount", "cost"]))
    .filter((value): value is number => typeof value === "number");

  const averagePrice =
    pricedItems.length === 0
      ? null
      : pricedItems.reduce((sum, value) => sum + value, 0) / pricedItems.length;

  const anyPending = productsQueries.some((query) => query.isPending);
  const anyFetching = productsQueries.some((query) => query.isFetching);
  const firstError = productsQueries.find((query) => query.error)?.error;

  const refreshAllProducts = async () => {
    await Promise.all(productsQueries.map((query) => query.refetch()));
  };

  return (
    <div className="page-stack" data-testid="project-products-panel">
      <header className="page-section-header">
        <h2>Товары</h2>
        <p>
          Данные собираются со всех аккаунтов проекта (всех доступных worker route), с
          возможностью фильтра по конкретному аккаунту.
        </p>
      </header>

      <section className="summary-grid">
        <article className="summary-card">
          <p>Найдено товаров</p>
          <strong>{filteredRows.length}</strong>
          <small>По текущему фильтру и scope аккаунтов</small>
        </article>
        <article className="summary-card">
          <p>Средняя цена</p>
          <strong>{averagePrice === null ? "n/a" : averagePrice.toFixed(2)}</strong>
          <small>Рассчитано по доступным значениям цены</small>
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
          <span>Поиск по товарам</span>
          <input
            className="input"
            value={productSearch}
            onChange={(event) => setProductSearch(event.target.value)}
            placeholder="ID, название, аккаунт"
          />
        </label>
      </div>

      {scopedAccountIds.length === 0 ? (
        <p className="route-hint">Добавьте аккаунт в проект, чтобы загрузить товары.</p>
      ) : anyPending ? (
        <p className="route-hint">Загружаем товары по аккаунтам...</p>
      ) : firstError ? (
        <p className="route-error">
          {firstError instanceof Error
            ? firstError.message
            : "Не удалось загрузить товары по одному из аккаунтов."}
        </p>
      ) : (
        <div className="split-grid">
          <section className="panel-card">
            <div className="panel-title-row">
              <h3>Список товаров</h3>
              <button
                type="button"
                className="button button-ghost"
                onClick={refreshAllProducts}
                disabled={anyFetching || scopedAccountIds.length === 0}
              >
                Обновить
              </button>
            </div>
            {anyFetching ? <p className="route-hint">Синхронизация товаров...</p> : null}
            {filteredRows.length === 0 ? (
              <p className="route-hint">Товары не найдены для выбранной выборки аккаунтов.</p>
            ) : (
              <ul className="entity-list">
                {filteredRows.map(({ accountId, row }, index) => {
                  const productId = readFirstString(row, ["productId", "id"]);
                  const title = readFirstString(row, ["title", "name", "displayName"]);
                  const price = toReadableValue(row.price ?? row.amount ?? row.cost ?? "");
                  const accountName = accountNameById.get(accountId) ?? accountId;

                  return (
                    <li key={`${accountId}:${productId || index}`} className="entity-list-item">
                      <div>
                        <strong>{title || "Без названия"}</strong>
                        <p>ID: {productId || "n/a"}</p>
                        <p>Цена: {price || "n/a"}</p>
                        <small>Аккаунт: {accountName}</small>
                      </div>
                      <div className="inline-actions">
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={!productId}
                          onClick={() => {
                            setEditProductAccountId(accountId);
                            setEditProductId(productId || "");
                            setEditProductTitle(title || "");
                            setEditProductPrice(price || "");
                            setStatus(
                              `Товар загружен в форму редактирования (аккаунт: ${accountName}).`,
                            );
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
                              deleteProductMutation.mutate({
                                accountId,
                                productId,
                              });
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
              <p className="route-hint">
                Создаёт новый товар через `products.create` в аккаунте:
                <strong> {accountNameById.get(targetAccountId) ?? "не выбран"}</strong>.
              </p>
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
                disabled={createProductMutation.isPending || !targetAccountId}
                onClick={() => createProductMutation.mutate()}
              >
                Добавить товар
              </button>
            </div>

            <div className="panel-card panel-soft">
              <h3>Редактор товара</h3>
              <div className="stacked-block">
                <label className="field">
                  <span>Аккаунт товара</span>
                  <input
                    className="input"
                    value={accountNameById.get(editProductAccountId) ?? editProductAccountId}
                    readOnly
                  />
                </label>
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
                  disabled={updateProductMutation.isPending || !targetAccountId}
                  onClick={() => updateProductMutation.mutate()}
                >
                  Обновить товар
                </button>
              </div>
            </div>

            <p className="route-hint">{status}</p>
          </section>
        </div>
      )}
    </div>
  );
}
