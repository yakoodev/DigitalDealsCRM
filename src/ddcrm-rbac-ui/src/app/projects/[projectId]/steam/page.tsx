"use client";

import { useParams, useSearchParams } from "next/navigation";
import { ProjectShell } from "@/components/project-shell";
import { ProjectSteamPanel } from "@/components/project-pages/project-steam-panel";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectSteamRoute() {
  const params = useParams<{ projectId: string }>();
  const searchParams = useSearchParams();
  const projectId = params.projectId;
  const instanceId = searchParams.get("instanceId") ?? undefined;
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
      activeTab="steam"
      onLogout={logout}
    >
      {({ apiSession, project }) => (
        <ProjectSteamPanel apiSession={apiSession} projectId={project.id} instanceId={instanceId} />
      )}
    </ProjectShell>
  );
}
