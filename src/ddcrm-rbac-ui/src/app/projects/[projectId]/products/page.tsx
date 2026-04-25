"use client";

import { useParams } from "next/navigation";
import { ProjectProductsPanel } from "@/components/project-pages/products-panel";
import { ProjectShell } from "@/components/project-shell";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectProductsRoute() {
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
      activeTab="products"
      onLogout={logout}
    >
      {({ apiSession, project }) => (
        <ProjectProductsPanel apiSession={apiSession} projectId={project.id} />
      )}
    </ProjectShell>
  );
}
