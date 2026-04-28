"use client";

import type { Account } from "@/generated/external-api";

interface AccountSelectorProps {
  accounts: Account[];
  selectedAccountId: string;
  onChange: (accountId: string) => void;
  isLoading: boolean;
}

export function AccountSelector({
  accounts,
  selectedAccountId,
  onChange,
  isLoading,
}: AccountSelectorProps) {
  return (
    <label className="field">
      <span>Аккаунт</span>
      <select
        className="input"
        value={selectedAccountId}
        onChange={(event) => onChange(event.target.value)}
        disabled={isLoading || accounts.length === 0}
      >
        {accounts.length === 0 ? (
          <option value="">Нет доступных аккаунтов</option>
        ) : null}
        {accounts.map((account) => (
          <option key={account.id} value={account.id}>
            {account.displayName} · {account.platform} · {account.businessStatus}
          </option>
        ))}
      </select>
    </label>
  );
}
