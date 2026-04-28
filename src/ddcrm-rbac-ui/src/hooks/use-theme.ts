"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  applyThemeToDocument,
  readStoredThemePreference,
  type ResolvedTheme,
  type ThemePreference,
  writeStoredThemePreference,
} from "@/lib/theme";

interface ThemeState {
  preference: ThemePreference;
  resolvedTheme: ResolvedTheme;
  setPreference: (nextPreference: ThemePreference) => void;
}

export function useTheme(): ThemeState {
  const mediaQuery = useMemo(() => {
    if (typeof window === "undefined") {
      return null;
    }

    if (typeof window.matchMedia !== "function") {
      return null;
    }

    return window.matchMedia("(prefers-color-scheme: dark)");
  }, []);

  const [themeState, setThemeState] = useState<{
    preference: ThemePreference;
    resolvedTheme: ResolvedTheme;
  }>({
    preference: "system",
    resolvedTheme: "light",
  });

  const applyPreference = useCallback(
    (nextPreference: ThemePreference) => {
      writeStoredThemePreference(nextPreference);
      const nextResolvedTheme = applyThemeToDocument(nextPreference, mediaQuery);
      setThemeState({
        preference: nextPreference,
        resolvedTheme: nextResolvedTheme,
      });
    },
    [mediaQuery],
  );

  useEffect(() => {
    const initialPreference = readStoredThemePreference();
    const initialResolvedTheme = applyThemeToDocument(initialPreference, mediaQuery);

    const timer = window.setTimeout(() => {
      setThemeState({
        preference: initialPreference,
        resolvedTheme: initialResolvedTheme,
      });
    }, 0);

    return () => {
      window.clearTimeout(timer);
    };
  }, [mediaQuery]);

  useEffect(() => {
    applyThemeToDocument(themeState.preference, mediaQuery);
  }, [mediaQuery, themeState.preference]);

  useEffect(() => {
    if (!mediaQuery) {
      return undefined;
    }

    const handleChange = () => {
      setThemeState((previous) => {
        if (previous.preference !== "system") {
          return previous;
        }

        const nextResolvedTheme = applyThemeToDocument("system", mediaQuery);
        if (nextResolvedTheme === previous.resolvedTheme) {
          return previous;
        }

        return {
          ...previous,
          resolvedTheme: nextResolvedTheme,
        };
      });
    };

    mediaQuery.addEventListener("change", handleChange);
    return () => {
      mediaQuery.removeEventListener("change", handleChange);
    };
  }, [mediaQuery]);

  return {
    preference: themeState.preference,
    resolvedTheme: themeState.resolvedTheme,
    setPreference: applyPreference,
  };
}
