"use client";

import { useEffect } from "react";
import { useParams, useRouter } from "next/navigation";

export default function ProjectWorkflowsRoute() {
  const params = useParams<{ projectId: string }>();
  const router = useRouter();

  useEffect(() => {
    router.replace(`/projects/${params.projectId}/offers`);
  }, [params.projectId, router]);

  return (
    <main className="loading-shell">
      <section className="glass-card">
        <h1>Перенаправляем в Offers...</h1>
      </section>
    </main>
  );
}
