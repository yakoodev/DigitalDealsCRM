"use client";

import type { ReactElement } from "react";
import { useTheme } from "@/hooks/use-theme";
import type { ThemePreference } from "@/lib/theme";

const options: Array<{
  value: ThemePreference;
  label: string;
  icon: ReactElement;
}> = [
  {
    value: "dark",
    label: "Темная тема",
    icon: (
      <svg viewBox="0 0 24 24" className="theme-switcher-icon" aria-hidden="true">
        <path
          d="M14.5 3.5a8.5 8.5 0 1 0 6 14.5 9 9 0 1 1-6-14.5Z"
          fill="currentColor"
        />
      </svg>
    ),
  },
  {
    value: "system",
    label: "Системная тема",
    icon: (
      <svg viewBox="0 0 24 24" className="theme-switcher-icon" aria-hidden="true">
        <path
          d="M4 5.5A1.5 1.5 0 0 1 5.5 4h13A1.5 1.5 0 0 1 20 5.5v8A1.5 1.5 0 0 1 18.5 15h-5v2h2a1 1 0 1 1 0 2h-7a1 1 0 0 1 0-2h2v-2h-5A1.5 1.5 0 0 1 4 13.5v-8Zm2 .5v7h12V6H6Z"
          fill="currentColor"
        />
      </svg>
    ),
  },
  {
    value: "light",
    label: "Светлая тема",
    icon: (
      <svg viewBox="0 0 24 24" className="theme-switcher-icon" aria-hidden="true">
        <path
          d="M12 4.5a1 1 0 0 1 1 1V7a1 1 0 1 1-2 0V5.5a1 1 0 0 1 1-1Zm0 12a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Zm0 3a1 1 0 0 1 1 1V22a1 1 0 1 1-2 0v-1.5a1 1 0 0 1 1-1Zm7-7.5a1 1 0 0 1 1-1h1.5a1 1 0 1 1 0 2H20a1 1 0 0 1-1-1ZM2.5 12a1 1 0 0 1 1-1H5a1 1 0 1 1 0 2H3.5a1 1 0 0 1-1-1Zm14.95-5.45a1 1 0 0 1 1.41 0l1.06 1.06a1 1 0 1 1-1.41 1.41l-1.06-1.06a1 1 0 0 1 0-1.41ZM5.08 17.86a1 1 0 0 1 1.41 0l1.06 1.06a1 1 0 0 1-1.41 1.41l-1.06-1.06a1 1 0 0 1 0-1.41Zm1.41-9.25a1 1 0 0 1-1.41 0L4.02 7.55a1 1 0 0 1 1.41-1.41l1.06 1.06a1 1 0 0 1 0 1.41Zm12.37 9.25a1 1 0 0 1 0 1.41l-1.06 1.06a1 1 0 0 1-1.41-1.41l1.06-1.06a1 1 0 0 1 1.41 0Z"
          fill="currentColor"
        />
      </svg>
    ),
  },
];

export function ThemeToggle() {
  const { preference, setPreference } = useTheme();

  return (
    <section className="theme-switcher" aria-label="Переключение темы">
      <div className="theme-switcher-options" role="group" aria-label="Выбор темы">
        {options.map((option) => (
          <button
            key={option.value}
            type="button"
            className={`theme-switcher-option ${preference === option.value ? "is-active" : ""}`}
            onClick={() => setPreference(option.value)}
            data-testid={`theme-option-${option.value}`}
            aria-pressed={preference === option.value}
            aria-label={option.label}
            title={option.label}
          >
            {option.icon}
            <span className="sr-only">{option.label}</span>
          </button>
        ))}
      </div>
    </section>
  );
}
