"use client";

import { useEffect } from "react";
import { useParams, useRouter } from "next/navigation";

export default function ProjectRootRedirectPage() {
  const router = useRouter();
  const params = useParams<{ projectId: string }>();

  useEffect(() => {
    router.replace(`/projects/${params.projectId}/accounts`);
  }, [params.projectId, router]);

  return (
    <main className="workspace-layout">
      <section className="workspace-main-card">
        <h1>Открываем проект...</h1>
      </section>
    </main>
  );
}
