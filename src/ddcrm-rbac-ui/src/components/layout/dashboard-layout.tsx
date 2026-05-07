"use client";

import type { ReactNode } from "react";

interface DashboardLayoutProps {
  header: ReactNode;
  children: ReactNode;
  mainClassName?: string;
}

export function DashboardLayout({ header, children, mainClassName }: DashboardLayoutProps) {
  return (
    <div className="app-shell">
      <header className="app-header">{header}</header>
      <main className={`app-main ${mainClassName ?? ""}`.trim()}>{children}</main>
    </div>
  );
}
