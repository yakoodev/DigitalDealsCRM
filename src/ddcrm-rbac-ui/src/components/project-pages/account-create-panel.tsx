"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import {
  createAccountRequest,
  listProjectAccountTypesRequest,
  type ApiSession,
  type ProjectAccountType,
} from "@/lib/api-client";

interface ProjectAccountCreatePanelProps {
  apiSession: ApiSession;
  projectId: string;
}

type AccountDraft = Record<string, string>;

function buildDefaultDraft(accountType: ProjectAccountType): AccountDraft {
  const draft: AccountDraft = {};
  for (const field of accountType.formFields) {
    draft[field.key] = field.defaultValue ?? "";
  }

  if (!draft.displayName) {
    draft.displayName = accountType.displayName;
  }

  return draft;
}

function readRequiredDraftValue(
  draft: AccountDraft,
  fieldKey: string,
  displayName: string,
) {
  const value = (draft[fieldKey] ?? "").trim();
  if (!value) {
    throw new Error(`Поле «${displayName}» обязательно.`);
  }

  return value;
}

export function ProjectAccountCreatePanel({
  apiSession,
  projectId,
}: ProjectAccountCreatePanelProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [selectedTypeId, setSelectedTypeId] = useState("");
  const [draftsByType, setDraftsByType] = useState<Record<string, AccountDraft>>({});
  const [status, setStatus] = useState("Выберите тип аккаунта и заполните форму.");

  const accountTypesQuery = useQuery({
    queryKey: ["account-types", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listProjectAccountTypesRequest(apiSession, projectId),
  });

  const accountTypes = useMemo(
    () => accountTypesQuery.data ?? [],
    [accountTypesQuery.data],
  );

  const selectedAccountType = useMemo(
    () =>
      accountTypes.find((accountType) => accountType.accountTypeId === selectedTypeId)
      ?? accountTypes[0]
      ?? null,
    [accountTypes, selectedTypeId],
  );

  const activeDraft = useMemo(() => {
    if (!selectedAccountType) {
      return {};
    }

    return (
      draftsByType[selectedAccountType.accountTypeId]
      ?? buildDefaultDraft(selectedAccountType)
    );
  }, [draftsByType, selectedAccountType]);

  const createAccountMutation = useMutation({
    mutationFn: async () => {
      if (!selectedAccountType) {
        throw new Error("Нет доступного типа аккаунта для создания.");
      }

      const displayName = readRequiredDraftValue(
        activeDraft,
        "displayName",
        "Название аккаунта",
      );
      const proxyHost = readRequiredDraftValue(activeDraft, "proxyHost", "Proxy host");
      const proxyLogin = readRequiredDraftValue(activeDraft, "proxyLogin", "Proxy login");
      const proxyPassword = readRequiredDraftValue(
        activeDraft,
        "proxyPassword",
        "Proxy password",
      );
      const proxyPortRaw = readRequiredDraftValue(activeDraft, "proxyPort", "Proxy port");
      const proxyPort = Number(proxyPortRaw);
      if (!Number.isInteger(proxyPort) || proxyPort < 1 || proxyPort > 65535) {
        throw new Error("Proxy port должен быть целым числом от 1 до 65535.");
      }

      return createAccountRequest(apiSession, projectId, {
        accountTypeId: selectedAccountType.accountTypeId,
        platform: selectedAccountType.platform,
        displayName,
        proxyConfig: {
          host: proxyHost,
          port: proxyPort,
          login: proxyLogin,
          password: proxyPassword,
        },
      });
    },
    onSuccess: async (account) => {
      await queryClient.invalidateQueries({
        queryKey: ["accounts", apiSession.baseUrl, apiSession.token, projectId],
      });
      setStatus("Аккаунт добавлен в проект. Возвращаемся к списку.");
      router.push(`/projects/${projectId}/accounts`);
      return account;
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось создать аккаунт.");
    },
  });

  const updateDraftField = (fieldKey: string, value: string) => {
    if (!selectedAccountType) {
      return;
    }

    const accountTypeId = selectedAccountType.accountTypeId;
    setDraftsByType((previous) => ({
      ...previous,
      [accountTypeId]: {
        ...(previous[accountTypeId] ?? buildDefaultDraft(selectedAccountType)),
        [fieldKey]: value,
      },
    }));
  };

  return (
    <div className="page-stack" data-testid="project-account-create-panel">
      <header className="page-section-header">
        <h2>Добавить аккаунт</h2>
        <p>Выберите тип аккаунта из каталога Accounts Manager и заполните форму подключения.</p>
      </header>

      <div className="panel-actions">
        <Link href={`/projects/${projectId}/accounts`} className="button button-ghost">
          Назад к аккаунтам
        </Link>
      </div>

      {accountTypesQuery.isPending ? (
        <section className="panel-card">
          <p className="route-hint">Загружаем типы аккаунтов...</p>
        </section>
      ) : null}

      {accountTypesQuery.error ? (
        <section className="panel-card">
          <p className="route-error">
            {accountTypesQuery.error instanceof Error
              ? accountTypesQuery.error.message
              : "Не удалось получить каталог типов аккаунтов."}
          </p>
        </section>
      ) : null}

      {!accountTypesQuery.isPending && !accountTypesQuery.error && accountTypes.length === 0 ? (
        <section className="panel-card">
          <p className="route-hint">
            Accounts Manager пока не вернул доступные типы аккаунтов.
          </p>
        </section>
      ) : null}

      {!accountTypesQuery.isPending && !accountTypesQuery.error && accountTypes.length > 0 ? (
        <>
          <section className="panel-card">
            <h3>Доступные типы</h3>
            <div className="type-tabs" role="tablist" aria-label="Account types">
              {accountTypes.map((accountType) => {
                const isActive = selectedAccountType?.accountTypeId === accountType.accountTypeId;
                return (
                  <button
                    key={accountType.accountTypeId}
                    type="button"
                    role="tab"
                    aria-selected={isActive}
                    className={`type-tab ${isActive ? "is-active" : ""}`}
                    onClick={() => setSelectedTypeId(accountType.accountTypeId)}
                  >
                    <strong>{accountType.displayName}</strong>
                    <small>{accountType.platform}</small>
                  </button>
                );
              })}
            </div>
          </section>

          {selectedAccountType ? (
            <section className="panel-card page-stack">
              <div>
                <h3>{selectedAccountType.displayName}</h3>
                <p className="route-hint">
                  {selectedAccountType.description || "Заполните обязательные поля."}
                </p>
                <p className="route-hint">
                  Профиль воркера: <strong>{selectedAccountType.workerProfileId}</strong>
                </p>
              </div>

              <div className="stacked-block">
                {selectedAccountType.formFields.map((field) => {
                  const value = activeDraft[field.key] ?? "";
                  const inputType = field.inputType === "password" ? "password" : "text";
                  const isNumber = field.inputType === "number";

                  return (
                    <label key={field.key} className="field">
                      <span>
                        {field.label}
                        {field.required ? " *" : ""}
                      </span>
                      <input
                        className="input"
                        type={isNumber ? "number" : inputType}
                        value={value}
                        placeholder={field.placeholder ?? ""}
                        onChange={(event) => updateDraftField(field.key, event.target.value)}
                        min={isNumber ? 1 : undefined}
                        max={isNumber ? 65535 : undefined}
                        step={isNumber ? 1 : undefined}
                      />
                    </label>
                  );
                })}
              </div>

              <div className="panel-actions">
                <button
                  type="button"
                  className="button button-primary"
                  disabled={createAccountMutation.isPending}
                  onClick={() => createAccountMutation.mutate()}
                >
                  Добавить аккаунт в проект
                </button>
                <p className="route-hint">{status}</p>
              </div>
            </section>
          ) : null}
        </>
      ) : null}
    </div>
  );
}
