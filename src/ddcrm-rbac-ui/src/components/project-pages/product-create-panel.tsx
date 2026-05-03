"use client";
/* eslint-disable react-hooks/set-state-in-effect */

import { useMutation, useQueries, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { useProjectAccounts } from "@/hooks/use-project-accounts";
import type { ApiSession } from "@/lib/api-client";
import { runAccountActionRequest } from "@/lib/api-client";
import { extractObjectRows, isRecord, readFirstString } from "@/lib/worker-result";

interface ProjectProductCreatePanelProps {
  apiSession: ApiSession;
  projectId: string;
  preferredAccountId: string;
  mode?: "page" | "modal";
  onCompleted?: () => void;
  onCancel?: () => void;
}

interface SchemaField {
  key: string;
  type: string;
  required: boolean;
}

interface SchemaOption {
  schemaId: string;
  provider: string;
  title: string;
  fields: SchemaField[];
  requiredFields: string[];
}

interface AccountSchemaState {
  accountId: string;
  schemas: SchemaOption[];
  createEnabled: boolean;
  createReason: string;
}

interface OverrideDraft {
  schemaId?: string;
  title?: string;
  priceAmount?: string;
  priceCurrency?: string;
  providerFields: Record<string, string>;
}

interface SubmitResultRow {
  accountId: string;
  accountLabel: string;
  status: "success" | "error" | "skipped";
  message: string;
}

const CORE_FIELD_KEYS = new Set(["schemaId", "title", "price.amount", "price.currency", "status"]);
const PLAYEROK_PLATFORM_KEY = "playerok";
const PLAYEROK_SCHEMA_ID = "playerok.item.v1";
const PLAYEROK_GAME_CATEGORY_FIELD = "attributes.gameCategoryId";
const PLAYEROK_OBTAINING_TYPE_FIELD = "attributes.obtainingTypeId";
const PLAYEROK_OPTIONS_FIELD = "attributes.options";
const PLAYEROK_DATA_FIELDS_FIELD = "attributes.dataFields";

interface PlayerokMetadataCategory {
  id: string;
  gameId: string;
  name: string;
}

interface PlayerokMetadataObtainingType {
  id: string;
  name: string;
}

interface PlayerokMetadataOption {
  field: string;
  value: string;
  label: string;
}

interface PlayerokMetadataDataField {
  id: string;
  name: string;
  required: boolean;
}

interface PlayerokMetadataPayload {
  categories: PlayerokMetadataCategory[];
  obtainingTypes: PlayerokMetadataObtainingType[];
  options: PlayerokMetadataOption[];
  dataFields: PlayerokMetadataDataField[];
}

function normalizeCurrency(value: string) {
  const normalized = value.trim().toUpperCase();
  return normalized || "RUB";
}

function normalizeNumber(value: string) {
  const parsed = Number(value.trim());
  return Number.isFinite(parsed) ? parsed : Number.NaN;
}

function normalizePlatform(value: string) {
  return value.trim().toLowerCase();
}

function isPlayerokAccount(platform: string) {
  return normalizePlatform(platform) === PLAYEROK_PLATFORM_KEY;
}

function isJsonLikeFieldType(fieldType: string) {
  const normalized = fieldType.trim().toLowerCase();
  return normalized === "array" || normalized === "object" || normalized === "json";
}

function parseSchemaFields(row: Record<string, unknown>) {
  const fields = Array.isArray(row.fields) ? row.fields : [];
  return fields
    .filter(isRecord)
    .map((field) => ({
      key: readFirstString(field, ["key"]),
      type: readFirstString(field, ["type"]).toLowerCase() || "string",
      required: field.required === true,
    }))
    .filter((field) => field.key.length > 0);
}

function buildSchemaOptions(payload: Record<string, unknown> | null): SchemaOption[] {
  if (!payload) {
    return [];
  }

  const rows = extractObjectRows(payload, ["items", "schemas"]);
  return rows
    .map((row) => {
      const fields = parseSchemaFields(row);
      return {
        schemaId: readFirstString(row, ["schemaId", "id"]),
        provider: readFirstString(row, ["provider", "platform"]),
        title: readFirstString(row, ["title", "name"]),
        fields,
        requiredFields: fields.filter((field) => field.required).map((field) => field.key),
      };
    })
    .filter((row) => row.schemaId.length > 0);
}

function parsePlayerokMetadata(payload: Record<string, unknown> | null): PlayerokMetadataPayload | null {
  if (!payload) {
    return null;
  }

  const categories = extractObjectRows(payload, ["categories"])
    .map((row) => ({
      id: readFirstString(row, ["id"]),
      gameId: readFirstString(row, ["gameId"]),
      name: readFirstString(row, ["name", "label"]),
    }))
    .filter((item) => item.id.length > 0);

  const obtainingTypes = extractObjectRows(payload, ["obtainingTypes"])
    .map((row) => ({
      id: readFirstString(row, ["id"]),
      name: readFirstString(row, ["name", "label"]),
    }))
    .filter((item) => item.id.length > 0);

  const options = extractObjectRows(payload, ["options"])
    .map((row) => ({
      field: readFirstString(row, ["field"]),
      value: readFirstString(row, ["value", "id"]),
      label: readFirstString(row, ["label", "value", "name"]),
    }))
    .filter((item) => item.field.length > 0 && item.value.length > 0);

  const dataFields = extractObjectRows(payload, ["dataFields"])
    .map((row) => ({
      id: readFirstString(row, ["id"]),
      name: readFirstString(row, ["name", "label"]),
      required: row.required === true,
    }))
    .filter((item) => item.id.length > 0);

  return {
    categories,
    obtainingTypes,
    options,
    dataFields,
  };
}

function dedupeByKey<TItem>(items: TItem[], keyResolver: (item: TItem) => string) {
  const map = new Map<string, TItem>();
  for (const item of items) {
    const key = keyResolver(item);
    if (!key || map.has(key)) {
      continue;
    }
    map.set(key, item);
  }
  return [...map.values()];
}

function buildJsonPlaceholder(fieldType: string) {
  return isJsonLikeFieldType(fieldType) ? "Введите JSON" : `type: ${fieldType}`;
}

function readCapabilityCreateFlag(payload: Record<string, unknown> | null) {
  if (!payload) {
    return { enabled: true, reason: "" };
  }

  let enabled: boolean | null = null;
  let reason = "";

  if (isRecord(payload.features)) {
    const featureMap = payload.features;
    const direct = featureMap["products.create"];
    if (typeof direct === "boolean") {
      enabled = direct;
    }

    if (isRecord(featureMap.products)) {
      const nested = featureMap.products.create;
      if (typeof nested === "boolean") {
        enabled = nested;
      }
    }
  }

  if (Array.isArray(payload.capabilities)) {
    for (const capability of payload.capabilities) {
      if (!isRecord(capability)) {
        continue;
      }

      const key = readFirstString(capability, ["key"]);
      if (key !== "products.create") {
        continue;
      }

      if (typeof capability.enabled === "boolean") {
        enabled = capability.enabled;
      }

      if (!enabled) {
        reason =
          readFirstString(capability, ["reason", "message"])
          || "На площадке отключен products.create по capability-политике.";
      }
    }
  }

  return {
    enabled: enabled ?? true,
    reason,
  };
}

function ensureOverrideDraft(draft: OverrideDraft | undefined): OverrideDraft {
  return draft ?? { providerFields: {} };
}

function readByPath(payload: Record<string, unknown>, key: string) {
  const segments = key.split(".").filter(Boolean);
  let current: unknown = payload;
  for (const segment of segments) {
    if (!isRecord(current) || !(segment in current)) {
      return undefined;
    }

    current = current[segment];
  }

  return current;
}

function setByPath(payload: Record<string, unknown>, key: string, value: unknown) {
  const segments = key.split(".").filter(Boolean);
  if (segments.length === 0) {
    return;
  }

  let cursor: Record<string, unknown> = payload;
  for (let index = 0; index < segments.length; index += 1) {
    const segment = segments[index];
    const isLast = index === segments.length - 1;

    if (isLast) {
      cursor[segment] = value;
      return;
    }

    const next = cursor[segment];
    if (!isRecord(next)) {
      const created: Record<string, unknown> = {};
      cursor[segment] = created;
      cursor = created;
      continue;
    }

    cursor = next;
  }
}

function parseTypedFieldValue(rawValue: string, fieldType: string) {
  const trimmed = rawValue.trim();
  if (!trimmed) {
    return "";
  }

  if (fieldType === "number" || fieldType === "integer" || fieldType === "float" || fieldType === "decimal") {
    const numberValue = Number(trimmed);
    if (!Number.isFinite(numberValue)) {
      throw new Error(`Поле \`${fieldType}\` должно быть числом.`);
    }

    return numberValue;
  }

  if (fieldType === "boolean") {
    if (trimmed.toLowerCase() === "true" || trimmed === "1") {
      return true;
    }

    if (trimmed.toLowerCase() === "false" || trimmed === "0") {
      return false;
    }

    throw new Error("Boolean поле принимает только true/false или 1/0.");
  }

  if (isJsonLikeFieldType(fieldType)) {
    try {
      return JSON.parse(trimmed);
    } catch {
      throw new Error(`Поле \`${fieldType}\` должно быть валидным JSON.`);
    }
  }

  return trimmed;
}

function mergeString(first: string | undefined, second: string | undefined, fallback: string) {
  if (typeof first === "string" && first.trim()) {
    return first.trim();
  }

  if (typeof second === "string" && second.trim()) {
    return second.trim();
  }

  return fallback;
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

  const [selectedAccountIds, setSelectedAccountIds] = useState<string[]>([]);
  const [universalSchemaId, setUniversalSchemaId] = useState("digital_goods.v1");
  const [universalTitle, setUniversalTitle] = useState("Новый товар");
  const [universalPriceAmount, setUniversalPriceAmount] = useState("100");
  const [universalPriceCurrency, setUniversalPriceCurrency] = useState("RUB");
  const [universalProviderFields, setUniversalProviderFields] = useState<Record<string, string>>({});
  const [platformOverrides, setPlatformOverrides] = useState<Record<string, OverrideDraft>>({});
  const [accountOverrides, setAccountOverrides] = useState<Record<string, OverrideDraft>>({});
  const [status, setStatus] = useState("Заполните универсальные поля и при необходимости задайте overrides для платформ и аккаунтов.");
  const [submitResults, setSubmitResults] = useState<SubmitResultRow[]>([]);

  const {
    accounts,
    isLoading: accountsLoading,
    error: accountsError,
  } = useProjectAccounts(apiSession, projectId);

  useEffect(() => {
    if (accounts.length === 0) {
      setSelectedAccountIds([]);
      return;
    }

    setSelectedAccountIds((prev) => {
      const existing = prev.filter((accountId) => accounts.some((account) => account.id === accountId));
      if (existing.length > 0) {
        const isSame =
          existing.length === prev.length
          && existing.every((value, index) => value === prev[index]);
        return isSame ? prev : existing;
      }

      if (preferredAccountId && accounts.some((account) => account.id === preferredAccountId)) {
        return [preferredAccountId];
      }

      return [accounts[0].id];
    });
  }, [accounts, preferredAccountId]);

  const selectedAccounts = useMemo(
    () => accounts.filter((account) => selectedAccountIds.includes(account.id)),
    [accounts, selectedAccountIds],
  );

  const schemaQueries = useQueries({
    queries: selectedAccounts.map((account) => ({
      queryKey: [
        "products.schemas.list",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
        account.id,
      ] as const,
      queryFn: async () => {
        const result = await runAccountActionRequest(
          apiSession,
          account.id,
          "products.schemas.list",
          {},
        );

        return isRecord(result) ? result : null;
      },
      enabled: true,
      staleTime: 10_000,
    })),
  });

  const schemaStateByAccountId = useMemo(() => {
    const entries = selectedAccounts.map((account, index) => {
      const query = schemaQueries[index];
      const payload = query?.data ?? null;
      const schemas = buildSchemaOptions(payload);
      const createAvailability = readCapabilityCreateFlag(payload);

      return [
        account.id,
        {
          accountId: account.id,
          schemas,
          createEnabled: createAvailability.enabled,
          createReason: createAvailability.reason,
        } satisfies AccountSchemaState,
      ] as const;
    });

    return new Map(entries);
  }, [schemaQueries, selectedAccounts]);

  const availableSchemaIds = useMemo(() => {
    const candidates = selectedAccounts
      .map((account) => new Set((schemaStateByAccountId.get(account.id)?.schemas ?? []).map((schema) => schema.schemaId)))
      .filter((set) => set.size > 0);

    if (candidates.length === 0) {
      return ["digital_goods.v1"];
    }

    const [first, ...rest] = candidates;
    const intersection = [...first].filter((schemaId) => rest.every((set) => set.has(schemaId)));
    return intersection.length > 0 ? intersection : [...first];
  }, [schemaStateByAccountId, selectedAccounts]);

  useEffect(() => {
    if (availableSchemaIds.includes(universalSchemaId)) {
      return;
    }

    setUniversalSchemaId(availableSchemaIds[0] ?? "digital_goods.v1");
  }, [availableSchemaIds, universalSchemaId]);

  const providerFieldCatalog = useMemo(() => {
    const map = new Map<string, SchemaField>();

    for (const account of selectedAccounts) {
      const state = schemaStateByAccountId.get(account.id);
      if (!state) {
        continue;
      }

      const selectedSchema = state.schemas.find((schema) => schema.schemaId === universalSchemaId)
        ?? state.schemas[0];
      if (!selectedSchema) {
        continue;
      }

      for (const field of selectedSchema.fields) {
        if (CORE_FIELD_KEYS.has(field.key)) {
          continue;
        }

        if (!map.has(field.key)) {
          map.set(field.key, field);
        }
      }
    }

    return [...map.values()];
  }, [schemaStateByAccountId, selectedAccounts, universalSchemaId]);

  const resolveProviderFieldForAccount = (accountId: string, platform: string, fieldKey: string) => {
    const platformOverride = ensureOverrideDraft(platformOverrides[platform]);
    const accountOverride = ensureOverrideDraft(accountOverrides[accountId]);
    return mergeString(
      accountOverride.providerFields[fieldKey],
      platformOverride.providerFields[fieldKey],
      universalProviderFields[fieldKey] ?? "",
    );
  };

  const playerokAccounts = useMemo(
    () => selectedAccounts.filter((account) => isPlayerokAccount(account.platform)),
    [selectedAccounts],
  );

  const playerokMetadataQueries = useQueries({
    queries: playerokAccounts.map((account) => {
      const gameCategoryId = resolveProviderFieldForAccount(account.id, account.platform, PLAYEROK_GAME_CATEGORY_FIELD);
      const obtainingTypeId = resolveProviderFieldForAccount(account.id, account.platform, PLAYEROK_OBTAINING_TYPE_FIELD);
      const schemaState = schemaStateByAccountId.get(account.id);
      const hasPlayerokSchema = (schemaState?.schemas ?? []).some((schema) => schema.schemaId === PLAYEROK_SCHEMA_ID);
      const payload: Record<string, unknown> = {};
      if (gameCategoryId.trim()) {
        payload.gameCategoryId = gameCategoryId.trim();
      }
      if (obtainingTypeId.trim()) {
        payload.obtainingTypeId = obtainingTypeId.trim();
      }

      return {
        queryKey: [
          "ext.playerok.products.metadata",
          apiSession.baseUrl,
          apiSession.token,
          projectId,
          account.id,
          gameCategoryId,
          obtainingTypeId,
        ] as const,
        queryFn: async () => {
          const result = await runAccountActionRequest(
            apiSession,
            account.id,
            "ext.playerok.products.metadata",
            payload,
          );

          return isRecord(result) ? result : null;
        },
        enabled: hasPlayerokSchema,
        staleTime: 10_000,
      };
    }),
  });

  const mergedPlayerokMetadata = useMemo(() => {
    const parsedRows = playerokMetadataQueries
      .map((query) => parsePlayerokMetadata(query.data ?? null))
      .filter((row): row is PlayerokMetadataPayload => row !== null);

    if (parsedRows.length === 0) {
      return null;
    }

    return {
      categories: dedupeByKey(
        parsedRows.flatMap((row) => row.categories),
        (item) => item.id,
      ),
      obtainingTypes: dedupeByKey(
        parsedRows.flatMap((row) => row.obtainingTypes),
        (item) => item.id,
      ),
      options: dedupeByKey(
        parsedRows.flatMap((row) => row.options),
        (item) => `${item.field}:${item.value}`,
      ),
      dataFields: dedupeByKey(
        parsedRows.flatMap((row) => row.dataFields),
        (item) => item.id,
      ),
    };
  }, [playerokMetadataQueries]);

  const playerokMetadataError = useMemo(() => {
    for (const query of playerokMetadataQueries) {
      if (query.error instanceof Error) {
        return query.error.message;
      }
    }
    return "";
  }, [playerokMetadataQueries]);

  const playerokMetadataLoading = playerokMetadataQueries.some((query) => query.isPending);

  const playerokOptionsTemplate = useMemo(() => {
    if (!mergedPlayerokMetadata || mergedPlayerokMetadata.options.length === 0) {
      return "";
    }

    const byField = new Map<string, string>();
    for (const option of mergedPlayerokMetadata.options) {
      if (!byField.has(option.field)) {
        byField.set(option.field, option.value);
      }
    }

    return JSON.stringify(Object.fromEntries(byField), null, 2);
  }, [mergedPlayerokMetadata]);

  const playerokDataFieldsTemplate = useMemo(() => {
    if (!mergedPlayerokMetadata || mergedPlayerokMetadata.dataFields.length === 0) {
      return "";
    }

    const payload: Record<string, string> = {};
    for (const field of mergedPlayerokMetadata.dataFields) {
      payload[field.id] = "";
    }
    return JSON.stringify(payload, null, 2);
  }, [mergedPlayerokMetadata]);

  const renderProviderFieldInput = (
    scopeId: string,
    field: SchemaField,
    value: string,
    onChange: (next: string) => void,
    requiredMark: string,
  ) => {
    const categoryListId = `${scopeId}-playerok-categories`;
    const obtainingTypeListId = `${scopeId}-playerok-obtaining-types`;

    if (field.key === PLAYEROK_GAME_CATEGORY_FIELD && mergedPlayerokMetadata?.categories.length) {
      return (
        <label key={`${scopeId}-${field.key}`} className="field">
          <span>{field.key}{requiredMark}</span>
          <input
            className="input"
            value={value}
            onChange={(event) => onChange(event.target.value)}
            list={categoryListId}
            placeholder="Выберите category id"
          />
          <datalist id={categoryListId}>
            {mergedPlayerokMetadata.categories.map((category) => (
              <option
                key={`${category.id}-${category.gameId}`}
                value={category.id}
                label={`${category.name}${category.gameId ? ` (${category.gameId})` : ""}`}
              />
            ))}
          </datalist>
        </label>
      );
    }

    if (field.key === PLAYEROK_OBTAINING_TYPE_FIELD && mergedPlayerokMetadata?.obtainingTypes.length) {
      return (
        <label key={`${scopeId}-${field.key}`} className="field">
          <span>{field.key}{requiredMark}</span>
          <input
            className="input"
            value={value}
            onChange={(event) => onChange(event.target.value)}
            list={obtainingTypeListId}
            placeholder="Выберите obtaining type id"
          />
          <datalist id={obtainingTypeListId}>
            {mergedPlayerokMetadata.obtainingTypes.map((item) => (
              <option key={item.id} value={item.id} label={item.name} />
            ))}
          </datalist>
        </label>
      );
    }

    if (field.key === PLAYEROK_OPTIONS_FIELD || field.key === PLAYEROK_DATA_FIELDS_FIELD || isJsonLikeFieldType(field.type)) {
      const template = field.key === PLAYEROK_OPTIONS_FIELD
        ? playerokOptionsTemplate
        : field.key === PLAYEROK_DATA_FIELDS_FIELD
          ? playerokDataFieldsTemplate
          : "";
      return (
        <label key={`${scopeId}-${field.key}`} className="field">
          <span>{field.key}{requiredMark}</span>
          <textarea
            className="input"
            rows={4}
            value={value}
            onChange={(event) => onChange(event.target.value)}
            placeholder={template || buildJsonPlaceholder(field.type)}
          />
        </label>
      );
    }

    return (
      <label key={`${scopeId}-${field.key}`} className="field">
        <span>{field.key}{requiredMark}</span>
        <input
          className="input"
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={buildJsonPlaceholder(field.type)}
        />
      </label>
    );
  };

  const groupedPlatforms = useMemo(() => {
    const map = new Map<string, typeof selectedAccounts>();

    for (const account of selectedAccounts) {
      const list = map.get(account.platform);
      if (list) {
        list.push(account);
      } else {
        map.set(account.platform, [account]);
      }
    }

    return [...map.entries()];
  }, [selectedAccounts]);

  const toggleAccountSelection = (accountId: string, checked: boolean) => {
    setSelectedAccountIds((prev) => {
      if (checked) {
        return prev.includes(accountId) ? prev : [...prev, accountId];
      }

      const next = prev.filter((value) => value !== accountId);
      return next;
    });
  };

  const updatePlatformOverrideField = (platform: string, key: keyof OverrideDraft, value: string) => {
    setPlatformOverrides((prev) => {
      const current = ensureOverrideDraft(prev[platform]);
      return {
        ...prev,
        [platform]: {
          ...current,
          [key]: value,
          providerFields: current.providerFields,
        },
      };
    });
  };

  const updatePlatformProviderField = (platform: string, fieldKey: string, value: string) => {
    setPlatformOverrides((prev) => {
      const current = ensureOverrideDraft(prev[platform]);
      return {
        ...prev,
        [platform]: {
          ...current,
          providerFields: {
            ...current.providerFields,
            [fieldKey]: value,
          },
        },
      };
    });
  };

  const updateAccountOverrideField = (accountId: string, key: keyof OverrideDraft, value: string) => {
    setAccountOverrides((prev) => {
      const current = ensureOverrideDraft(prev[accountId]);
      return {
        ...prev,
        [accountId]: {
          ...current,
          [key]: value,
          providerFields: current.providerFields,
        },
      };
    });
  };

  const updateAccountProviderField = (accountId: string, fieldKey: string, value: string) => {
    setAccountOverrides((prev) => {
      const current = ensureOverrideDraft(prev[accountId]);
      return {
        ...prev,
        [accountId]: {
          ...current,
          providerFields: {
            ...current.providerFields,
            [fieldKey]: value,
          },
        },
      };
    });
  };

  const createProductMutation = useMutation({
    mutationFn: async () => {
      if (selectedAccounts.length === 0) {
        throw new Error("Выберите минимум один аккаунт для создания товара.");
      }

      if (!universalTitle.trim()) {
        throw new Error("Название товара обязательно.");
      }

      const universalAmount = normalizeNumber(universalPriceAmount);
      if (!Number.isFinite(universalAmount) || universalAmount < 0) {
        throw new Error("Базовая цена должна быть неотрицательным числом.");
      }

      const results: SubmitResultRow[] = [];
      const successfulAccountIds: string[] = [];

      for (const account of selectedAccounts) {
        const platformOverride = ensureOverrideDraft(platformOverrides[account.platform]);
        const accountOverride = ensureOverrideDraft(accountOverrides[account.id]);

        const schemaState = schemaStateByAccountId.get(account.id);
        const accountLabel = `${account.displayName} (${account.platform})`;

        if (!schemaState) {
          results.push({
            accountId: account.id,
            accountLabel,
            status: "error",
            message: "Не удалось получить schemas для аккаунта.",
          });
          continue;
        }

        if (!schemaState.createEnabled) {
          results.push({
            accountId: account.id,
            accountLabel,
            status: "skipped",
            message: schemaState.createReason || "`products.create` отключен на стороне площадки.",
          });
          continue;
        }

        const schemaId = mergeString(accountOverride.schemaId, platformOverride.schemaId, universalSchemaId);
        const schemaOption = schemaState.schemas.find((item) => item.schemaId === schemaId)
          ?? schemaState.schemas[0];

        if (!schemaOption) {
          results.push({
            accountId: account.id,
            accountLabel,
            status: "error",
            message: "Для аккаунта не опубликованы product schemas.",
          });
          continue;
        }

        const mergedTitle = mergeString(accountOverride.title, platformOverride.title, universalTitle.trim());
        const mergedPriceAmount = mergeString(accountOverride.priceAmount, platformOverride.priceAmount, universalPriceAmount);
        const mergedPriceCurrency = mergeString(accountOverride.priceCurrency, platformOverride.priceCurrency, universalPriceCurrency);
        const parsedAmount = normalizeNumber(mergedPriceAmount);

        if (!Number.isFinite(parsedAmount) || parsedAmount < 0) {
          results.push({
            accountId: account.id,
            accountLabel,
            status: "error",
            message: "Некорректная цена в overrides: ожидается неотрицательное число.",
          });
          continue;
        }

        const payload: Record<string, unknown> = {
          schemaId: schemaOption.schemaId,
          title: mergedTitle,
          price: {
            amount: parsedAmount,
            currency: normalizeCurrency(mergedPriceCurrency),
          },
          status: "active",
        };

        const providerFieldKeys = new Set<string>([
          ...Object.keys(universalProviderFields),
          ...Object.keys(platformOverride.providerFields),
          ...Object.keys(accountOverride.providerFields),
          ...schemaOption.requiredFields,
        ]);

        for (const key of providerFieldKeys) {
          if (CORE_FIELD_KEYS.has(key)) {
            continue;
          }

          const rawValue = mergeString(
            accountOverride.providerFields[key],
            platformOverride.providerFields[key],
            universalProviderFields[key] ?? "",
          );

          if (!rawValue) {
            continue;
          }

          const fieldType = schemaOption.fields.find((field) => field.key === key)?.type ?? "string";
          setByPath(payload, key, parseTypedFieldValue(rawValue, fieldType));
        }

        const missingRequired = schemaOption.requiredFields.filter((requiredKey) => {
          const value = readByPath(payload, requiredKey);
          if (value === null || value === undefined) {
            return true;
          }

          if (typeof value === "string") {
            return value.trim().length === 0;
          }

          return false;
        });

        if (missingRequired.length > 0) {
          results.push({
            accountId: account.id,
            accountLabel,
            status: "error",
            message: `Не заполнены обязательные поля: ${missingRequired.join(", ")}.`,
          });
          continue;
        }

        try {
          await runAccountActionRequest(apiSession, account.id, "products.create", payload);
          successfulAccountIds.push(account.id);
          results.push({
            accountId: account.id,
            accountLabel,
            status: "success",
            message: "Товар создан.",
          });
        } catch (error) {
          results.push({
            accountId: account.id,
            accountLabel,
            status: "error",
            message: error instanceof Error ? error.message : "Не удалось создать товар.",
          });
        }
      }

      return {
        results,
        successfulAccountIds,
      };
    },
    onSuccess: async ({ results, successfulAccountIds }) => {
      for (const accountId of successfulAccountIds) {
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

      setSubmitResults(results);
      const successCount = results.filter((item) => item.status === "success").length;
      const errorCount = results.length - successCount;
      setStatus(`Создание завершено: success=${successCount}, errors=${errorCount}.`);

      if (successCount > 0 && errorCount === 0) {
        if (onCompleted) {
          onCompleted();
          return;
        }

        router.push(`/projects/${projectId}/products`);
      }
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось создать товар.");
    },
  });

  const schemaLoading = schemaQueries.some((query) => query.isPending);

  return (
    <div className="page-stack" data-testid="project-product-create-panel">
      {mode === "page" ? (
        <>
          <header className="page-section-header">
            <h2>Добавить товар</h2>
            <p>Один submit на несколько аккаунтов: универсальные поля + platform/account overrides.</p>
          </header>

          <div className="panel-actions">
            <Link href={`/projects/${projectId}/products`} className="button button-ghost">
              Назад к товарам
            </Link>
          </div>
        </>
      ) : null}

      {accountsError ? <p className="route-error">{accountsError.message}</p> : null}

      <section className="panel-card page-stack">
        <div className="panel-title-row">
          <h3>Целевые аккаунты</h3>
        </div>
        {accountsLoading ? <p className="route-hint">Загружаем аккаунты...</p> : null}
        {accounts.length === 0 ? (
          <p className="route-hint">В проекте нет аккаунтов для создания товара.</p>
        ) : (
          <div className="grid-2">
            {accounts.map((account) => {
              const checked = selectedAccountIds.includes(account.id);
              return (
                <label key={account.id} className="field field-inline">
                  <span>{account.displayName} · {account.platform}</span>
                  <input
                    type="checkbox"
                    checked={checked}
                    onChange={(event) => toggleAccountSelection(account.id, event.target.checked)}
                  />
                </label>
              );
            })}
          </div>
        )}
      </section>

      <section className="panel-card page-stack">
        <div className="panel-title-row">
          <h3>Универсальные поля</h3>
        </div>

        <label className="field">
          <span>Схема товара</span>
          <select
            className="input"
            value={universalSchemaId}
            onChange={(event) => setUniversalSchemaId(event.target.value)}
            disabled={availableSchemaIds.length === 0}
          >
            {availableSchemaIds.map((schemaId) => (
              <option key={schemaId} value={schemaId}>{schemaId}</option>
            ))}
          </select>
        </label>

        <label className="field">
          <span>Название</span>
          <input
            className="input"
            value={universalTitle}
            onChange={(event) => setUniversalTitle(event.target.value)}
            placeholder="Название товара"
          />
        </label>

        <div className="grid-2">
          <label className="field">
            <span>Цена</span>
            <input
              className="input"
              value={universalPriceAmount}
              onChange={(event) => setUniversalPriceAmount(event.target.value)}
              placeholder="100"
            />
          </label>
          <label className="field">
            <span>Валюта</span>
            <input
              className="input"
              value={universalPriceCurrency}
              onChange={(event) => setUniversalPriceCurrency(event.target.value)}
              placeholder="RUB"
            />
          </label>
        </div>

        {schemaLoading ? <p className="route-hint">Загружаем schema-поля по выбранным аккаунтам...</p> : null}

        {playerokAccounts.length > 0 ? (
          <div className="stacked-block">
            <h4>Playerok metadata helper</h4>
            {playerokMetadataLoading ? <p className="route-hint">Загружаем категории/опции Playerok...</p> : null}
            {playerokMetadataError ? (
              <p className="route-hint">
                Metadata временно недоступна: {playerokMetadataError}. Форма остаётся в ручном режиме.
              </p>
            ) : null}
            {mergedPlayerokMetadata ? (
              <div className="page-stack">
                <p className="route-hint">
                  categories: {mergedPlayerokMetadata.categories.length}, obtaining types: {mergedPlayerokMetadata.obtainingTypes.length}, options: {mergedPlayerokMetadata.options.length}, dataFields: {mergedPlayerokMetadata.dataFields.length}.
                </p>
                {playerokOptionsTemplate ? (
                  <p className="route-hint">Шаблон `attributes.options`: {playerokOptionsTemplate}</p>
                ) : null}
                {mergedPlayerokMetadata.dataFields.length > 0 ? (
                  <p className="route-hint">
                    Обязательные dataFields: {mergedPlayerokMetadata.dataFields.filter((item) => item.required).map((item) => item.name || item.id).join(", ") || "нет"}.
                  </p>
                ) : null}
              </div>
            ) : null}
          </div>
        ) : null}

        {providerFieldCatalog.length > 0 ? (
          <div className="stacked-block">
            <h4>Provider-required поля</h4>
            {providerFieldCatalog.map((field) => renderProviderFieldInput(
              "universal",
              field,
              universalProviderFields[field.key] ?? "",
              (nextValue) =>
                setUniversalProviderFields((prev) => ({
                  ...prev,
                  [field.key]: nextValue,
                })),
              field.required ? " *" : "",
            ))}
          </div>
        ) : (
          <p className="route-hint">Для выбранных схем нет дополнительных provider-полей.</p>
        )}
      </section>

      {groupedPlatforms.map(([platform, platformAccounts]) => {
        const override = ensureOverrideDraft(platformOverrides[platform]);
        return (
          <section key={`platform-${platform}`} className="panel-card page-stack">
            <div className="panel-title-row">
              <h3>Platform override · {platform}</h3>
            </div>
            <p className="route-hint">Аккаунты: {platformAccounts.map((item) => item.displayName).join(", ")}</p>

            <div className="grid-2">
              <label className="field">
                <span>SchemaId override</span>
                <input
                  className="input"
                  value={override.schemaId ?? ""}
                  onChange={(event) => updatePlatformOverrideField(platform, "schemaId", event.target.value)}
                  placeholder="optional"
                />
              </label>
              <label className="field">
                <span>Title override</span>
                <input
                  className="input"
                  value={override.title ?? ""}
                  onChange={(event) => updatePlatformOverrideField(platform, "title", event.target.value)}
                  placeholder="optional"
                />
              </label>
            </div>

            <div className="grid-2">
              <label className="field">
                <span>Price override</span>
                <input
                  className="input"
                  value={override.priceAmount ?? ""}
                  onChange={(event) => updatePlatformOverrideField(platform, "priceAmount", event.target.value)}
                  placeholder="optional"
                />
              </label>
              <label className="field">
                <span>Currency override</span>
                <input
                  className="input"
                  value={override.priceCurrency ?? ""}
                  onChange={(event) => updatePlatformOverrideField(platform, "priceCurrency", event.target.value)}
                  placeholder="optional"
                />
              </label>
            </div>

            {providerFieldCatalog.length > 0 ? (
              <div className="stacked-block">
                {providerFieldCatalog.map((field) => renderProviderFieldInput(
                  `platform-${platform}`,
                  field,
                  override.providerFields[field.key] ?? "",
                  (nextValue) => updatePlatformProviderField(platform, field.key, nextValue),
                  "",
                ))}
              </div>
            ) : null}
          </section>
        );
      })}

      {selectedAccounts.map((account) => {
        const override = ensureOverrideDraft(accountOverrides[account.id]);
        const schemaState = schemaStateByAccountId.get(account.id);

        return (
          <section key={`account-${account.id}`} className="panel-card page-stack">
            <div className="panel-title-row">
              <h3>Account override · {account.displayName}</h3>
            </div>
            <p className="route-hint">{account.platform} · accountId: {account.id}</p>
            {schemaState && !schemaState.createEnabled ? (
              <p className="route-error">
                products.create disabled: {schemaState.createReason || "операция отключена по capability"}
              </p>
            ) : null}

            <div className="grid-2">
              <label className="field">
                <span>SchemaId override</span>
                <input
                  className="input"
                  value={override.schemaId ?? ""}
                  onChange={(event) => updateAccountOverrideField(account.id, "schemaId", event.target.value)}
                  placeholder="optional"
                />
              </label>
              <label className="field">
                <span>Title override</span>
                <input
                  className="input"
                  value={override.title ?? ""}
                  onChange={(event) => updateAccountOverrideField(account.id, "title", event.target.value)}
                  placeholder="optional"
                />
              </label>
            </div>

            <div className="grid-2">
              <label className="field">
                <span>Price override</span>
                <input
                  className="input"
                  value={override.priceAmount ?? ""}
                  onChange={(event) => updateAccountOverrideField(account.id, "priceAmount", event.target.value)}
                  placeholder="optional"
                />
              </label>
              <label className="field">
                <span>Currency override</span>
                <input
                  className="input"
                  value={override.priceCurrency ?? ""}
                  onChange={(event) => updateAccountOverrideField(account.id, "priceCurrency", event.target.value)}
                  placeholder="optional"
                />
              </label>
            </div>

            {providerFieldCatalog.length > 0 ? (
              <div className="stacked-block">
                {providerFieldCatalog.map((field) => renderProviderFieldInput(
                  `account-${account.id}`,
                  field,
                  override.providerFields[field.key] ?? "",
                  (nextValue) => updateAccountProviderField(account.id, field.key, nextValue),
                  "",
                ))}
              </div>
            ) : null}
          </section>
        );
      })}

      <div className="panel-actions">
        <button
          type="button"
          className="button button-primary"
          disabled={createProductMutation.isPending || selectedAccounts.length === 0}
          onClick={() => createProductMutation.mutate()}
        >
          Создать товары
        </button>
        {mode === "modal" && onCancel ? (
          <button type="button" className="button button-ghost" onClick={onCancel}>
            Отмена
          </button>
        ) : null}
      </div>

      {submitResults.length > 0 ? (
        <section className="panel-card page-stack">
          <h3>Результат по аккаунтам</h3>
          <ul className="entity-list">
            {submitResults.map((item) => (
              <li key={`${item.accountId}-${item.status}`} className="entity-list-item">
                <div>
                  <strong>{item.accountLabel}</strong>
                  <div className="entity-pills">
                    <span className="entity-pill">{item.status}</span>
                  </div>
                  <p className={item.status === "success" ? "route-hint" : "route-error"}>{item.message}</p>
                </div>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <p className="route-hint">{status}</p>
    </div>
  );
}
