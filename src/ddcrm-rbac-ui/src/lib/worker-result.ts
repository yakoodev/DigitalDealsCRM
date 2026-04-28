export function extractObjectRows(
  value: Record<string, unknown> | undefined | null,
  preferredKeys: readonly string[],
): Record<string, unknown>[] {
  if (!value) {
    return [];
  }

  for (const key of preferredKeys) {
    const candidate = value[key];
    if (!Array.isArray(candidate)) {
      continue;
    }

    const rows = candidate.filter(
      (entry): entry is Record<string, unknown> =>
        typeof entry === "object" && entry !== null && !Array.isArray(entry),
    );

    if (rows.length > 0) {
      return rows;
    }
  }

  return [];
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function stringifyPrimitive(value: string | number | boolean) {
  return String(value);
}

function summarizeArray(values: unknown[]): string {
  if (values.length === 0) {
    return "";
  }

  const primitives = values.filter(
    (entry): entry is string | number | boolean =>
      typeof entry === "string"
      || typeof entry === "number"
      || typeof entry === "boolean",
  );

  if (primitives.length === values.length && values.length <= 5) {
    return primitives.map(stringifyPrimitive).join(", ");
  }

  return `${values.length} элементов`;
}

function summarizeObject(value: Record<string, unknown>): string {
  const amount = value.amount;
  const currency = value.currency;
  if (
    (typeof amount === "number" || typeof amount === "string")
    && (typeof currency === "string" || typeof currency === "number")
  ) {
    return `${String(amount)} ${String(currency)}`.trim();
  }

  const preferredKeys = [
    "title",
    "name",
    "displayName",
    "text",
    "message",
    "body",
    "preview",
    "value",
    "status",
    "id",
  ] as const;

  for (const key of preferredKeys) {
    const candidate = value[key];
    if (
      typeof candidate === "string"
      || typeof candidate === "number"
      || typeof candidate === "boolean"
    ) {
      const text = stringifyPrimitive(candidate);
      if (text.trim()) {
        return text;
      }
    }
  }

  const primitiveEntries = Object.entries(value).filter(
    (entry): entry is [string, string | number | boolean] => {
      const candidate = entry[1];
      return typeof candidate === "string"
        || typeof candidate === "number"
        || typeof candidate === "boolean";
    },
  );

  if (primitiveEntries.length > 0 && primitiveEntries.length <= 3) {
    return primitiveEntries
      .map(([key, entry]) => `${key}: ${stringifyPrimitive(entry)}`)
      .join(" · ");
  }

  const keysCount = Object.keys(value).length;
  return keysCount === 0 ? "" : `структурированные данные (${keysCount} полей)`;
}

export function toReadableValue(value: unknown): string {
  if (value === null || value === undefined) {
    return "";
  }

  if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
    return stringifyPrimitive(value);
  }

  if (Array.isArray(value)) {
    return summarizeArray(value);
  }

  if (isRecord(value)) {
    return summarizeObject(value);
  }

  return "";
}

export function readFirstString(
  row: Record<string, unknown>,
  keys: readonly string[],
): string {
  for (const key of keys) {
    const candidate = row[key];
    if (typeof candidate === "string" && candidate.trim()) {
      return candidate;
    }
  }

  return "";
}
