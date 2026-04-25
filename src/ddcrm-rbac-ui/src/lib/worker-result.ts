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

export function toReadableValue(value: unknown): string {
  if (value === null || value === undefined) {
    return "";
  }

  if (
    typeof value === "string" ||
    typeof value === "number" ||
    typeof value === "boolean"
  ) {
    return String(value);
  }

  return JSON.stringify(value);
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
