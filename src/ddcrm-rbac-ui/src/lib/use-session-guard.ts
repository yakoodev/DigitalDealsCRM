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
  const [session, setSession] = useState<PlatformSession | null>(null);
  const [isInitialized, setIsInitialized] = useState(false);

  const redirectTarget = useMemo(() => {
    if (typeof window === "undefined") {
      return pathname;
    }

    const nextSearch = window.location.search;
    return nextSearch ? `${pathname}${nextSearch}` : pathname;
  }, [pathname]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSession(readStoredSession());
      setIsInitialized(true);
    }, 0);

    return () => {
      window.clearTimeout(timer);
    };
  }, []);

  useEffect(() => {
    if (!isInitialized || session) {
      return;
    }

    router.replace(`/login?next=${encodeURIComponent(redirectTarget)}`);
  }, [isInitialized, redirectTarget, router, session]);

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
