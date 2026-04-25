"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import {
  clearStoredSession,
  readStoredSession,
  type PlatformSession,
} from "@/lib/auth";

interface SessionGuardState {
  session: PlatformSession | null;
  logout: () => void;
}

export function useSessionGuard(): SessionGuardState {
  const router = useRouter();
  const pathname = usePathname();
  const [session, setSession] = useState<PlatformSession | null>(() =>
    readStoredSession(),
  );

  const redirectTarget = useMemo(() => pathname, [pathname]);

  useEffect(() => {
    if (session) {
      return;
    }

    router.replace(`/login?next=${encodeURIComponent(redirectTarget)}`);
  }, [redirectTarget, router, session]);

  const logout = () => {
    clearStoredSession();
    setSession(null);
    router.replace("/login");
  };

  return {
    session,
    logout,
  };
}
