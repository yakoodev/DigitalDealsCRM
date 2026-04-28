import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { ThemeToggle } from "@/components/theme-toggle";
import { THEME_STORAGE_KEY } from "@/lib/theme";

function mockMatchMedia(prefersDark: boolean) {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: prefersDark,
      media: "(prefers-color-scheme: dark)",
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }),
  });
}

describe("ThemeToggle", () => {
  it("применяет системную тёмную тему при режиме system", async () => {
    mockMatchMedia(true);
    localStorage.setItem(THEME_STORAGE_KEY, "system");

    render(<ThemeToggle />);

    expect(screen.getByTestId("theme-option-system")).toHaveAttribute("aria-pressed", "true");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(document.documentElement.dataset.themePreference).toBe("system");
  });

  it("сохраняет ручной выбор темы и обновляет data-theme", async () => {
    const user = userEvent.setup();
    mockMatchMedia(false);

    render(<ThemeToggle />);

    await user.click(screen.getByTestId("theme-option-dark"));
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(screen.getByTestId("theme-option-dark")).toHaveAttribute("aria-pressed", "true");

    await user.click(screen.getByTestId("theme-option-light"));
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe("light");
    expect(document.documentElement.dataset.theme).toBe("light");
    expect(screen.getByTestId("theme-option-light")).toHaveAttribute("aria-pressed", "true");
  });
});
