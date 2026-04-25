"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo } from "react";
import { AuthScreen } from "@/components/auth-screen";
import { readStoredSession, storeSession, type PlatformSession } from "@/lib/auth";

export function LoginPageClient() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const nextPath = useMemo(() => {
    const candidate = searchParams.get("next");
    if (!candidate || !candidate.startsWith("/")) {
      return "/projects";
    }

    return candidate;
  }, [searchParams]);

  const existingSession = useMemo(() => readStoredSession(), []);

  useEffect(() => {
    if (existingSession) {
      router.replace(nextPath);
    }
  }, [existingSession, nextPath, router]);

  const handleAuthenticated = (session: PlatformSession) => {
    storeSession(session);
    router.replace(nextPath);
  };

  if (existingSession) {
    return null;
  }

  return (
    <div data-testid="login-page">
      <AuthScreen onAuthenticated={handleAuthenticated} />
    </div>
  );
}
