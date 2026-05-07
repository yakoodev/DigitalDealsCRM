export const THEME_STORAGE_KEY = "ddcrm-ui-theme";

export type ThemePreference = "system" | "light" | "dark";
export type ResolvedTheme = "light" | "dark";

const validThemePreferences: ThemePreference[] = ["system", "light", "dark"];

export function isThemePreference(value: string | null | undefined): value is ThemePreference {
  if (!value) {
    return false;
  }

  return validThemePreferences.includes(value as ThemePreference);
}

export function readStoredThemePreference(): ThemePreference {
  if (typeof window === "undefined") {
    return "dark";
  }

  const raw = window.localStorage.getItem(THEME_STORAGE_KEY);
  return isThemePreference(raw) ? raw : "dark";
}

export function writeStoredThemePreference(preference: ThemePreference) {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(THEME_STORAGE_KEY, preference);
}

export function prefersDarkMode(mediaQueryList?: MediaQueryList | null): boolean {
  if (typeof window === "undefined") {
    return false;
  }

  if (mediaQueryList) {
    return mediaQueryList.matches;
  }

  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

export function resolveTheme(preference: ThemePreference, systemPrefersDark: boolean): ResolvedTheme {
  if (preference === "dark") {
    return "dark";
  }

  if (preference === "light") {
    return "light";
  }

  return systemPrefersDark ? "dark" : "light";
}

export function applyThemeToDocument(
  preference: ThemePreference,
  mediaQueryList?: MediaQueryList | null,
): ResolvedTheme {
  if (typeof document === "undefined") {
    return preference === "dark" ? "dark" : "light";
  }

  const root = document.documentElement;
  const nextResolvedTheme = resolveTheme(preference, prefersDarkMode(mediaQueryList));
  root.dataset.themePreference = preference;
  root.dataset.theme = nextResolvedTheme;
  root.style.colorScheme = nextResolvedTheme;
  return nextResolvedTheme;
}

export function getThemeInitScript() {
  return `
(() => {
  const key = "${THEME_STORAGE_KEY}";
  const valid = ["system", "light", "dark"];
  const root = document.documentElement;
  const stored = localStorage.getItem(key);
  const preference = valid.includes(stored ?? "") ? stored : "dark";
  const prefersDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
  const resolved = preference === "dark" ? "dark" : preference === "light" ? "light" : prefersDark ? "dark" : "light";
  root.dataset.themePreference = preference;
  root.dataset.theme = resolved;
  root.style.colorScheme = resolved;
})();
`.trim();
}
