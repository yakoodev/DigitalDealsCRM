"use client";

import { useParams } from "next/navigation";
import { ProjectIntegrationsPanel } from "@/components/project-pages/project-integrations-panel";
import { ProjectShell } from "@/components/project-shell";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectIntegrationsRoute() {
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
      activeTab="integrations"
      onLogout={logout}
    >
      {({ apiSession, project }) => (
        <ProjectIntegrationsPanel
          apiSession={apiSession}
          projectId={project.id}
          currentRole={session.profile.role}
        />
      )}
    </ProjectShell>
  );
}
