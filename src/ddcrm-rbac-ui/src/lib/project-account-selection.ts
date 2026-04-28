const STORAGE_KEY = "ddcrm-platform.selected-account-by-project";

type SelectionMap = Record<string, string>;

function readMap(): SelectionMap {
  if (typeof window === "undefined") {
    return {};
  }

  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) {
    return {};
  }

  try {
    const parsed = JSON.parse(raw) as SelectionMap;
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      return {};
    }

    return parsed;
  } catch {
    return {};
  }
}

export function readSelectedAccountId(projectId: string): string {
  const map = readMap();
  return (map[projectId] ?? "").trim();
}

export function writeSelectedAccountId(projectId: string, accountId: string) {
  if (typeof window === "undefined") {
    return;
  }

  const map = readMap();
  const normalizedProjectId = projectId.trim();
  const normalizedAccountId = accountId.trim();

  if (!normalizedProjectId) {
    return;
  }

  if (!normalizedAccountId) {
    delete map[normalizedProjectId];
  } else {
    map[normalizedProjectId] = normalizedAccountId;
  }

  localStorage.setItem(STORAGE_KEY, JSON.stringify(map));
}
