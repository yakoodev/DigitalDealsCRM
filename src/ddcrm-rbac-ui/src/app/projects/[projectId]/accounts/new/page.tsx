"use client";

import { useParams } from "next/navigation";
import { ProjectAccountCreatePanel } from "@/components/project-pages/account-create-panel";
import { ProjectShell } from "@/components/project-shell";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectAccountCreateRoute() {
  const params = useParams<{ projectId: string }>();
  const projectId = params.projectId;
  const { session, logout } = useSessionGuard();

  if (!session) {
    return (
      <main className="workspace-layout">
        <section className="workspace-main-card">
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
        <ProjectAccountCreatePanel apiSession={apiSession} projectId={project.id} />
      )}
    </ProjectShell>
  );
}
