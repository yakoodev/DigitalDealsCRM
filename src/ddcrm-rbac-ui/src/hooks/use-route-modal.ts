"use client";

import { useCallback, useMemo } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";

const modalQueryKeys = ["modal", "accountId", "productId", "conversationId"] as const;

interface RouteModalState {
  modal: string | null;
  accountId: string;
  productId: string;
  conversationId: string;
  openModal: (modal: string, params?: Record<string, string | null | undefined>) => void;
  closeModal: () => void;
}

export function useRouteModal(): RouteModalState {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const modal = searchParams.get("modal");
  const accountId = searchParams.get("accountId")?.trim() ?? "";
  const productId = searchParams.get("productId")?.trim() ?? "";
  const conversationId = searchParams.get("conversationId")?.trim() ?? "";

  const pushQuery = useCallback(
    (updates: Record<string, string | null | undefined>, clearModalKeys: boolean) => {
      const next = new URLSearchParams(searchParams.toString());

      if (clearModalKeys) {
        for (const key of modalQueryKeys) {
          next.delete(key);
        }
      }

      for (const [key, value] of Object.entries(updates)) {
        const normalized = value?.trim() ?? "";
        if (!normalized) {
          next.delete(key);
        } else {
          next.set(key, normalized);
        }
      }

      const nextSearch = next.toString();
      router.replace(nextSearch ? `${pathname}?${nextSearch}` : pathname);
    },
    [pathname, router, searchParams],
  );

  const openModal = useCallback(
    (nextModal: string, params?: Record<string, string | null | undefined>) => {
      pushQuery(
        {
          modal: nextModal,
          accountId: params?.accountId,
          productId: params?.productId,
          conversationId: params?.conversationId,
        },
        true,
      );
    },
    [pushQuery],
  );

  const closeModal = useCallback(() => {
    pushQuery({}, true);
  }, [pushQuery]);

  return useMemo(
    () => ({
      modal,
      accountId,
      productId,
      conversationId,
      openModal,
      closeModal,
    }),
    [accountId, closeModal, conversationId, modal, openModal, productId],
  );
}
