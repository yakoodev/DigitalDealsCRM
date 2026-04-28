"use client";

import { useEffect, useState, type ReactNode } from "react";

interface DashboardLayoutProps {
  sidebar: ReactNode;
  topbar?: ReactNode;
  children: ReactNode;
}

export function DashboardLayout({ sidebar, topbar, children }: DashboardLayoutProps) {
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);

  useEffect(() => {
    if (!mobileSidebarOpen) {
      return undefined;
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setMobileSidebarOpen(false);
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [mobileSidebarOpen]);

  return (
    <main className={`app-shell ${mobileSidebarOpen ? "is-sidebar-open" : ""}`}>
      <aside className="app-sidebar">{sidebar}</aside>

      <button
        type="button"
        className="app-sidebar-backdrop"
        aria-label="Закрыть меню"
        onClick={() => setMobileSidebarOpen(false)}
      />

      <section className="app-stage">
        <header className="app-topbar">
          <button
            type="button"
            className="mobile-sidebar-toggle"
            onClick={() => setMobileSidebarOpen((previous) => !previous)}
          >
            Меню
          </button>
          {topbar}
        </header>

        <section className="app-content">{children}</section>
      </section>
    </main>
  );
}
