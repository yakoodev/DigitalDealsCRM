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
      <div className="page-stack">
        <section className="page-head">
          <div className="page-head__row">
            <div>
              <h1 className="page-title">AccountManager</h1>
              <p className="route-hint">Worker servers, platform templates и integration grants в единой панели.</p>
            </div>
            <div className="inline">
              <Link href="/admin/account-manager/servers" className="button button-primary button-small">
                Servers
              </Link>
              <Link href="/admin/account-manager/templates" className="button button-ghost button-small">
                Templates
              </Link>
              <Link href="/admin/account-manager/integrations" className="button button-ghost button-small">
                Integrations
              </Link>
            </div>
          </div>
          <div className="status status--ok">system.accountManager.manage</div>
        </section>

        <section className="state-grid">
          <article className="state-card">
            <span className="label">System claim</span>
            <strong>enabled</strong>
          </article>
          <article className="state-card">
            <span className="label">Role</span>
            <strong>{session.profile.role}</strong>
          </article>
          <article className="state-card">
            <span className="label">System admin</span>
            <strong>{session.profile.isSystemAdmin ? "yes" : "no"}</strong>
          </article>
          <article className="state-card">
            <span className="label">Active routes</span>
            <strong>3</strong>
          </article>
        </section>

        <section className="card page-stack">
          <h2 className="card__title">Что дальше</h2>
          <ul className="entity-list">
            <li className="entity-list-item">
              <div>
                <strong>Worker servers</strong>
                <p className="route-hint">Пулы размещения, лимиты, health и registry.</p>
              </div>
              <Link href="/admin/account-manager/servers" className="button button-ghost button-small">Открыть</Link>
            </li>
            <li className="entity-list-item">
              <div>
                <strong>Platform templates</strong>
                <p className="route-hint">Каталог образов, команд и runtime параметров.</p>
              </div>
              <Link href="/admin/account-manager/templates" className="button button-ghost button-small">Открыть</Link>
            </li>
            <li className="entity-list-item">
              <div>
                <strong>Integrations</strong>
                <p className="route-hint">Матрица grant/revoke, runtime-операции и Telegram proxy.</p>
              </div>
              <Link href="/admin/account-manager/integrations" className="button button-ghost button-small">Открыть</Link>
            </li>
          </ul>
        </section>
      </div>
    </AdminLayout>
  );
}
