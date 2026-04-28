"use client";

import { useTheme } from "@/hooks/use-theme";
import type { ThemePreference } from "@/lib/theme";

const options: Array<{ value: ThemePreference; label: string }> = [
  { value: "system", label: "Авто" },
  { value: "light", label: "Светлая" },
  { value: "dark", label: "Темная" },
];

export function ThemeToggle() {
  const { preference, resolvedTheme, setPreference } = useTheme();

  return (
    <section className="theme-switcher" aria-label="Переключение темы">
      <div className="theme-switcher-header">
        <p className="theme-switcher-title">Тема интерфейса</p>
        <small>
          {resolvedTheme === "dark" ? "Сейчас: темная" : "Сейчас: светлая"}
        </small>
      </div>

      <div className="theme-switcher-options" role="group" aria-label="Выбор темы">
        {options.map((option) => (
          <button
            key={option.value}
            type="button"
            className={`theme-switcher-option ${preference === option.value ? "is-active" : ""}`}
            onClick={() => setPreference(option.value)}
            data-testid={`theme-option-${option.value}`}
            aria-pressed={preference === option.value}
          >
            {option.label}
          </button>
        ))}
      </div>
    </section>
  );
}
