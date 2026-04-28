"use client";

import Link from "next/link";
import { AdminLayout } from "@/components/layout/admin-layout";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function AccountManagerAdminOverviewPage() {
  const { session, logout } = useSessionGuard();

  if (!session) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>Проверяем сессию...</h1>
        </section>
      </main>
    );
  }

  if (!session.profile.isSystemAdmin) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>403 · System admin required</h1>
          <p className="route-error">
            Для доступа к `/admin/account-manager` нужен system-claim
            {" "}
            <code>system.accountManager.manage</code>.
          </p>
        </section>
      </main>
    );
  }

  return (
    <AdminLayout session={session} activeTab="overview" onLogout={logout}>
      <section className="dashboard-grid">
        <article className="hero-card">
          <p className="module-page-kicker">Control plane</p>
          <h2>Управление worker-серверами и платформенными шаблонами</h2>
          <p>
            Здесь настраиваются доступные worker servers и runtime-профили платформ.
            Эти параметры используются AccountManager при lifecycle create/migrate/rebalance.
          </p>
          <div className="hero-actions">
            <Link href="/admin/account-manager/servers" className="button button-primary">
              Открыть servers
            </Link>
            <Link href="/admin/account-manager/templates" className="button button-ghost">
              Открыть templates
            </Link>
            <Link href="/admin/account-manager/integrations" className="button button-ghost">
              Открыть integrations
            </Link>
          </div>
        </article>

        <article className="glass-card stats-card">
          <p>System claim</p>
          <strong>enabled</strong>
          <small>system.accountManager.manage</small>
        </article>
      </section>
    </AdminLayout>
  );
}
