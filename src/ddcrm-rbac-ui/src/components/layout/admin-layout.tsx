"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { DashboardLayout } from "@/components/layout/dashboard-layout";
import { ThemeToggle } from "@/components/theme-toggle";
import type { PlatformSession } from "@/lib/auth";

type AdminTab = "overview" | "servers" | "templates";

interface AdminLayoutProps {
  session: PlatformSession;
  activeTab: AdminTab;
  onLogout: () => void;
  children: ReactNode;
}

export function AdminLayout({ session, activeTab, onLogout, children }: AdminLayoutProps) {
  const tabClass = (tab: AdminTab) =>
    `sidebar-nav-link ${activeTab === tab ? "is-active" : ""}`;

  return (
    <DashboardLayout
      sidebar={(
        <>
          <div className="sidebar-group">
            <p className="sidebar-kicker">DDCRM System Admin</p>
            <h2>{session.profile.displayName}</h2>
            <p className="sidebar-muted">{session.profile.email}</p>
            <p className="sidebar-muted">Scope: AccountManager control plane</p>
          </div>

          <nav className="sidebar-nav">
            <Link href="/dashboard" className="sidebar-nav-link">
              Dashboard
            </Link>
            <Link href="/projects" className="sidebar-nav-link">
              Проекты
            </Link>
            <Link href="/admin/account-manager" className={tabClass("overview")}>
              Admin обзор
            </Link>
            <Link href="/admin/account-manager/servers" className={tabClass("servers")}>
              Worker servers
            </Link>
            <Link href="/admin/account-manager/templates" className={tabClass("templates")}>
              Platform templates
            </Link>
            <button type="button" className="sidebar-nav-link" onClick={onLogout}>
              Выйти
            </button>
          </nav>

          <ThemeToggle />

          <div className="sidebar-group">
            <p className="sidebar-kicker">Policy</p>
            <p className="sidebar-muted">
              Доступ к этой зоне есть только при system-claim
              {" "}
              <code>system.accountManager.manage</code>.
            </p>
          </div>
        </>
      )}
      topbar={(
        <div className="topbar-content">
          <div>
            <p className="module-page-kicker">System Admin</p>
            <h1>AccountManager Control Plane</h1>
          </div>
        </div>
      )}
    >
      {children}
    </DashboardLayout>
  );
}
