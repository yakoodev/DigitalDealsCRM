import { Suspense } from "react";
import { LoginPageClient } from "@/components/login-page-client";

export default function LoginPage() {
  return (
    <Suspense
      fallback={
        <main className="workspace-layout">
          <section className="workspace-main-card">
            <h1>Открываем форму входа...</h1>
          </section>
        </main>
      }
    >
      <LoginPageClient />
    </Suspense>
  );
}
