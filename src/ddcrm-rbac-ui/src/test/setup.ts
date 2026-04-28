import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { beforeEach } from "vitest";
import { afterEach } from "vitest";

if (typeof window !== "undefined" && typeof window.matchMedia !== "function") {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }),
  });
}

beforeEach(() => {
  document.documentElement.dataset.theme = "light";
  document.documentElement.dataset.themePreference = "system";
  document.documentElement.style.colorScheme = "light";
});

afterEach(() => {
  cleanup();
  localStorage.clear();
});
