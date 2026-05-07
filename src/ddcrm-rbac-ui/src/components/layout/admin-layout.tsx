"use client";

import type { ReactNode } from "react";
import {
  HeaderBrand,
  HeaderDropdown,
  HeaderEmail,
  HeaderMeta,
  HeaderLink,
  HeaderNav,
  HeaderSection,
  HeaderStatus,
} from "@/components/layout/app-header-primitives";
import { DashboardLayout } from "@/components/layout/dashboard-layout";
import { ThemeToggle } from "@/components/theme-toggle";
import type { PlatformSession } from "@/lib/auth";

type AdminTab = "overview" | "servers" | "templates" | "integrations";

interface AdminLayoutProps {
  session: PlatformSession;
  activeTab: AdminTab;
  onLogout: () => void;
  children: ReactNode;
}

export function AdminLayout({ session, activeTab, onLogout, children }: AdminLayoutProps) {
  const adminItems = [
    { href: "/admin/account-manager", label: "Admin обзор", active: activeTab === "overview" },
    { href: "/admin/account-manager/servers", label: "Worker servers", active: activeTab === "servers" },
    { href: "/admin/account-manager/templates", label: "Platform templates", active: activeTab === "templates" },
    { href: "/admin/account-manager/integrations", label: "Integrations", active: activeTab === "integrations" },
  ] as const;

  return (
    <DashboardLayout
      header={(
        <>
          <HeaderSection align="left">
            <HeaderBrand label="DDCRM Platform" href="/projects" />
            <HeaderNav>
              <HeaderLink href="/projects" label="Dashboard" />
              <HeaderDropdown label="Admin" active items={[...adminItems]} />
            </HeaderNav>
          </HeaderSection>
          <HeaderSection align="right">
            <HeaderStatus>
              <span className="status status--info">system admin</span>
              <HeaderMeta>
                <HeaderEmail value={session.profile.email} />
                <ThemeToggle />
                <button type="button" className="button button-ghost button-small" onClick={onLogout}>
                  Выйти
                </button>
              </HeaderMeta>
            </HeaderStatus>
          </HeaderSection>
        </>
      )}
    >
      <section className="page-wide">
        {children}
      </section>
    </DashboardLayout>
  );
}
