"use client";
/* eslint-disable react-hooks/set-state-in-effect */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import type { ApiSession, Offer, OfferVariantUpsertPayload } from "@/lib/api-client";
import {
  createOfferRequest,
  listOffersRequest,
  replaceOfferVariantsRequest,
  runAccountActionRequest,
} from "@/lib/api-client";
import { hasPermission, projectPermissions, type ProjectRole } from "@/lib/rbac";
import { extractObjectRows, isRecord, readFirstString } from "@/lib/worker-result";

interface ProjectOffersPanelProps {
  apiSession: ApiSession;
  projectId: string;
  currentRole: ProjectRole;
}

interface VariantDraftRow extends OfferVariantUpsertPayload {
  id: string;
}

interface PlatformProductOption {
  productId: string;
  title: string;
  description: string;
  price: number;
  currency: string;
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

function resolveProductCurrency(row: Record<string, unknown>) {
  const direct = readFirstString(row, ["currency", "currencyCode", "priceCurrency"]);
  if (direct) {
    return direct.toUpperCase();
  }

  if (isRecord(row.price)) {
    const nestedCurrency = readFirstString(row.price, ["currency", "code"]);
    if (nestedCurrency) {
      return nestedCurrency.toUpperCase();
    }
  }

  return "RUB";
}

function resolveProductPrice(row: Record<string, unknown>) {
  const direct = readFirstNumber(row, ["price", "amount", "cost"]);
  if (direct !== null) {
    return direct;
  }

  if (isRecord(row.price)) {
    const nestedAmount = readFirstNumber(row.price, ["amount", "value", "price"]);
    if (nestedAmount !== null) {
      return nestedAmount;
    }
  }

  return 0;
}

function resolveProductDescription(row: Record<string, unknown>) {
  return readFirstString(row, ["description", "details", "body", "text", "summary"]);
}

function toPlatformProductOptions(result: Record<string, unknown> | null | undefined) {
  const rows = extractObjectRows(result ?? undefined, ["items", "products", "listings"]);
  const map = new Map<string, PlatformProductOption>();

  for (const row of rows) {
    const productId = readFirstString(row, ["productId", "id"]);
    if (!productId) {
      continue;
    }

    const title = readFirstString(row, ["title", "name", "displayName"]) || productId;
    map.set(productId, {
      productId,
      title,
      description: resolveProductDescription(row),
      price: resolveProductPrice(row),
      currency: resolveProductCurrency(row),
    });
  }

  return Array.from(map.values()).sort((left, right) => left.title.localeCompare(right.title));
}

function toVariantDraftRows(offer: Offer | null): VariantDraftRow[] {
  if (!offer) {
    return [];
  }

  return offer.variants.map((variant) => ({
    id: variant.id,
    accountId: variant.accountId,
    workerProductId: variant.workerProductId,
    platform: variant.platform,
    observedTitle: variant.observedTitle,
    observedDescription: variant.observedDescription ?? "",
    observedPrice: variant.observedPrice,
    observedCurrency: variant.observedCurrency,
    priority: variant.priority,
    isActive: variant.isActive,
  }));
}

export function ProjectOffersPanel({ apiSession, projectId, currentRole }: ProjectOffersPanelProps) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState(
    "Создайте Offer и свяжите с variants из аккаунтов проекта.",
  );
  const [newOfferName, setNewOfferName] = useState("");
  const [newOfferDescription, setNewOfferDescription] = useState("");
  const [selectedOfferId, setSelectedOfferId] = useState("");
  const [variantRows, setVariantRows] = useState<VariantDraftRow[]>([]);

  const [variantPlatformFilter, setVariantPlatformFilter] = useState("");
  const [variantAccountId, setVariantAccountId] = useState("");
  const [variantProductId, setVariantProductId] = useState("");
  const [variantWorkerProductId, setVariantWorkerProductId] = useState("");
  const [variantPlatform, setVariantPlatform] = useState("");
  const [variantTitle, setVariantTitle] = useState("");
  const [variantDescription, setVariantDescription] = useState("");
  const [variantPrice, setVariantPrice] = useState("0");
  const [variantCurrency, setVariantCurrency] = useState("RUB");
  const [variantPriority, setVariantPriority] = useState("10");
  const canManageOffers = hasPermission(currentRole, projectPermissions.offersManage);

  const offersQuery = useQuery({
    queryKey: ["offers", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listOffersRequest(apiSession, projectId),
    staleTime: 10_000,
    enabled: canManageOffers,
  });

  const {
    accounts,
    isLoading: accountsLoading,
    error: accountsError,
  } = useProjectAccounts(apiSession, projectId);

  const accountById = useMemo(
    () => new Map(accounts.map((account) => [account.id, account])),
    [accounts],
  );
  const platformOptions = useMemo(
    () => Array.from(new Set(accounts.map((account) => account.platform))).sort((left, right) => left.localeCompare(right)),
    [accounts],
  );
  const scopedAccounts = useMemo(
    () => (variantPlatformFilter ? accounts.filter((account) => account.platform === variantPlatformFilter) : accounts),
    [accounts, variantPlatformFilter],
  );

  const accountProductsQuery = useQuery({
    queryKey: ["offers-products.list", apiSession.baseUrl, apiSession.token, projectId, variantAccountId],
    queryFn: () => runAccountActionRequest(apiSession, variantAccountId, "products.list", { limit: 200 }),
    enabled: canManageOffers && selectedOfferId.length > 0 && variantAccountId.length > 0,
    staleTime: 15_000,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });
  const productOptions = useMemo(
    () => toPlatformProductOptions(isRecord(accountProductsQuery.data) ? accountProductsQuery.data : null),
    [accountProductsQuery.data],
  );
  const selectedProduct = useMemo(
    () => productOptions.find((product) => product.productId === variantProductId) ?? null,
    [productOptions, variantProductId],
  );
  const currencyOptions = useMemo(
    () => Array.from(new Set(["RUB", "USD", "EUR", ...productOptions.map((item) => item.currency.toUpperCase())])),
    [productOptions],
  );

  const offers = useMemo(() => offersQuery.data ?? [], [offersQuery.data]);
  const selectedOffer = useMemo(
    () => offers.find((offer) => offer.id === selectedOfferId) ?? null,
    [offers, selectedOfferId],
  );

  useEffect(() => {
    if (selectedOfferId && offers.some((offer) => offer.id === selectedOfferId)) {
      return;
    }

    const first = offers[0];
    if (!first) {
      if (selectedOfferId !== "") {
        setSelectedOfferId("");
      }
      setVariantRows((current) => (current.length === 0 ? current : []));
      return;
    }

    setSelectedOfferId(first.id);
  }, [offers, selectedOfferId]);

  useEffect(() => {
    setVariantRows(toVariantDraftRows(selectedOffer));
  }, [selectedOffer]);

  useEffect(() => {
    if (platformOptions.length === 0) {
      if (variantPlatformFilter) {
        setVariantPlatformFilter("");
      }
      return;
    }

    if (!variantPlatformFilter || !platformOptions.includes(variantPlatformFilter)) {
      setVariantPlatformFilter(platformOptions[0]);
    }
  }, [platformOptions, variantPlatformFilter]);

  useEffect(() => {
    if (scopedAccounts.length === 0) {
      if (variantAccountId) {
        setVariantAccountId("");
      }
      return;
    }

    if (!variantAccountId || !scopedAccounts.some((account) => account.id === variantAccountId)) {
      setVariantAccountId(scopedAccounts[0].id);
    }
  }, [scopedAccounts, variantAccountId]);

  useEffect(() => {
    if (productOptions.length === 0) {
      if (variantProductId) {
        setVariantProductId("");
      }
      return;
    }

    if (!variantProductId || !productOptions.some((product) => product.productId === variantProductId)) {
      setVariantProductId(productOptions[0].productId);
    }
  }, [productOptions, variantProductId]);

  useEffect(() => {
    if (!selectedProduct || !variantAccountId) {
      return;
    }

    const account = accountById.get(variantAccountId);
    setVariantWorkerProductId(selectedProduct.productId);
    setVariantTitle(selectedProduct.title);
    setVariantDescription(selectedProduct.description);
    setVariantPrice(String(selectedProduct.price));
    setVariantCurrency(selectedProduct.currency.toUpperCase());
    setVariantPlatform(account?.platform ?? variantPlatformFilter);
  }, [accountById, selectedProduct, variantAccountId, variantPlatformFilter]);

  const createOfferMutation = useMutation({
    mutationFn: () =>
      createOfferRequest(apiSession, projectId, {
        name: newOfferName.trim(),
        description: newOfferDescription.trim() || undefined,
      }),
    onSuccess: async (offer) => {
      await queryClient.invalidateQueries({
        queryKey: ["offers", apiSession.baseUrl, apiSession.token, projectId],
      });
      setSelectedOfferId(offer.id);
      setNewOfferName("");
      setNewOfferDescription("");
      setStatus("Offer создан.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось создать Offer.");
    },
  });

  const saveVariantsMutation = useMutation({
    mutationFn: async () => {
      if (!selectedOfferId) {
        throw new Error("Выберите Offer для сохранения variants.");
      }

      const payload: OfferVariantUpsertPayload[] = variantRows.map((row) => ({
        accountId: row.accountId,
        workerProductId: row.workerProductId.trim(),
        platform: row.platform.trim(),
        observedTitle: row.observedTitle.trim(),
        observedDescription: row.observedDescription?.trim() || undefined,
        observedPrice: row.observedPrice,
        observedCurrency: row.observedCurrency.trim().toUpperCase(),
        priority: row.priority,
        isActive: row.isActive,
      }));

      return replaceOfferVariantsRequest(apiSession, projectId, selectedOfferId, payload);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["offers", apiSession.baseUrl, apiSession.token, projectId],
      });
      setStatus("Variants сохранены.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось сохранить variants.");
    },
  });

  const appendVariant = () => {
    const numericPrice = Number(variantPrice.trim());
    const numericPriority = Number(variantPriority.trim());
    if (!variantAccountId.trim()) {
      setStatus("Выберите account для variant.");
      return;
    }

    if (!variantProductId.trim()) {
      setStatus("Выберите product из платформы.");
      return;
    }

    if (!variantWorkerProductId.trim() || !variantTitle.trim()) {
      setStatus("Не удалось подтянуть поля variant. Выберите product повторно.");
      return;
    }

    if (!Number.isFinite(numericPrice) || numericPrice < 0) {
      setStatus("Observed price должен быть неотрицательным числом.");
      return;
    }

    if (!Number.isFinite(numericPriority)) {
      setStatus("Priority должен быть числом.");
      return;
    }

    if (variantRows.some((row) => row.accountId === variantAccountId.trim() && row.workerProductId === variantWorkerProductId.trim())) {
      setStatus("Этот product уже добавлен в variants для выбранного account.");
      return;
    }

    setVariantRows((current) => [
      ...current,
      {
        id:
          typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
            ? crypto.randomUUID()
            : `${Date.now()}-${current.length + 1}`,
        accountId: variantAccountId.trim(),
        workerProductId: variantWorkerProductId.trim(),
        platform: variantPlatform.trim(),
        observedTitle: variantTitle.trim(),
        observedDescription: variantDescription.trim(),
        observedPrice: numericPrice,
        observedCurrency: variantCurrency.trim().toUpperCase(),
        priority: Math.floor(numericPriority),
        isActive: true,
      },
    ]);
    setVariantWorkerProductId("");
    setVariantTitle(selectedProduct?.title ?? "");
    setVariantDescription(selectedProduct?.description ?? "");
    setVariantPrice(selectedProduct ? String(selectedProduct.price) : "0");
    setVariantPriority("10");
    setStatus("Variant добавлен в draft.");
  };

  if (!canManageOffers) {
    return (
      <div className="panel-card page-stack" data-testid="project-offers-panel-no-access">
        <h2>Offers</h2>
        <p className="route-error">
          У текущей роли нет permission `project.offers.manage`.
        </p>
      </div>
    );
  }

  return (
    <div className="page-stack" data-testid="project-offers-panel">
      <header className="page-section-header">
        <h2>Offers</h2>
        <p>Offer объединяет варианты товара с разных аккаунтов в одну CRM-сущность.</p>
      </header>

      <section className="panel-card page-stack">
        <h3>Новый Offer</h3>
        <div className="grid-2">
          <label className="field">
            <span>Name</span>
            <input
              className="input"
              value={newOfferName}
              onChange={(event) => setNewOfferName(event.target.value)}
              placeholder="Например: Steam Prime Rental"
            />
          </label>
          <label className="field">
            <span>Description</span>
            <input
              className="input"
              value={newOfferDescription}
              onChange={(event) => setNewOfferDescription(event.target.value)}
              placeholder="Короткое описание оффера"
            />
          </label>
        </div>
        <div className="panel-actions">
          <button
            type="button"
            className="button"
            disabled={createOfferMutation.isPending || !newOfferName.trim()}
            onClick={() => createOfferMutation.mutate()}
          >
            Создать Offer
          </button>
        </div>
      </section>

      <section className="panel-card page-stack">
        <h3>Список Offer</h3>
        {offersQuery.isPending ? <p className="route-hint">Загружаем offers...</p> : null}
        {offersQuery.error ? (
          <p className="route-error">
            {offersQuery.error instanceof Error ? offersQuery.error.message : "Не удалось загрузить offers."}
          </p>
        ) : null}
        {!offersQuery.isPending && !offersQuery.error && offers.length === 0 ? (
          <p className="route-hint">Пока нет Offer. Создайте первый оффер выше.</p>
        ) : null}
        {offers.length > 0 ? (
          <div className="grid-2">
            {offers.map((offer) => (
              <button
                key={offer.id}
                type="button"
                className={`panel-card text-left ${selectedOfferId === offer.id ? "is-active" : ""}`}
                onClick={() => setSelectedOfferId(offer.id)}
              >
                <strong>{offer.name}</strong>
                <p className="route-hint">status: {offer.status}</p>
                <p className="route-hint">
                  price: {offer.minPrice ?? "-"}..{offer.maxPrice ?? "-"} ({offer.currencies.join(", ") || "-"})
                </p>
                <p className="route-hint">variants: {offer.variantCount}</p>
              </button>
            ))}
          </div>
        ) : null}
      </section>

      <section className="panel-card page-stack">
        <h3>Variants</h3>
        {!selectedOffer ? <p className="route-hint">Выберите Offer из списка.</p> : null}
        {selectedOffer ? (
          <>
            <p className="route-hint">Offer: {selectedOffer.name}</p>
            {accountsLoading ? <p className="route-hint">Загружаем аккаунты...</p> : null}
            {accountsError ? (
              <p className="route-error">
                {accountsError instanceof Error ? accountsError.message : "Не удалось загрузить аккаунты."}
              </p>
            ) : null}

            <div className="grid-3">
              <label className="field">
                <span>Platform</span>
                <select
                  className="input"
                  value={variantPlatformFilter}
                  onChange={(event) => setVariantPlatformFilter(event.target.value)}
                >
                  {platformOptions.map((platform) => (
                    <option key={platform} value={platform}>{platform}</option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Account</span>
                <select
                  className="input"
                  value={variantAccountId}
                  onChange={(event) => setVariantAccountId(event.target.value)}
                >
                  {scopedAccounts.length === 0 ? <option value="">Нет аккаунтов на платформе</option> : null}
                  {scopedAccounts.map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.displayName} ({account.businessStatus})
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Product</span>
                <select
                  className="input"
                  value={variantProductId}
                  onChange={(event) => setVariantProductId(event.target.value)}
                  disabled={accountProductsQuery.isPending || productOptions.length === 0}
                >
                  {productOptions.length === 0 ? <option value="">Нет товаров</option> : null}
                  {productOptions.map((product) => (
                    <option key={product.productId} value={product.productId}>
                      {product.title} · {product.productId}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            {accountProductsQuery.isPending ? <p className="route-hint">Загружаем товары выбранного аккаунта...</p> : null}
            {accountProductsQuery.error ? (
              <p className="route-error">
                {accountProductsQuery.error instanceof Error ? accountProductsQuery.error.message : "Не удалось загрузить товары аккаунта."}
              </p>
            ) : null}

            {selectedProduct ? (
              <article className="stacked-block">
                <strong>{selectedProduct.title}</strong>
                <p className="route-hint">productId: {selectedProduct.productId}</p>
                <p className="route-hint">price: {selectedProduct.price} {selectedProduct.currency}</p>
                <p className="route-hint">{selectedProduct.description || "Без описания"}</p>
              </article>
            ) : null}

            <details className="details-block">
              <summary>Ручная корректировка variant (опционально)</summary>
              <div className="page-stack">
                <div className="grid-2">
                  <label className="field">
                    <span>Worker product ID</span>
                    <input
                      className="input"
                      value={variantWorkerProductId}
                      onChange={(event) => setVariantWorkerProductId(event.target.value)}
                    />
                  </label>
                  <label className="field">
                    <span>Observed title</span>
                    <input
                      className="input"
                      value={variantTitle}
                      onChange={(event) => setVariantTitle(event.target.value)}
                    />
                  </label>
                </div>
                <label className="field">
                  <span>Observed description</span>
                  <input
                    className="input"
                    value={variantDescription}
                    onChange={(event) => setVariantDescription(event.target.value)}
                  />
                </label>
                <div className="grid-3">
                  <label className="field">
                    <span>Platform</span>
                    <input
                      className="input"
                      value={variantPlatform}
                      onChange={(event) => setVariantPlatform(event.target.value)}
                    />
                  </label>
                  <label className="field">
                    <span>Observed price</span>
                    <input
                      className="input"
                      value={variantPrice}
                      onChange={(event) => setVariantPrice(event.target.value)}
                    />
                  </label>
                  <label className="field">
                    <span>Currency</span>
                    <select
                      className="input"
                      value={variantCurrency}
                      onChange={(event) => setVariantCurrency(event.target.value)}
                    >
                      {currencyOptions.map((currency) => (
                        <option key={currency} value={currency}>{currency}</option>
                      ))}
                    </select>
                  </label>
                </div>
              </div>
            </details>

            <div className="grid-2">
              <label className="field">
                <span>Priority</span>
                <select className="input" value={variantPriority} onChange={(event) => setVariantPriority(event.target.value)}>
                  <option value="1">1 (highest)</option>
                  <option value="5">5</option>
                  <option value="10">10</option>
                  <option value="20">20</option>
                  <option value="50">50</option>
                  <option value="100">100</option>
                </select>
              </label>
              <div className="field align-end">
                <button
                  type="button"
                  className="button button-ghost"
                  onClick={appendVariant}
                  disabled={!variantAccountId || !variantProductId}
                >
                  Добавить variant
                </button>
              </div>
            </div>

            {variantRows.length === 0 ? (
              <p className="route-hint">Для Offer пока нет variants.</p>
            ) : (
              <div className="page-stack">
                {variantRows.map((row, index) => (
                  <article key={row.id} className="panel-card">
                    <strong>
                      #{index + 1} {row.observedTitle}
                    </strong>
                    <p className="route-hint">
                      {row.platform} · {row.workerProductId} · {row.observedPrice} {row.observedCurrency}
                    </p>
                    <p className="route-hint">
                      account: {accountById.get(row.accountId)?.displayName ?? row.accountId}, priority: {row.priority}
                    </p>
                    <button
                      type="button"
                      className="button button-ghost"
                      onClick={() => setVariantRows((current) => current.filter((item) => item.id !== row.id))}
                    >
                      Удалить
                    </button>
                  </article>
                ))}
              </div>
            )}

            <div className="panel-actions">
              <button
                type="button"
                className="button"
                disabled={saveVariantsMutation.isPending}
                onClick={() => saveVariantsMutation.mutate()}
              >
                Сохранить variants
              </button>
            </div>
          </>
        ) : null}
      </section>

      <p className="route-hint">{status}</p>
    </div>
  );
}
