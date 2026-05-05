"use client";

import { useParams } from "next/navigation";
import { ProjectWorkflowsPanel } from "@/components/project-pages/project-workflows-panel";
import { ProjectShell } from "@/components/project-shell";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectWorkflowsRoute() {
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
      activeTab="workflows"
      onLogout={logout}
    >
      {({ apiSession, project }) => (
        <ProjectWorkflowsPanel
          apiSession={apiSession}
          projectId={project.id}
          currentRole={session.profile.role}
        />
      )}
    </ProjectShell>
  );
}
