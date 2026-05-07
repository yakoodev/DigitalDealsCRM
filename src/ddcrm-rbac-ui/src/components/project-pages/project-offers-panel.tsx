"use client";
/* eslint-disable react-hooks/set-state-in-effect */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { RouteModalHost } from "@/components/layout/route-modal-host";
import { ProjectWorkflowsPanel } from "@/components/project-pages/project-workflows-panel";
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

type OfferTab = "overview" | "variants" | "flow" | "history";
type OfferSort = "activity" | "name" | "status";

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

function toOfferStatusTone(status: string) {
  const normalized = status.trim().toLowerCase();
  if (normalized === "active") {
    return "status--ok";
  }

  if (normalized === "draft") {
    return "status--info";
  }

  if (normalized === "review") {
    return "status--warn";
  }

  if (normalized === "paused" || normalized === "disabled") {
    return "status--bad";
  }

  return "status--info";
}

export function ProjectOffersPanel({ apiSession, projectId, currentRole }: ProjectOffersPanelProps) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState("Создайте Offer и свяжите его с variants из аккаунтов проекта.");
  const [activeTab, setActiveTab] = useState<OfferTab>("overview");

  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [variantModalOpen, setVariantModalOpen] = useState(false);

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

  const [offerSearch, setOfferSearch] = useState("");
  const [offerStatusFilter, setOfferStatusFilter] = useState("all");
  const [offerSort, setOfferSort] = useState<OfferSort>("activity");
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

  const offerStatusOptions = useMemo(
    () => Array.from(new Set(offers.map((offer) => offer.status).filter(Boolean))).sort((left, right) => left.localeCompare(right)),
    [offers],
  );

  const filteredOffers = useMemo(() => {
    const query = offerSearch.trim().toLowerCase();
    const normalizedStatusFilter = offerStatusFilter.trim().toLowerCase();

    const scoped = offers.filter((offer) => {
      if (normalizedStatusFilter !== "all" && offer.status.toLowerCase() !== normalizedStatusFilter) {
        return false;
      }

      if (!query) {
        return true;
      }

      const haystack = [
        offer.id,
        offer.name,
        offer.status,
        offer.currencies.join(" "),
      ].join(" ").toLowerCase();
      return haystack.includes(query);
    });

    const sorted = [...scoped];
    if (offerSort === "name") {
      sorted.sort((left, right) => left.name.localeCompare(right.name));
      return sorted;
    }

    if (offerSort === "status") {
      sorted.sort((left, right) => left.status.localeCompare(right.status) || left.name.localeCompare(right.name));
      return sorted;
    }

    sorted.sort((left, right) => right.variantCount - left.variantCount || left.name.localeCompare(right.name));
    return sorted;
  }, [offerSearch, offerSort, offerStatusFilter, offers]);

  const totalVariants = useMemo(
    () => offers.reduce((sum, offer) => sum + offer.variantCount, 0),
    [offers],
  );

  const activeOffersCount = useMemo(
    () => offers.filter((offer) => offer.status.toLowerCase() === "active").length,
    [offers],
  );

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
      setCreateModalOpen(false);
      setNewOfferName("");
      setNewOfferDescription("");
      setStatus("Offer создан.");
      setActiveTab("overview");
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
      return false;
    }

    if (!variantProductId.trim()) {
      setStatus("Выберите product из платформы.");
      return false;
    }

    if (!variantWorkerProductId.trim() || !variantTitle.trim()) {
      setStatus("Не удалось подтянуть поля variant. Выберите product повторно.");
      return false;
    }

    if (!Number.isFinite(numericPrice) || numericPrice < 0) {
      setStatus("Observed price должен быть неотрицательным числом.");
      return false;
    }

    if (!Number.isFinite(numericPriority)) {
      setStatus("Priority должен быть числом.");
      return false;
    }

    if (variantRows.some((row) => row.accountId === variantAccountId.trim() && row.workerProductId === variantWorkerProductId.trim())) {
      setStatus("Этот product уже добавлен в variants для выбранного account.");
      return false;
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
    return true;
  };

  const selectedOfferVariantsCount = variantRows.length;
  const selectedPlatformsCount = new Set(variantRows.map((row) => row.platform.toLowerCase()).filter(Boolean)).size;
  const selectedPriceMin = variantRows.length > 0 ? Math.min(...variantRows.map((row) => row.observedPrice)) : null;
  const selectedPriceMax = variantRows.length > 0 ? Math.max(...variantRows.map((row) => row.observedPrice)) : null;

  const historyRows = [
    {
      id: "run_1",
      title: "Публикация оффера",
      note: selectedOffer ? `Обновлён оффер ${selectedOffer.name}` : "Оффер не выбран",
      status: selectedOffer ? "success" : "idle",
    },
    {
      id: "run_2",
      title: "Синхронизация вариантов",
      note: `Вариантов в draft: ${variantRows.length}`,
      status: variantRows.length > 0 ? "success" : "idle",
    },
    {
      id: "run_3",
      title: "Проверка runtime",
      note: "Invoke Worker -> Buyer Response",
      status: "fallback",
    },
  ] as const;

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
      <section className="page-head">
        <div className="page-head__row">
          <div>
            <h1 className="page-title">Офферы</h1>
            <p className="route-hint">Оффер объединяет товары и flow в единую CRM-сущность.</p>
          </div>
          <div className="inline">
            <button
              type="button"
              className="button button-ghost button-small"
              disabled={offersQuery.isFetching}
              onClick={() => offersQuery.refetch()}
            >
              Проверить
            </button>
            <button
              type="button"
              className="button button-ghost button-small"
              onClick={() => setCreateModalOpen(true)}
            >
              ＋ Создать оффер
            </button>
            <button
              type="button"
              className="button button-primary button-small"
              disabled={saveVariantsMutation.isPending || !selectedOffer}
              onClick={() => saveVariantsMutation.mutate()}
            >
              Сохранить
            </button>
            <button
              type="button"
              className="button button-primary button-small"
              disabled={!selectedOffer}
              onClick={() => setStatus("Оффер опубликован (UI-stage).")}
            >
              Опубликовать
            </button>
          </div>
        </div>
        <div className={`status ${status.toLowerCase().includes("не удалось") ? "status--bad" : "status--info"}`}>
          {status}
        </div>
      </section>

      <section className="state-grid">
        <article className="state-card">
          <span className="label">Офферов</span>
          <strong>{offers.length}</strong>
        </article>
        <article className="state-card">
          <span className="label">Активных</span>
          <strong>{activeOffersCount}</strong>
        </article>
        <article className="state-card">
          <span className="label">Всего variants</span>
          <strong>{totalVariants}</strong>
        </article>
        <article className="state-card">
          <span className="label">Variants выбранного</span>
          <strong>{selectedOfferVariantsCount}</strong>
        </article>
      </section>

      <section className="card">
        <div className="offer-toolbar">
          <label className="field">
            <span>Поиск оффера</span>
            <input
              className="input"
              value={offerSearch}
              onChange={(event) => setOfferSearch(event.target.value)}
              placeholder="Название, ID, статус, валюта"
            />
          </label>
          <label className="field">
            <span>Статус</span>
            <select
              className="input"
              value={offerStatusFilter}
              onChange={(event) => setOfferStatusFilter(event.target.value)}
            >
              <option value="all">Все статусы</option>
              {offerStatusOptions.map((statusOption) => (
                <option key={statusOption} value={statusOption}>
                  {statusOption}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Сортировка</span>
            <select
              className="input"
              value={offerSort}
              onChange={(event) => setOfferSort(event.target.value as OfferSort)}
            >
              <option value="activity">По активности</option>
              <option value="name">По названию</option>
              <option value="status">По статусу</option>
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

      <section className="offer-strip" aria-label="Список офферов">
        {offersQuery.isPending ? <p className="route-hint">Загружаем offers...</p> : null}
        {offersQuery.error ? (
          <p className="route-error">
            {offersQuery.error instanceof Error ? offersQuery.error.message : "Не удалось загрузить offers."}
          </p>
        ) : null}
        {!offersQuery.isPending && !offersQuery.error && filteredOffers.length === 0 ? (
          <p className="route-hint">Офферы не найдены. Создайте первый оффер через модалку.</p>
        ) : null}
        {filteredOffers.map((offer) => (
          <button
            key={offer.id}
            type="button"
            className={`offer-pill ${selectedOfferId === offer.id ? "is-active" : ""}`}
            onClick={() => {
              setSelectedOfferId(offer.id);
              setActiveTab("overview");
            }}
          >
            <div className="offer-pill__head">
              <h3 className="offer-pill__title">{offer.name}</h3>
              <span className={`status ${toOfferStatusTone(offer.status)}`}>{offer.status}</span>
            </div>
            <div className="chip-row">
              <span className="chip">variants: {offer.variantCount}</span>
              <span className="chip">currencies: {offer.currencies.join(", ") || "n/a"}</span>
              <span className="chip">flow v3</span>
            </div>
            <p className="route-hint">{offer.id}</p>
          </button>
        ))}
      </section>

      <nav className="offer-tabs" aria-label="Разделы оффера">
        <button type="button" className={`offer-tab ${activeTab === "overview" ? "is-active" : ""}`} onClick={() => setActiveTab("overview")}>Обзор</button>
        <button type="button" className={`offer-tab ${activeTab === "variants" ? "is-active" : ""}`} onClick={() => setActiveTab("variants")}>Варианты товаров</button>
        <button type="button" className={`offer-tab ${activeTab === "flow" ? "is-active" : ""}`} onClick={() => setActiveTab("flow")}>Flow editor</button>
        <button type="button" className={`offer-tab ${activeTab === "history" ? "is-active" : ""}`} onClick={() => setActiveTab("history")}>История запусков</button>
      </nav>

      {activeTab === "overview" ? (
        <section className="page-stack">
          <section className="offer-metrics-grid">
            <article className="offer-metric-card">
              <div className="offer-metric-card__value">{selectedOfferVariantsCount}</div>
              <div className="offer-metric-card__label">варианта товара</div>
            </article>
            <article className="offer-metric-card">
              <div className="offer-metric-card__value">{selectedPlatformsCount}</div>
              <div className="offer-metric-card__label">площадки</div>
            </article>
            <article className="offer-metric-card">
              <div className="offer-metric-card__value">{selectedOffer ? "1" : "0"}</div>
              <div className="offer-metric-card__label">flow опубликован</div>
            </article>
            <article className="offer-metric-card">
              <div className="offer-metric-card__value">{Math.max(0, selectedOfferVariantsCount * 3)}</div>
              <div className="offer-metric-card__label">запусков за 24ч</div>
            </article>
            <article className="offer-metric-card">
              <div className="offer-metric-card__value">{selectedOfferVariantsCount > 0 ? "0" : "1"}</div>
              <div className="offer-metric-card__label">критических ошибок</div>
            </article>
          </section>

          <section className="offers-overview-grid">
            <article className="card page-stack">
              <div className="card__head">
                <div>
                  <h2 className="card__title">Основные данные</h2>
                  <p className="route-hint">Название и описание единой CRM-сущности.</p>
                </div>
                <span className={`status ${toOfferStatusTone(selectedOffer?.status ?? "draft")}`}>{selectedOffer?.status ?? "draft"}</span>
              </div>
              <div className="summary-list">
                <div className="summary-line"><span>Название</span><strong>{selectedOffer?.name ?? "n/a"}</strong></div>
                <div className="summary-line"><span>ID</span><strong>{selectedOffer?.id ?? "n/a"}</strong></div>
                <div className="summary-line"><span>Вариантов</span><strong>{selectedOfferVariantsCount}</strong></div>
                <div className="summary-line"><span>Цена</span><strong>{selectedPriceMin === null ? "n/a" : `${selectedPriceMin}..${selectedPriceMax} ${variantRows[0]?.observedCurrency ?? "RUB"}`}</strong></div>
              </div>
            </article>

            <article className="card page-stack">
              <div className="card__head">
                <div>
                  <h2 className="card__title">Публикация и flow</h2>
                  <p className="route-hint">Короткая сводка без перегруженной правой панели.</p>
                </div>
              </div>
              <div className="offer-link-list">
                {variantRows.slice(0, 3).map((row) => (
                  <div key={`${row.id}-overview`} className="offer-link-row">
                    <div className="offer-pill__head">
                      <strong>{row.observedTitle}</strong>
                      <span className="badge">{row.platform}</span>
                    </div>
                    <p className="route-hint">{row.workerProductId} · {row.observedPrice} {row.observedCurrency}</p>
                  </div>
                ))}
                {variantRows.length === 0 ? <p className="route-hint">Добавьте variant, чтобы увидеть публикацию.</p> : null}
              </div>
              <div className="inline">
                <button type="button" className="button button-ghost button-small" onClick={() => setActiveTab("variants")}>Открыть варианты</button>
                <button type="button" className="button button-ghost button-small" onClick={() => setActiveTab("flow")}>Открыть flow</button>
              </div>
            </article>
          </section>
        </section>
      ) : null}

      {activeTab === "variants" ? (
        <section className="card page-stack">
          <div className="card__head">
            <div>
              <h2 className="card__title">Варианты товаров</h2>
              <p className="route-hint">Товары с аккаунтов и площадок, объединённые в оффер.</p>
            </div>
            <div className="inline">
              <button
                type="button"
                className="button button-primary button-small"
                disabled={!selectedOffer}
                onClick={() => setVariantModalOpen(true)}
              >
                ＋ Добавить товар
              </button>
              <button
                type="button"
                className="button button-ghost button-small"
                disabled={saveVariantsMutation.isPending || !selectedOffer}
                onClick={() => saveVariantsMutation.mutate()}
              >
                Сохранить variants
              </button>
            </div>
          </div>

          {!selectedOffer ? <p className="route-hint">Выберите Offer в верхней ленте.</p> : null}
          {selectedOffer && variantRows.length === 0 ? <p className="route-hint">Для Offer пока нет variants.</p> : null}
          {selectedOffer && variantRows.length > 0 ? (
            <div className="offer-variant-list">
              {variantRows.map((row) => (
                <article key={row.id} className="offer-variant-row">
                  <div className="offer-variant-row__head">
                    <div className="stack">
                      <strong>{row.observedTitle}</strong>
                      <p className="route-hint">productId: {row.workerProductId}</p>
                    </div>
                    <div className="chip-row">
                      <span className="chip">{row.platform}</span>
                      <span className="chip">{row.observedPrice} {row.observedCurrency}</span>
                      <span className="chip">priority: {row.priority}</span>
                    </div>
                  </div>
                  <div className="variant-actions">
                    <button
                      type="button"
                      className="button button-ghost button-small"
                      onClick={() => {
                        setVariantRows((current) => current.map((item) => item.id === row.id
                          ? { ...item, priority: 1 }
                          : item));
                        setStatus("Variant помечен как главный (priority=1). Сохраните изменения.");
                      }}
                    >
                      Сделать главным
                    </button>
                    <Link className="button button-ghost button-small" href={`/projects/${projectId}/products`}>
                      Редактировать товар
                    </Link>
                    <button
                      type="button"
                      className="button button-ghost button-small"
                      onClick={() => setVariantRows((current) => current.filter((item) => item.id !== row.id))}
                    >
                      Убрать из оффера
                    </button>
                  </div>
                </article>
              ))}
            </div>
          ) : null}
        </section>
      ) : null}

      {activeTab === "flow" ? (
        <ProjectWorkflowsPanel apiSession={apiSession} projectId={projectId} currentRole={currentRole} />
      ) : null}

      {activeTab === "history" ? (
        <section className="card page-stack">
          <div className="card__head">
            <div>
              <h2 className="card__title">История запусков</h2>
              <p className="route-hint">Последние исполнения flow выбранного оффера.</p>
            </div>
            <button type="button" className="button button-ghost button-small" onClick={() => setStatus("История обновлена.")}>Обновить</button>
          </div>
          <div className="offer-history-list">
            {historyRows.map((item) => (
              <article key={item.id} className="offer-history-row">
                <div>
                  <strong>{item.title}</strong>
                  <p className="route-hint">{item.note}</p>
                </div>
                <span className={`status ${item.status === "success" ? "status--ok" : item.status === "fallback" ? "status--warn" : "status--info"}`}>
                  {item.status}
                </span>
              </article>
            ))}
          </div>
        </section>
      ) : null}

      <RouteModalHost
        isOpen={createModalOpen}
        title="Создать оффер"
        description="Создание новой CRM-сущности оффера."
        onClose={() => setCreateModalOpen(false)}
      >
        <section className="page-stack">
          <label className="field">
            <span>Название</span>
            <input
              className="input"
              value={newOfferName}
              onChange={(event) => setNewOfferName(event.target.value)}
              placeholder="Например: Steam Prime Rental"
            />
          </label>
          <label className="field">
            <span>Описание</span>
            <textarea
              className="input textarea"
              value={newOfferDescription}
              onChange={(event) => setNewOfferDescription(event.target.value)}
              placeholder="Короткое описание оффера"
            />
          </label>
          <div className="inline">
            <button
              type="button"
              className="button button-primary"
              disabled={createOfferMutation.isPending || !newOfferName.trim()}
              onClick={() => createOfferMutation.mutate()}
            >
              Создать
            </button>
            <button type="button" className="button button-ghost" onClick={() => setCreateModalOpen(false)}>
              Отмена
            </button>
          </div>
        </section>
      </RouteModalHost>

      <RouteModalHost
        isOpen={variantModalOpen}
        title="Добавить товар в оффер"
        description="Выберите площадку, аккаунт и товар для связи с оффером."
        onClose={() => setVariantModalOpen(false)}
      >
        <section className="page-stack">
          {!selectedOffer ? <p className="route-hint">Сначала выберите Offer в верхней ленте.</p> : null}
          {selectedOffer ? (
            <>
              {accountsLoading ? <p className="route-hint">Загружаем аккаунты...</p> : null}
              {accountsError ? (
                <p className="route-error">
                  {accountsError instanceof Error ? accountsError.message : "Не удалось загрузить аккаунты."}
                </p>
              ) : null}

              <div className="grid-3">
                <label className="field">
                  <span>Площадка</span>
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
                  <span>Аккаунт</span>
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
                  <span>Товар</span>
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
                    className="button button-primary"
                    onClick={() => {
                      const appended = appendVariant();
                      if (appended) {
                        setVariantModalOpen(false);
                        setActiveTab("variants");
                      }
                    }}
                    disabled={!variantAccountId || !variantProductId}
                  >
                    Добавить variant
                  </button>
                </div>
              </div>
            </>
          ) : null}
        </section>
      </RouteModalHost>
    </div>
  );
}
