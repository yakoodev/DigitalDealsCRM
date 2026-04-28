"use client";

import { useEffect } from "react";
import { useParams, useRouter } from "next/navigation";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function LegacyAccountCreateRoute() {
  const { session } = useSessionGuard();
  const router = useRouter();
  const params = useParams<{ projectId: string }>();

  useEffect(() => {
    if (!session) {
      return;
    }

    router.replace(`/projects/${params.projectId}/accounts?modal=create`);
  }, [params.projectId, router, session]);

  return (
    <main className="loading-shell">
      <section className="glass-card">
        <h1>Открываем форму добавления аккаунта...</h1>
      </section>
    </main>
  );
}
