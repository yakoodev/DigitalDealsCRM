"use client";

import { useEffect } from "react";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function LegacyMessageThreadRoute() {
  const { session } = useSessionGuard();
  const router = useRouter();
  const params = useParams<{ projectId: string }>();
  const searchParams = useSearchParams();

  useEffect(() => {
    if (!session) {
      return;
    }

    const accountId = searchParams.get("accountId")?.trim() ?? "";
    const conversationId = searchParams.get("conversationId")?.trim() ?? "";
    const next = new URLSearchParams();
    next.set("modal", "thread");
    if (accountId) {
      next.set("accountId", accountId);
    }
    if (conversationId) {
      next.set("conversationId", conversationId);
    }

    router.replace(`/projects/${params.projectId}/messages?${next.toString()}`);
  }, [params.projectId, router, searchParams, session]);

  return (
    <main className="loading-shell">
      <section className="glass-card">
        <h1>Открываем чат переписки...</h1>
      </section>
    </main>
  );
}
