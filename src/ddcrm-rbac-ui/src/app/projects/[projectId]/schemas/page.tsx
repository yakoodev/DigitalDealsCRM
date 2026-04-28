"use client";

import { useEffect } from "react";
import { useParams, useRouter } from "next/navigation";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectSchemasRoute() {
  const params = useParams<{ projectId: string }>();
  const router = useRouter();
  const { session } = useSessionGuard();

  useEffect(() => {
    if (!session) {
      return;
    }

    router.replace(`/projects/${params.projectId}/products`);
  }, [params.projectId, router, session]);

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
    <main className="loading-shell">
      <section className="glass-card">
        <h1>Открываем товары проекта...</h1>
      </section>
    </main>
  );
}
