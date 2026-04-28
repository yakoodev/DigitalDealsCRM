"use client";

import { useParams } from "next/navigation";
import { ProjectOverviewPanel } from "@/components/project-pages/project-overview-panel";
import { ProjectShell } from "@/components/project-shell";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectOverviewRoute() {
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
      activeTab="overview"
      onLogout={logout}
    >
      {({ apiSession, project }) => (
        <ProjectOverviewPanel apiSession={apiSession} projectId={project.id} />
      )}
    </ProjectShell>
  );
}
