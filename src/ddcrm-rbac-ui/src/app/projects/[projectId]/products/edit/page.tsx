"use client";

import { useEffect } from "react";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function LegacyProductEditRoute() {
  const { session } = useSessionGuard();
  const router = useRouter();
  const params = useParams<{ projectId: string }>();
  const searchParams = useSearchParams();

  useEffect(() => {
    if (!session) {
      return;
    }

    const accountId = searchParams.get("accountId")?.trim() ?? "";
    const productId = searchParams.get("productId")?.trim() ?? "";
    const next = new URLSearchParams();
    next.set("modal", "edit");
    if (accountId) {
      next.set("accountId", accountId);
    }
    if (productId) {
      next.set("productId", productId);
    }

    router.replace(`/projects/${params.projectId}/products?${next.toString()}`);
  }, [params.projectId, router, searchParams, session]);

  return (
    <main className="loading-shell">
      <section className="glass-card">
        <h1>Открываем редактирование товара...</h1>
      </section>
    </main>
  );
}
