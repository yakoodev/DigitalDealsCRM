"use client";

import { useMutation, useQueries, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import Link from "next/link";
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

type StatusTone = "ok" | "warn" | "info" | "bad";

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

function readStatusKey(status: string) {
  return status.trim().toLowerCase();
}

function resolveStatusTone(status: string): StatusTone {
  const normalized = readStatusKey(status);
  if (!normalized) {
    return "info";
  }

  if (normalized.includes("error") || normalized.includes("fail") || normalized.includes("broken")) {
    return "bad";
  }

  if (
    normalized.includes("warn")
    || normalized.includes("review")
    || normalized.includes("token")
  ) {
    return "warn";
  }

  if (
    normalized.includes("active")
    || normalized.includes("online")
    || normalized.includes("enabled")
    || normalized.includes("ok")
  ) {
    return "ok";
  }

  return "info";
}

function resolveStock(row: Record<string, unknown>) {
  const direct = readFirstNumber(row, ["stock", "balance", "quantity", "available"]);
  if (direct === null) {
    return null;
  }

  return direct;
}

function resolveSales(row: Record<string, unknown>) {
  return readFirstNumber(row, ["sales", "sold", "orders"]) ?? null;
}

function resolveOfferCount(row: Record<string, unknown>) {
  return readFirstNumber(row, ["offersCount", "offers", "variants"]) ?? null;
}

export function ProjectProductsPanel({ apiSession, projectId }: ProjectProductsPanelProps) {
  const queryClient = useQueryClient();
  const [accountFilterId, setAccountFilterId] = useState("all");
  const [productSearch, setProductSearch] = useState("");
  const [platformFilter, setPlatformFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState("all");
  const [selectedProductKey, setSelectedProductKey] = useState("");
  const [status, setStatus] = useState("");
  const { modal, accountId: modalAccountId, productId: modalProductId, closeModal, openModal } =
    useRouteModal();

  const {
    accounts,
    selectedAccountId,
    isLoading: accountsLoading,
    error: accountsError,
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
    return aggregatedRows.filter(({ accountId, row }) => {
      const id = readFirstString(row, ["productId", "id"]).toLowerCase();
      const title = readFirstString(row, ["title", "name", "displayName"]).toLowerCase();
      const accountName = (accountNameById.get(accountId) ?? "").toLowerCase();
      const accountPlatform =
        accounts.find((account) => account.id === accountId)?.platform.toLowerCase() ?? "";
      const normalizedStatus = readStatusKey(resolveProductStatus(row));
      const passesQuery = !query || id.includes(query) || title.includes(query) || accountName.includes(query);

      if (!passesQuery) {
        return false;
      }

      if (platformFilter !== "all" && accountPlatform !== platformFilter) {
        return false;
      }

      if (statusFilter === "all") {
        return true;
      }

      if (statusFilter === "active") {
        return normalizedStatus.includes("active") || normalizedStatus.includes("online") || normalizedStatus.includes("ok");
      }

      if (statusFilter === "paused") {
        return normalizedStatus.includes("pause") || normalizedStatus.includes("draft") || normalizedStatus.includes("inactive");
      }

      if (statusFilter === "error") {
        return normalizedStatus.includes("error") || normalizedStatus.includes("warn") || normalizedStatus.includes("fail");
      }

      return true;
    });
  }, [accountNameById, accounts, aggregatedRows, platformFilter, productSearch, statusFilter]);

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

  const statusSummary = useMemo(() => {
    return aggregatedRows.reduce(
      (acc, entry) => {
        const statusText = resolveProductStatus(entry.row);
        const tone = resolveStatusTone(statusText);

        if (tone === "ok") {
          acc.active += 1;
        } else if (tone === "bad") {
          acc.error += 1;
        } else if (tone === "warn") {
          acc.warn += 1;
        } else {
          acc.paused += 1;
        }

        const stock = resolveStock(entry.row);
        if (typeof stock === "number" && Number.isFinite(stock) && stock <= 0) {
          acc.outOfStock += 1;
        }

        return acc;
      },
      {
        active: 0,
        paused: 0,
        warn: 0,
        error: 0,
        outOfStock: 0,
      },
    );
  }, [aggregatedRows]);

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
      <section className="page-head">
        <div className="page-head__row">
          <div>
            <h1 className="page-title">Товары</h1>
          </div>
          <div className="inline">
            <button
              type="button"
              className="button button-ghost button-small"
              onClick={refreshAllProducts}
              disabled={anyFetching || scopedAccountIds.length === 0}
            >
              Обновить
            </button>
            <button
              type="button"
              className="button button-primary button-small"
              onClick={() => openModal("create", { accountId: targetAccountId })}
            >
              ＋ Создать товар
            </button>
          </div>
        </div>
        {status ? <div className="status status--info">{status}</div> : null}
      </section>

      <section className="compact-kpi-grid" aria-label="Сводка товаров">
        <article className="compact-kpi">
          <div className="compact-kpi__value">{aggregatedRows.length}</div>
          <div className="compact-kpi__label">всего товаров</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__value">{statusSummary.active}</div>
          <div className="compact-kpi__label">активные</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__value">{statusSummary.paused + statusSummary.warn}</div>
          <div className="compact-kpi__label">на паузе/проверке</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__value">{statusSummary.outOfStock}</div>
          <div className="compact-kpi__label">закончились</div>
        </article>
        <article className="compact-kpi">
          <div className="compact-kpi__value">{statusSummary.error}</div>
          <div className="compact-kpi__label">с ошибками</div>
        </article>
      </section>

      <section className="card mb-4" aria-label="Фильтры товаров">
        <div className="product-filter">
          <label className="field">
            <span>Поиск</span>
            <input
              className="input"
              value={productSearch}
              onChange={(event) => setProductSearch(event.target.value)}
              placeholder="Название, ID, аккаунт"
            />
          </label>
          <label className="field">
            <span>Аккаунт</span>
            <select
              className="input"
              value={effectiveFilterId}
              onChange={(event) => setAccountFilterId(event.target.value)}
            >
              <option value="all">Все аккаунты</option>
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.displayName}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Площадка</span>
            <select
              className="input"
              value={platformFilter}
              onChange={(event) => setPlatformFilter(event.target.value)}
            >
              <option value="all">Все площадки</option>
              {[...new Set(accounts.map((account) => account.platform.toLowerCase()))].map((platform) => (
                <option key={`platform-${platform}`} value={platform}>
                  {platform}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Статус</span>
            <select
              className="input"
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value)}
            >
              <option value="all">Все статусы</option>
              <option value="active">Активные</option>
              <option value="paused">На паузе</option>
              <option value="error">С ошибками</option>
            </select>
          </label>
          <button
            type="button"
            className="button"
            onClick={refreshAllProducts}
            disabled={anyFetching || scopedAccountIds.length === 0}
          >
            Применить
          </button>
        </div>
      </section>

      <section className="products-layout">
        <div className="stack">
          <section className="card">
            <div className="card__head">
              <div>
                <h2 className="card__title">Список товаров</h2>
                <div className="card__meta">Карточки компактные, чтобы быстро смотреть статус и ключевые поля.</div>
              </div>
              <div className="inline">
                <button type="button" className="button button-ghost button-small">
                  Массовые действия
                </button>
                <button type="button" className="button button-ghost button-small">
                  Экспорт
                </button>
              </div>
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
              <div className="product-list">
                {filteredRows.map(({ accountId, row }, index) => {
                  const productId = readFirstString(row, ["productId", "id"]);
                  const title = readFirstString(row, ["title", "name", "displayName"]);
                  const price = resolveProductPrice(row);
                  const accountName = accountNameById.get(accountId) ?? accountId;
                  const statusLabel = resolveProductStatus(row) || "n/a";
                  const statusTone = resolveStatusTone(statusLabel);
                  const stock = resolveStock(row);
                  const sales = resolveSales(row);
                  const offersCount = resolveOfferCount(row);
                  const itemKey = `${accountId}:${productId || index}`;

                  return (
                    <article
                      key={itemKey}
                      className={`product-row ${selectedProductKey === itemKey ? "is-selected" : ""}`}
                    >
                      <div className="product-main">
                        <div className="product-head">
                          <h3 className="product-title">{title || "Без названия"}</h3>
                          <span
                            className={`status ${statusTone === "ok"
                              ? "status--ok"
                              : statusTone === "warn"
                                ? "status--warn"
                                : statusTone === "bad"
                                  ? "status--bad"
                                  : "status--info"}`}
                          >
                            {statusLabel}
                          </span>
                          <span className="badge">{accountName}</span>
                        </div>
                        <div className="product-meta">
                          ID: {productId || "n/a"} · {accountName}
                        </div>
                        <div className="product-stats">
                          <span className="chip">Цена: {price || "n/a"}</span>
                          <span className="chip">Остаток: {stock === null ? "n/a" : formatNumber(stock)}</span>
                          <span className="chip">Продаж: {sales === null ? "n/a" : formatNumber(sales)}</span>
                          <span className="chip">Офферы: {offersCount === null ? "n/a" : formatNumber(offersCount)}</span>
                        </div>
                      </div>

                      <div className="product-actions">
                        <button
                          type="button"
                          className="button button-ghost button-small"
                          onClick={() => setSelectedProductKey(itemKey)}
                        >
                          Выбрать
                        </button>
                        <button
                          type="button"
                          className="button button-ghost button-small"
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
                          className="button button-ghost button-small"
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
                    </article>
                  );
                })}
              </div>
            )}
          </section>
        </div>
        <aside className="stack sticky-side">
          <section className="card stack">
            <div>
              <h2 className="card__title">Контекст выборки</h2>
              <div className="card__meta">Краткая информация по текущему списку</div>
            </div>
            <div className="side-list">
              <div className="side-row">
                <div className="summary-line">
                  <span>Текущий фильтр</span>
                  <strong>
                    {statusFilter === "all" ? "Все товары" : statusFilter}
                  </strong>
                </div>
              </div>
              <div className="side-row">
                <div className="summary-line">
                  <span>Аккаунтов в выборке</span>
                  <strong>{scopedAccountIds.length}</strong>
                </div>
              </div>
              <div className="side-row">
                <div className="summary-line">
                  <span>Средняя цена</span>
                  <strong>{averagePrice === null ? "n/a" : `${averagePrice.toFixed(2)} RUB`}</strong>
                </div>
              </div>
              <div className="side-row">
                <div className="summary-line">
                  <span>Требуют внимания</span>
                  <strong>{statusSummary.warn + statusSummary.error}</strong>
                </div>
              </div>
            </div>
          </section>

          <section className="card stack">
            <div>
              <h2 className="card__title">Действия</h2>
              <div className="card__meta">Для выбранных или отфильтрованных товаров</div>
            </div>
            <button
              type="button"
              className="button button-primary w-full"
              onClick={() => openModal("create", { accountId: targetAccountId })}
            >
              Создать товар
            </button>
            <button
              type="button"
              className="button w-full"
              onClick={refreshAllProducts}
              disabled={anyFetching || scopedAccountIds.length === 0}
            >
              Синхронизировать
            </button>
            {accountsError ? <p className="route-error">{accountsError.message}</p> : null}
            {accountsLoading ? <p className="route-hint">Загружаем аккаунты...</p> : null}
            {selectedProduct ? (
              <>
                <h3>Выбранный товар</h3>
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
                <div className="split">
                  <Link
                    className="button button-ghost button-small"
                    href={`/projects/${projectId}/products?modal=edit&accountId=${selectedProduct.accountId}&productId=${readFirstString(selectedProduct.row, ["productId", "id"])}`}
                  >
                    Редактировать
                  </Link>
                  <Link className="button button-ghost button-small" href={`/projects/${projectId}/offers`}>
                    К офферам
                  </Link>
                </div>
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
          </section>
        </aside>
      </section>

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
