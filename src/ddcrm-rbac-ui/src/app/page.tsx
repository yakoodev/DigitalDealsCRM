"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { readStoredSession } from "@/lib/auth";

export default function HomePage() {
  const router = useRouter();

  useEffect(() => {
    const session = readStoredSession();
    router.replace(session ? "/dashboard" : "/login");
  }, [router]);

  return (
    <main className="workspace-layout">
      <section className="workspace-main-card">
        <h1>Открываем DDCRM...</h1>
      </section>
    </main>
  );
}
