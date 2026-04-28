"use client";

import { useQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import type { Account } from "@/generated/external-api";
import { type ApiSession, listAccountsRequest } from "@/lib/api-client";
import {
  readSelectedAccountId,
  writeSelectedAccountId,
} from "@/lib/project-account-selection";

interface UseProjectAccountsResult {
  accounts: Account[];
  selectedAccountId: string;
  selectedAccount: Account | null;
  isLoading: boolean;
  error: Error | null;
  queryKey: readonly [string, string, string, string];
  setSelectedAccountId: (value: string) => void;
}

export function useProjectAccounts(
  apiSession: ApiSession,
  projectId: string,
): UseProjectAccountsResult {
  const queryKey = useMemo(
    () =>
      [
        "accounts",
        apiSession.baseUrl,
        apiSession.token,
        projectId,
      ] as const,
    [apiSession.baseUrl, apiSession.token, projectId],
  );

  const accountsQuery = useQuery({
    queryKey,
    queryFn: () => listAccountsRequest(apiSession, projectId),
    enabled: Boolean(projectId),
  });

  const [manualSelectionMap, setManualSelectionMap] = useState<Record<string, string>>(
    {},
  );
  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data]);

  const preferredAccountId =
    (manualSelectionMap[projectId] ?? "").trim() || readSelectedAccountId(projectId);

  const selectedAccountId = useMemo(() => {
    if (accounts.length === 0) {
      return "";
    }

    if (accounts.some((account) => account.id === preferredAccountId)) {
      return preferredAccountId;
    }

    return accounts[0].id;
  }, [accounts, preferredAccountId]);

  useEffect(() => {
    writeSelectedAccountId(projectId, selectedAccountId);
  }, [projectId, selectedAccountId]);

  const selectedAccount = useMemo(
    () => accounts.find((account) => account.id === selectedAccountId) ?? null,
    [accounts, selectedAccountId],
  );

  const setSelectedAccountId = (value: string) => {
    const normalized = value.trim();
    setManualSelectionMap((previous) => ({
      ...previous,
      [projectId]: normalized,
    }));
    writeSelectedAccountId(projectId, normalized);
  };

  return {
    accounts,
    selectedAccountId,
    selectedAccount,
    isLoading: accountsQuery.isPending,
    error: accountsQuery.error instanceof Error ? accountsQuery.error : null,
    queryKey,
    setSelectedAccountId,
  };
}
