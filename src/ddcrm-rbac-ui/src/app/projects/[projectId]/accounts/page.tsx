"use client";

import { useParams } from "next/navigation";
import { ProjectAccountsPanel } from "@/components/project-pages/accounts-panel";
import { ProjectShell } from "@/components/project-shell";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectAccountsRoute() {
  const params = useParams<{ projectId: string }>();
  const projectId = params.projectId;
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

  return (
    <ProjectShell
      session={session}
      projectId={projectId}
      activeTab="accounts"
      onLogout={logout}
    >
      {({ apiSession, project }) => (
        <ProjectAccountsPanel
          apiSession={apiSession}
          projectId={project.id}
          activeRole={session.profile.role}
          canOpenIntegrationGrants={session.profile.isSystemAdmin === true}
        />
      )}
    </ProjectShell>
  );
}
