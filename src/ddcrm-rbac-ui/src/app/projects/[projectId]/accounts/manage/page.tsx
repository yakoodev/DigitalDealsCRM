"use client";

import { useEffect } from "react";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function LegacyAccountManageRoute() {
  const { session } = useSessionGuard();
  const router = useRouter();
  const params = useParams<{ projectId: string }>();
  const searchParams = useSearchParams();

  useEffect(() => {
    if (!session) {
      return;
    }

    const accountId = searchParams.get("accountId")?.trim() ?? "";
    const next = new URLSearchParams();
    next.set("modal", "manage");
    if (accountId) {
      next.set("accountId", accountId);
    }
    router.replace(`/projects/${params.projectId}/accounts?${next.toString()}`);
  }, [params.projectId, router, searchParams, session]);

  return (
    <main className="loading-shell">
      <section className="glass-card">
        <h1>Открываем управление аккаунтом...</h1>
      </section>
    </main>
  );
}
