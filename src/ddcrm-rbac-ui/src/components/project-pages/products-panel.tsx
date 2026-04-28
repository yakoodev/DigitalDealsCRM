"use client";

import { useMutation, useQueries, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { AccountSelector } from "@/components/account-selector";
import { ModulePageShell } from "@/components/layout/module-page-shell";
import { RouteModalHost } from "@/components/layout/route-modal-host";
import { ProjectProductCreatePanel } from "@/components/project-pages/product-create-panel";
import { ProjectProductEditPanel } from "@/components/project-pages/product-edit-panel";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import { useRouteModal } from "@/hooks/use-route-modal";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import { extractObjectRows, isRecord, readFirstString, toReadableValue } from "@/lib/worker-result";

interface ProjectProductsPanelProps {
  apiSession: ApiSession;
  projectId: string;
}

interface ProductRowWithAccount {
  accountId: string;
  row: Record<string, unknown>;
}

interface AccountQueryError {
  accountId: string;
  message: string;
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

function resolveProductPrice(row: Record<string, unknown>) {
  const direct = readFirstNumber(row, ["price", "amount", "cost"]);
  if (direct !== null) {
    return formatNumber(direct);
  }

  if (isRecord(row.price)) {
    const amount = readFirstNumber(row.price, ["amount", "value", "price"]);
    const currency = readFirstString(row.price, ["currency", "code"]);
    if (amount !== null && currency) {
      return `${formatNumber(amount)} ${currency}`;
    }

    if (amount !== null) {
      return formatNumber(amount);
    }
  }

  return "";
}

function resolveProductStatus(row: Record<string, unknown>) {
  return toReadableValue(row.status ?? row.businessStatus ?? row.state);
}

export function ProjectProductsPanel({ apiSession, projectId }: ProjectProductsPanelProps) {
  const queryClient = useQueryClient();
  const [accountFilterId, setAccountFilterId] = useState("all");
  const [productSearch, setProductSearch] = useState("");
  const [selectedProductKey, setSelectedProductKey] = useState("");
  const [status, setStatus] = useState("");
  const { modal, accountId: modalAccountId, productId: modalProductId, closeModal, openModal } =
    useRouteModal();

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
    setSelectedAccountId,
  } = useProjectAccounts(apiSession, projectId);

  const accountNameById = useMemo(
    () => new Map(accounts.map((account) => [account.id, account.displayName])),
    [accounts],
  );

  const effectiveFilterId = useMemo(() => {
    if (accountFilterId === "all") {
      return "all";
    }

    return accounts.some((account) => account.id === accountFilterId) ? accountFilterId : "all";
  }, [accountFilterId, accounts]);

  const scopedAccountIds = useMemo(() => {
    if (effectiveFilterId !== "all") {
      return [effectiveFilterId];
    }

    return accounts.map((account) => account.id);
  }, [accounts, effectiveFilterId]);

  const productsQueries = useQueries({
    queries: scopedAccountIds.map((accountId) => ({
      queryKey: ["products.list", apiSession.baseUrl, apiSession.token, projectId, accountId] as const,
      queryFn: () => runAccountActionRequest(apiSession, accountId, "products.list", { limit: 100 }),
      enabled: Boolean(accountId),
      refetchInterval: 30_000,
      staleTime: 10_000,
    })),
  });

  const aggregatedRows = useMemo<ProductRowWithAccount[]>(() => {
    return scopedAccountIds.flatMap((accountId, index) => {
      const query = productsQueries[index];
      const rows = extractObjectRows(query?.data ?? null, ["items", "products", "listings"]);
      return rows.map((row) => ({ accountId, row }));
    });
  }, [productsQueries, scopedAccountIds]);

  const accountQueryErrors = useMemo<AccountQueryError[]>(() => {
    return scopedAccountIds.flatMap((accountId, index) => {
      const query = productsQueries[index];
      if (!query?.error) {
        return [];
      }

      return [
        {
          accountId,
          message:
            query.error instanceof Error ? query.error.message : "Не удалось загрузить товары аккаунта.",
        },
      ];
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

  const selectedProduct = useMemo(() => {
    if (!selectedProductKey) {
      return null;
    }

    return (
      filteredRows.find(({ accountId, row }) => {
        const productId = readFirstString(row, ["productId", "id"]);
        return `${accountId}:${productId}` === selectedProductKey;
      }) ?? null
    );
  }, [filteredRows, selectedProductKey]);

  const targetAccountId = effectiveFilterId !== "all" ? effectiveFilterId : selectedAccountId;
  const createProductAccountId = modalAccountId || targetAccountId;
  const editAccountId = modalAccountId;
  const editProductId = modalProductId;

  const deleteProductMutation = useMutation({
    mutationFn: (variables: { accountId: string; productId: string }) =>
      runAccountActionRequest(apiSession, variables.accountId, "products.delete", {
        productId: variables.productId,
      }),
    onSuccess: async (_result, variables) => {
      await queryClient.invalidateQueries({
        queryKey: ["products.list", apiSession.baseUrl, apiSession.token, projectId, variables.accountId],
      });
      setStatus("Товар удалён.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось удалить товар.");
    },
  });

  const pricedItems = filteredRows
    .map(({ row }) => readFirstNumber(row, ["price", "amount", "cost"]))
    .filter((value): value is number => typeof value === "number");

  const averagePrice =
    pricedItems.length === 0 ? null : pricedItems.reduce((sum, value) => sum + value, 0) / pricedItems.length;

  const anyPending = productsQueries.some((query) => query.isPending);
  const anyFetching = productsQueries.some((query) => query.isFetching);

  const refreshAllProducts = async () => {
    await Promise.all(productsQueries.map((query) => query.refetch()));
  };

  const handleProductCreated = async () => {
    if (createProductAccountId) {
      await queryClient.invalidateQueries({
        queryKey: ["products.list", apiSession.baseUrl, apiSession.token, projectId, createProductAccountId],
      });
    }
    closeModal();
  };

  const handleProductEdited = async () => {
    if (editAccountId) {
      await queryClient.invalidateQueries({
        queryKey: ["products.list", apiSession.baseUrl, apiSession.token, projectId, editAccountId],
      });
    }
    closeModal();
  };

  return (
    <div className="page-stack" data-testid="project-products-panel">
      <ModulePageShell
        title="Товары / Products"
        description="Автосбор данных по воркерам, фильтрация по аккаунту и modal-first create/edit без перегруженных экранов."
        actions={(
          <button
            type="button"
            className="button button-primary"
            onClick={() => openModal("create", { accountId: targetAccountId })}
          >
            Добавить товар
          </button>
        )}
        stats={[
          { label: "Товаров", value: String(filteredRows.length), hint: "По текущему фильтру" },
          { label: "Средняя цена", value: averagePrice === null ? "n/a" : averagePrice.toFixed(2) },
          { label: "Аккаунтов", value: String(scopedAccountIds.length), hint: "В выборке" },
        ]}
        main={(
          <section className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Каталог проекта</h3>
              <button
                type="button"
                className="button button-ghost"
                onClick={refreshAllProducts}
                disabled={anyFetching || scopedAccountIds.length === 0}
              >
                Обновить
              </button>
            </div>

            {scopedAccountIds.length === 0 ? (
              <p className="route-hint">Добавьте аккаунт в проект, чтобы загрузить товары.</p>
            ) : anyPending ? (
              <p className="route-hint">Загружаем товары по аккаунтам...</p>
            ) : null}

            {accountQueryErrors.length > 0 ? (
              <section className="status-block status-warning">
                <h4>Часть воркеров недоступна</h4>
                <ul className="entity-list compact-list">
                  {accountQueryErrors.map((entry) => (
                    <li key={`products-error-${entry.accountId}`} className="entity-list-item">
                      <div>
                        <strong>{accountNameById.get(entry.accountId) ?? entry.accountId}</strong>
                        <p className="route-error">{entry.message}</p>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}

            {!anyPending && filteredRows.length === 0 ? (
              <p className="route-hint">Товары не найдены для выбранной выборки.</p>
            ) : (
              <ul className="entity-list">
                {filteredRows.map(({ accountId, row }, index) => {
                  const productId = readFirstString(row, ["productId", "id"]);
                  const title = readFirstString(row, ["title", "name", "displayName"]);
                  const price = resolveProductPrice(row);
                  const accountName = accountNameById.get(accountId) ?? accountId;
                  const itemKey = `${accountId}:${productId || index}`;

                  return (
                    <li
                      key={itemKey}
                      className={`entity-list-item ${selectedProductKey === itemKey ? "is-selected" : ""}`}
                    >
                      <button
                        type="button"
                        className="entity-hitbox"
                        onClick={() => setSelectedProductKey(itemKey)}
                      >
                        <strong>{title || "Без названия"}</strong>
                        <div className="entity-pills">
                          <span className="entity-pill">{accountName}</span>
                          <span className="entity-pill">{productId || "id: n/a"}</span>
                          <span className="entity-pill">Цена: {price || "n/a"}</span>
                        </div>
                        <small>Статус: {resolveProductStatus(row) || "n/a"}</small>
                      </button>

                      <div className="inline-actions">
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={!productId}
                          onClick={() =>
                            openModal("edit", {
                              accountId,
                              productId,
                            })
                          }
                        >
                          Редактировать
                        </button>
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={!productId || deleteProductMutation.isPending}
                          onClick={() => {
                            if (productId) {
                              deleteProductMutation.mutate({ accountId, productId });
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
        )}
        side={(
          <section className="glass-card page-stack">
            <h3>Фильтры и контекст</h3>
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
              <span>Поиск</span>
              <input
                className="input"
                value={productSearch}
                onChange={(event) => setProductSearch(event.target.value)}
                placeholder="название / id / аккаунт"
              />
            </label>

            {selectedProduct ? (
              <>
                <h4>Выбранный товар</h4>
                <dl className="kv-list">
                  <div>
                    <dt>Account</dt>
                    <dd>{accountNameById.get(selectedProduct.accountId) ?? selectedProduct.accountId}</dd>
                  </div>
                  <div>
                    <dt>Название</dt>
                    <dd>{readFirstString(selectedProduct.row, ["title", "name", "displayName"]) || "n/a"}</dd>
                  </div>
                  <div>
                    <dt>Цена</dt>
                    <dd>{resolveProductPrice(selectedProduct.row) || "n/a"}</dd>
                  </div>
                  <div>
                    <dt>Статус</dt>
                    <dd>{resolveProductStatus(selectedProduct.row) || "n/a"}</dd>
                  </div>
                </dl>

                <details className="details-block">
                  <summary>Technical details</summary>
                  <dl className="kv-list">
                    <div>
                      <dt>Product ID</dt>
                      <dd>{readFirstString(selectedProduct.row, ["productId", "id"]) || "n/a"}</dd>
                    </div>
                    <div>
                      <dt>Version</dt>
                      <dd>{toReadableValue(selectedProduct.row.version) || "n/a"}</dd>
                    </div>
                    <div>
                      <dt>Worker request ID</dt>
                      <dd>{toReadableValue(selectedProduct.row.requestId) || "n/a"}</dd>
                    </div>
                  </dl>
                </details>
              </>
            ) : (
              <p className="route-hint">Выберите товар в списке, чтобы увидеть детали.</p>
            )}

            {status ? <p className="route-hint">{status}</p> : null}
          </section>
        )}
      />

      <RouteModalHost
        isOpen={modal === "create"}
        title="Создание товара"
        description="Форма создания товара в выбранном аккаунте."
        onClose={closeModal}
      >
        <ProjectProductCreatePanel
          apiSession={apiSession}
          projectId={projectId}
          preferredAccountId={createProductAccountId}
          mode="modal"
          onCancel={closeModal}
          onCompleted={handleProductCreated}
        />
      </RouteModalHost>

      <RouteModalHost
        isOpen={modal === "edit"}
        title="Редактирование товара"
        description="Измените карточку товара и сохраните без выхода со страницы каталога."
        onClose={closeModal}
      >
        <ProjectProductEditPanel
          apiSession={apiSession}
          projectId={projectId}
          accountId={editAccountId}
          productId={editProductId}
          mode="modal"
          onCancel={closeModal}
          onCompleted={handleProductEdited}
        />
      </RouteModalHost>
    </div>
  );
}
