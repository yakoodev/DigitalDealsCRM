"use client";
/* eslint-disable react-hooks/set-state-in-effect */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import type { ApiSession, ProjectCustomHttpIntegration, ProjectIntegrationStatus } from "@/lib/api-client";
import {
  createProjectCustomHttpIntegrationRequest,
  createProjectIntegrationInstanceRequest,
  createTelegramLinkCodeRequest,
  deleteProjectCustomHttpIntegrationRequest,
  deleteProjectIntegrationInstanceRequest,
  invokeProjectIntegrationActionRequest,
  listProjectIntegrationInstancesRequest,
  listProjectCustomHttpIntegrationsRequest,
  listProjectIntegrationsStatusRequest,
  triggerProjectIntegrationInstanceRuntimeRequest,
  testProjectCustomHttpIntegrationRequest,
  triggerProjectIntegrationRuntimeRequest,
  updateProjectIntegrationInstanceRequest,
  updateProjectCustomHttpIntegrationRequest,
} from "@/lib/api-client";
import { hasPermission, projectPermissions, type ProjectRole } from "@/lib/rbac";

interface ProjectIntegrationsPanelProps {
  apiSession: ApiSession;
  projectId: string;
  currentRole: ProjectRole;
}

interface InvokeParamRow {
  id: string;
  key: string;
  value: string;
}

interface IntegrationActionTemplateField {
  key: string;
  label: string;
  placeholder: string;
  defaultValue?: string;
}

interface IntegrationActionTemplate {
  id: string;
  integrationKey: string;
  scope: "read" | "jobs";
  title: string;
  description: string;
  fields: IntegrationActionTemplateField[];
}

const integrationActionTemplates: readonly IntegrationActionTemplate[] = [
  {
    id: "funpay-read-overview",
    integrationKey: "funpaystat",
    scope: "read",
    title: "FunPay: сводка за период",
    description: "Базовый read-запрос статистики с периодом и лимитом.",
    fields: [
      { key: "periodDays", label: "Период (дней)", placeholder: "7", defaultValue: "7" },
      { key: "limit", label: "Лимит записей", placeholder: "50", defaultValue: "50" },
      { key: "includeArchived", label: "includeArchived", placeholder: "false", defaultValue: "false" },
    ],
  },
  {
    id: "funpay-jobs-sync",
    integrationKey: "funpaystat",
    scope: "jobs",
    title: "FunPay: запуск sync-job",
    description: "Постановка jobs-задачи синхронизации/обновления аналитики.",
    fields: [
      { key: "jobType", label: "Тип job", placeholder: "sync", defaultValue: "sync" },
      { key: "priority", label: "Приоритет", placeholder: "normal", defaultValue: "normal" },
      { key: "dryRun", label: "dryRun", placeholder: "false", defaultValue: "false" },
    ],
  },
] as const;

const TELEGRAM_BOT_USERNAME_STORAGE_KEY = "ddcrm.telegram.botUsername";
const DEFAULT_TELEGRAM_BOT_USERNAME = "DigitalDealsCRMBot";
const CUSTOM_HTTP_INTEGRATION_KEY = "custom-http";

function hasCustomHttpGrant(items: ProjectIntegrationStatus[]) {
  return items.some(
    (item) =>
      item.integrationKey === CUSTOM_HTTP_INTEGRATION_KEY
      && item.status.toLowerCase() === "active"
      && item.scopes.some((scope) => scope.toLowerCase() === "use"),
  );
}

function parseStringMapJson(text: string, label: string) {
  const trimmed = text.trim();
  if (!trimmed) {
    return undefined;
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch {
    throw new Error(`${label}: ожидается валидный JSON-объект.`);
  }

  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new Error(`${label}: ожидается JSON-объект.`);
  }

  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(parsed as Record<string, unknown>)) {
    if (!key.trim()) {
      continue;
    }
    if (value === null || value === undefined) {
      continue;
    }

    result[key.trim()] = typeof value === "string" ? value : String(value);
  }

  return Object.keys(result).length > 0 ? result : undefined;
}

function parseUnknownObjectJson(text: string, label: string) {
  const trimmed = text.trim();
  if (!trimmed) {
    return undefined;
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch {
    throw new Error(`${label}: ожидается валидный JSON-объект.`);
  }

  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new Error(`${label}: ожидается JSON-объект.`);
  }

  return parsed as Record<string, unknown>;
}

function readInvokeCandidates(items: ProjectIntegrationStatus[]) {
  return items.filter((item) => item.integrationType === "service");
}

function parseInvokeValue(raw: string): unknown {
  const trimmed = raw.trim();
  if (!trimmed.length) {
    return "";
  }

  const lower = trimmed.toLowerCase();
  if (lower === "true") {
    return true;
  }

  if (lower === "false") {
    return false;
  }

  if (/^-?\d+(\.\d+)?$/.test(trimmed)) {
    const numeric = Number(trimmed);
    if (Number.isFinite(numeric)) {
      return numeric;
    }
  }

  if ((trimmed.startsWith("{") && trimmed.endsWith("}")) || (trimmed.startsWith("[") && trimmed.endsWith("]"))) {
    try {
      return JSON.parse(trimmed);
    } catch {
      return trimmed;
    }
  }

  return trimmed;
}

function buildParamRows(fields: readonly IntegrationActionTemplateField[]): InvokeParamRow[] {
  if (fields.length === 0) {
    return [{ id: "param-1", key: "", value: "" }];
  }

  return fields.map((field, index) => ({
    id: `${field.key}-${index + 1}`,
    key: field.key,
    value: field.defaultValue ?? "",
  }));
}

function normalizeTelegramBotUsername(raw: string) {
  return raw.trim().replaceAll("@", "").replaceAll(" ", "");
}

function buildTelegramLinkCommand(code: string) {
  return `/link ${code.trim()}`;
}

function buildTelegramStartPayload(code: string) {
  return `link_${code.trim()}`;
}

async function copyTextToClipboard(text: string) {
  if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text);
    return;
  }

  if (typeof document === "undefined") {
    throw new Error("Clipboard недоступен в текущем окружении.");
  }

  const input = document.createElement("textarea");
  input.value = text;
  input.setAttribute("readonly", "true");
  input.style.position = "fixed";
  input.style.left = "-9999px";
  document.body.appendChild(input);
  input.select();
  const success = document.execCommand("copy");
  document.body.removeChild(input);
  if (!success) {
    throw new Error("Не удалось скопировать команду в буфер обмена.");
  }
}

export function ProjectIntegrationsPanel({ apiSession, projectId, currentRole }: ProjectIntegrationsPanelProps) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const canManageCustomHttp = hasPermission(currentRole, projectPermissions.customIntegrationsManage);
  const [selectedIntegrationKey, setSelectedIntegrationKey] = useState("");
  const [invokeScope, setInvokeScope] = useState<"read" | "jobs">("read");
  const [invokeParams, setInvokeParams] = useState<InvokeParamRow[]>([
    { id: "param-1", key: "", value: "" },
  ]);
  const [useAdvancedJson, setUseAdvancedJson] = useState(false);
  const [advancedPayloadText, setAdvancedPayloadText] = useState("{}");
  const [selectedTemplateId, setSelectedTemplateId] = useState("");
  const [invokeResult, setInvokeResult] = useState<string>("");
  const [statusMessage, setStatusMessage] = useState(
    "Управляйте runtime интеграций и запускайте read/jobs через удобную форму параметров.",
  );
  const [telegramLinkCode, setTelegramLinkCode] = useState<{
    code: string;
    bindingType: "group" | "user";
    expiresAtUtc: string;
  } | null>(null);
  const [telegramBotUsername, setTelegramBotUsername] = useState(() => {
    if (typeof window === "undefined") {
      return DEFAULT_TELEGRAM_BOT_USERNAME;
    }

    const saved = window.localStorage.getItem(TELEGRAM_BOT_USERNAME_STORAGE_KEY);
    return saved && saved.trim() ? saved : DEFAULT_TELEGRAM_BOT_USERNAME;
  });
  const [telegramBindFlowStatus, setTelegramBindFlowStatus] = useState("");
  const [copyingTelegramCommand, setCopyingTelegramCommand] = useState(false);
  const [editingCustomHttpId, setEditingCustomHttpId] = useState("");
  const [customHttpName, setCustomHttpName] = useState("");
  const [customHttpBaseUrl, setCustomHttpBaseUrl] = useState("");
  const [customHttpBearerToken, setCustomHttpBearerToken] = useState("");
  const [customHttpStatus, setCustomHttpStatus] = useState<"active" | "disabled">("active");
  const [customHttpHeadersJson, setCustomHttpHeadersJson] = useState("{}");
  const [customHttpTestMethod, setCustomHttpTestMethod] = useState("POST");
  const [customHttpTestPath, setCustomHttpTestPath] = useState("");
  const [customHttpTestHeadersJson, setCustomHttpTestHeadersJson] = useState("{}");
  const [customHttpTestPayloadJson, setCustomHttpTestPayloadJson] = useState("{}");
  const [customHttpTestResult, setCustomHttpTestResult] = useState("");
  const [steamRuntimeProxyHost, setSteamRuntimeProxyHost] = useState("");
  const [steamRuntimeProxyPort, setSteamRuntimeProxyPort] = useState("8080");
  const [steamRuntimeProxyLogin, setSteamRuntimeProxyLogin] = useState("");
  const [steamRuntimeProxyPassword, setSteamRuntimeProxyPassword] = useState("");
  const [steamInstanceDisplayName, setSteamInstanceDisplayName] = useState("");
  const [steamInstanceAutoProvision, setSteamInstanceAutoProvision] = useState(true);
  const [steamInstanceMakeDefault, setSteamInstanceMakeDefault] = useState(true);

  const statusQuery = useQuery({
    queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listProjectIntegrationsStatusRequest(apiSession, projectId),
    staleTime: 10_000,
  });

  const integrationItems = useMemo(
    () => statusQuery.data?.items ?? [],
    [statusQuery.data?.items],
  );
  const steamGrantItem = useMemo(
    () =>
      integrationItems.find(
        (item) => item.integrationKey === "steam-accounts-manager",
      ) ?? null,
    [integrationItems],
  );
  const steamInstancesQuery = useQuery({
    queryKey: ["steam-instances", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listProjectIntegrationInstancesRequest(apiSession, projectId, "steam-accounts-manager"),
    enabled: steamGrantItem?.status === "active",
    staleTime: 10_000,
  });
  const steamInstances = useMemo(
    () => steamInstancesQuery.data?.items ?? [],
    [steamInstancesQuery.data?.items],
  );
  const steamMaxInstances = steamInstancesQuery.data?.maxInstances ?? steamGrantItem?.maxInstances ?? 1;
  const customHttpGrantActive = useMemo(
    () => hasCustomHttpGrant(integrationItems),
    [integrationItems],
  );
  const telegramSummary = statusQuery.data?.telegram ?? { groupChats: 0, userDmChats: 0 };

  useEffect(() => {
    if (typeof window === "undefined") {
      return;
    }

    window.localStorage.setItem(TELEGRAM_BOT_USERNAME_STORAGE_KEY, telegramBotUsername);
  }, [telegramBotUsername]);

  const invokeCandidates = useMemo(() => readInvokeCandidates(integrationItems), [integrationItems]);

  const effectiveIntegrationKey = useMemo(() => {
    if (!selectedIntegrationKey.trim()) {
      return invokeCandidates[0]?.integrationKey ?? "";
    }

    return invokeCandidates.some((item) => item.integrationKey === selectedIntegrationKey)
      ? selectedIntegrationKey
      : (invokeCandidates[0]?.integrationKey ?? "");
  }, [invokeCandidates, selectedIntegrationKey]);

  const availableTemplates = useMemo(
    () =>
      integrationActionTemplates.filter(
        (item) => item.integrationKey === effectiveIntegrationKey && item.scope === invokeScope,
      ),
    [effectiveIntegrationKey, invokeScope],
  );

  const effectiveTemplate = useMemo(() => {
    if (!availableTemplates.length) {
      return null;
    }

    return (
      availableTemplates.find((item) => item.id === selectedTemplateId)
      ?? availableTemplates[0]
    );
  }, [availableTemplates, selectedTemplateId]);

  const normalizedTelegramBotUsername = useMemo(
    () => normalizeTelegramBotUsername(telegramBotUsername),
    [telegramBotUsername],
  );

  const telegramLinkCommand = useMemo(
    () => (telegramLinkCode ? buildTelegramLinkCommand(telegramLinkCode.code) : ""),
    [telegramLinkCode],
  );

  const telegramDirectChatUrl = useMemo(
    () =>
      normalizedTelegramBotUsername
        ? `https://t.me/${encodeURIComponent(normalizedTelegramBotUsername)}`
        : "",
    [normalizedTelegramBotUsername],
  );

  const telegramDeepLinkUrl = useMemo(() => {
    if (!telegramLinkCode || !normalizedTelegramBotUsername) {
      return "";
    }

    const payload = buildTelegramStartPayload(telegramLinkCode.code);
    return `https://t.me/${encodeURIComponent(normalizedTelegramBotUsername)}?start=${encodeURIComponent(payload)}`;
  }, [normalizedTelegramBotUsername, telegramLinkCode]);

  const telegramGroupDeepLinkUrl = useMemo(() => {
    if (!telegramLinkCode || !normalizedTelegramBotUsername) {
      return "";
    }

    const payload = buildTelegramStartPayload(telegramLinkCode.code);
    return `https://t.me/${encodeURIComponent(normalizedTelegramBotUsername)}?startgroup=${encodeURIComponent(payload)}`;
  }, [normalizedTelegramBotUsername, telegramLinkCode]);

  const customHttpQuery = useQuery({
    queryKey: ["project-custom-http", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listProjectCustomHttpIntegrationsRequest(apiSession, projectId),
    enabled: canManageCustomHttp && customHttpGrantActive,
    staleTime: 10_000,
  });

  const customHttpItems = useMemo(
    () => customHttpQuery.data ?? [],
    [customHttpQuery.data],
  );

  useEffect(() => {
    if (!editingCustomHttpId) {
      return;
    }

    if (!customHttpItems.some((item) => item.id === editingCustomHttpId)) {
      setEditingCustomHttpId("");
    }
  }, [customHttpItems, editingCustomHttpId]);

  const runtimeMutation = useMutation({
    mutationFn: async (variables: {
      integrationKey: string;
      operation: "provision" | "deprovision" | "restart";
    }) => triggerProjectIntegrationRuntimeRequest(
      apiSession,
      projectId,
      variables.integrationKey,
      variables.operation,
    ),
    onSuccess: async (_data, variables) => {
      await queryClient.invalidateQueries({
        queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
      });
      setStatusMessage(`Операция runtime \`${variables.operation}\` поставлена в очередь.`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось выполнить runtime-операцию.");
    },
  });

  const steamInstanceCreateMutation = useMutation({
    mutationFn: async () => {
      const proxyHost = steamRuntimeProxyHost.trim();
      const proxyLogin = steamRuntimeProxyLogin.trim();
      const proxyPassword = steamRuntimeProxyPassword.trim();
      const proxyPort = Number(steamRuntimeProxyPort);
      const displayName = steamInstanceDisplayName.trim();

      if (!proxyHost) {
        throw new Error("Для Steam instance обязателен proxy host.");
      }

      if (!Number.isInteger(proxyPort) || proxyPort < 1 || proxyPort > 65535) {
        throw new Error("Proxy port должен быть числом в диапазоне 1..65535.");
      }

      if (!proxyLogin) {
        throw new Error("Для Steam instance обязателен proxy login.");
      }

      if (!proxyPassword) {
        throw new Error("Для Steam instance обязателен proxy password.");
      }

      return createProjectIntegrationInstanceRequest(apiSession, projectId, "steam-accounts-manager", {
        displayName: displayName || undefined,
        autoProvision: steamInstanceAutoProvision,
        makeDefault: steamInstanceMakeDefault,
        proxyConfig: {
          host: proxyHost,
          port: proxyPort,
          login: proxyLogin,
          password: proxyPassword,
        },
      });
    },
    onSuccess: async (instance) => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
        }),
        queryClient.invalidateQueries({
          queryKey: ["steam-instances", apiSession.baseUrl, apiSession.token, projectId],
        }),
        queryClient.invalidateQueries({
          queryKey: ["project-custom-http", apiSession.baseUrl, apiSession.token, projectId],
        }),
      ]);
      setStatusMessage("Steam instance создан.");
      router.push(`/projects/${projectId}/steam?instanceId=${instance.instanceId}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось создать Steam instance.");
    },
  });

  const steamInstanceRuntimeMutation = useMutation({
    mutationFn: async (variables: {
      instanceId: string;
      operation: "provision" | "deprovision" | "restart";
    }) =>
      triggerProjectIntegrationInstanceRuntimeRequest(
        apiSession,
        projectId,
        "steam-accounts-manager",
        variables.instanceId,
        variables.operation,
      ),
    onSuccess: async (_data, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
        }),
        queryClient.invalidateQueries({
          queryKey: ["steam-instances", apiSession.baseUrl, apiSession.token, projectId],
        }),
      ]);
      setStatusMessage(`Steam instance runtime \`${variables.operation}\` поставлен в очередь.`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось выполнить Steam instance runtime-операцию.");
    },
  });

  const steamInstanceDefaultMutation = useMutation({
    mutationFn: (instanceId: string) =>
      updateProjectIntegrationInstanceRequest(apiSession, projectId, "steam-accounts-manager", instanceId, {
        makeDefault: true,
      }),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
        }),
        queryClient.invalidateQueries({
          queryKey: ["steam-instances", apiSession.baseUrl, apiSession.token, projectId],
        }),
      ]);
      setStatusMessage("Steam instance назначен default.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось назначить default instance.");
    },
  });

  const steamInstanceDeleteMutation = useMutation({
    mutationFn: (instanceId: string) =>
      deleteProjectIntegrationInstanceRequest(apiSession, projectId, "steam-accounts-manager", instanceId),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
        }),
        queryClient.invalidateQueries({
          queryKey: ["steam-instances", apiSession.baseUrl, apiSession.token, projectId],
        }),
      ]);
      setStatusMessage("Steam instance удалён.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось удалить Steam instance.");
    },
  });

  const invokeMutation = useMutation({
    mutationFn: async () => {
      if (!effectiveIntegrationKey) {
        throw new Error("Нет доступных интеграций для invoke read/jobs.");
      }

      let payload: Record<string, unknown>;

      if (useAdvancedJson) {
        const trimmed = advancedPayloadText.trim();
        const parsed = trimmed ? JSON.parse(trimmed) : {};
        if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
          throw new Error("Advanced payload должен быть JSON-объектом.");
        }

        payload = parsed as Record<string, unknown>;
      } else {
        payload = invokeParams
          .filter((item) => item.key.trim().length > 0)
          .reduce<Record<string, unknown>>((acc, item) => {
            acc[item.key.trim()] = parseInvokeValue(item.value);
            return acc;
          }, {});
      }

      return invokeProjectIntegrationActionRequest(
        apiSession,
        projectId,
        effectiveIntegrationKey,
        invokeScope,
        payload,
      );
    },
    onSuccess: (result) => {
      setInvokeResult(JSON.stringify(result, null, 2));
      setStatusMessage("Integration action успешно выполнен.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось выполнить integration action.");
    },
  });

  const telegramCodeMutation = useMutation({
    mutationFn: (bindingType: "group" | "user") =>
      createTelegramLinkCodeRequest(apiSession, projectId, { bindingType }),
    onSuccess: (payload) => {
      setTelegramLinkCode(payload);
      setTelegramBindFlowStatus(
        `Код создан. Команда: ${buildTelegramLinkCommand(payload.code)}.`,
      );
      setStatusMessage("Telegram link code создан.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось создать Telegram link code.");
    },
  });

  const handleCopyTelegramCommand = async () => {
    if (!telegramLinkCommand) {
      return;
    }

    try {
      setCopyingTelegramCommand(true);
      await copyTextToClipboard(telegramLinkCommand);
      setTelegramBindFlowStatus("Команда скопирована. Отправьте её боту в нужном чате.");
    } catch (error) {
      setTelegramBindFlowStatus(error instanceof Error ? error.message : "Не удалось скопировать команду.");
    } finally {
      setCopyingTelegramCommand(false);
    }
  };

  const addInvokeParam = () => {
    const id =
      typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
        ? crypto.randomUUID()
        : `${Date.now()}-${invokeParams.length + 1}`;
    setInvokeParams((current) => [...current, { id, key: "", value: "" }]);
  };

  const updateInvokeParam = (id: string, patch: Partial<InvokeParamRow>) => {
    setInvokeParams((current) =>
      current.map((item) => (item.id === id ? { ...item, ...patch } : item)),
    );
  };

  const removeInvokeParam = (id: string) => {
    setInvokeParams((current) => {
      const next = current.filter((item) => item.id !== id);
      return next.length > 0 ? next : [{ id: "param-1", key: "", value: "" }];
    });
  };

  const resetCustomHttpForm = () => {
    setEditingCustomHttpId("");
    setCustomHttpName("");
    setCustomHttpBaseUrl("");
    setCustomHttpBearerToken("");
    setCustomHttpStatus("active");
    setCustomHttpHeadersJson("{}");
  };

  const startCustomHttpEdit = (item: ProjectCustomHttpIntegration) => {
    setEditingCustomHttpId(item.id);
    setCustomHttpName(item.name);
    setCustomHttpBaseUrl(item.baseUrl);
    setCustomHttpStatus(item.status);
    setCustomHttpBearerToken("");
    setCustomHttpHeadersJson("{}");
  };

  const createOrUpdateCustomHttpMutation = useMutation({
    mutationFn: async () => {
      if (!customHttpName.trim() || !customHttpBaseUrl.trim()) {
        throw new Error("Для custom HTTP обязательны name и baseUrl.");
      }

      const defaultHeaders = parseStringMapJson(customHttpHeadersJson, "Default headers");

      if (!editingCustomHttpId) {
        if (!customHttpBearerToken.trim()) {
          throw new Error("Для создания custom HTTP интеграции обязателен bearer token.");
        }

        return createProjectCustomHttpIntegrationRequest(apiSession, projectId, {
          name: customHttpName.trim(),
          baseUrl: customHttpBaseUrl.trim(),
          bearerToken: customHttpBearerToken.trim(),
          status: customHttpStatus,
          defaultHeaders,
        });
      }

      return updateProjectCustomHttpIntegrationRequest(apiSession, projectId, editingCustomHttpId, {
        name: customHttpName.trim(),
        baseUrl: customHttpBaseUrl.trim(),
        bearerToken: customHttpBearerToken.trim() || undefined,
        status: customHttpStatus,
        defaultHeaders,
      });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["project-custom-http", apiSession.baseUrl, apiSession.token, projectId],
      });
      resetCustomHttpForm();
      setStatusMessage("Custom HTTP интеграция сохранена.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось сохранить custom HTTP интеграцию.");
    },
  });

  const deleteCustomHttpMutation = useMutation({
    mutationFn: (integrationId: string) =>
      deleteProjectCustomHttpIntegrationRequest(apiSession, projectId, integrationId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["project-custom-http", apiSession.baseUrl, apiSession.token, projectId],
      });
      setStatusMessage("Custom HTTP интеграция удалена.");
      resetCustomHttpForm();
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось удалить custom HTTP интеграцию.");
    },
  });

  const testCustomHttpMutation = useMutation({
    mutationFn: async (integrationId: string) => {
      const headers = parseStringMapJson(customHttpTestHeadersJson, "Test headers");
      const payload = parseUnknownObjectJson(customHttpTestPayloadJson, "Test payload");
      return testProjectCustomHttpIntegrationRequest(apiSession, projectId, integrationId, {
        method: customHttpTestMethod.trim().toUpperCase(),
        path: customHttpTestPath.trim() || undefined,
        headers,
        payload,
      });
    },
    onSuccess: (result) => {
      setCustomHttpTestResult(JSON.stringify(result, null, 2));
      setStatusMessage("Тест custom HTTP выполнен.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось выполнить тест custom HTTP.");
    },
  });

  return (
    <div className="page-stack" data-testid="project-integrations-panel">
      <header className="page-section-header">
        <h2>Интеграции проекта</h2>
        <p>Steam работает как отдельный worker-runtime на проект, FunPayStat остается service-bus интеграцией.</p>
        <p className="route-hint">
          Для операционной работы со Steam (аккаунты, jobs, runtime) используйте отдельную вкладку{" "}
          <a href={`/projects/${projectId}/steam`}>Steam</a>.
        </p>
      </header>

      <div className="panel-actions">
        <button
          type="button"
          className="button button-ghost"
          disabled={statusQuery.isFetching}
          onClick={() => statusQuery.refetch()}
        >
          Обновить статус
        </button>
      </div>

      {statusQuery.isPending ? <p className="route-hint">Загружаем статус интеграций...</p> : null}
      {statusQuery.error ? (
        <p className="route-error">
          {statusQuery.error instanceof Error ? statusQuery.error.message : "Не удалось загрузить статус интеграций."}
        </p>
      ) : null}

      <section className="two-panel-layout">
        <section className="page-stack">
          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Steam instances</h3>
              <button
                type="button"
                className="button button-ghost"
                disabled={statusQuery.isFetching || steamInstancesQuery.isFetching}
                onClick={() => {
                  void Promise.all([statusQuery.refetch(), steamInstancesQuery.refetch()]);
                }}
              >
                Обновить
              </button>
            </div>
            {steamGrantItem ? (
              <div className="entity-pills">
                <span className="entity-pill">grant: {steamGrantItem.status}</span>
                <span className="entity-pill">runtime: {steamGrantItem.runtimeStatus ?? "n/a"}</span>
                <span className="entity-pill">scopes: {steamGrantItem.scopes.join(", ") || "n/a"}</span>
                <span className="entity-pill">instances: {steamInstances.length}/{steamMaxInstances}</span>
                {steamGrantItem.runtimeAccountId ? (
                  <span className="entity-pill">rk.{steamGrantItem.runtimeAccountId.replaceAll("-", "")}</span>
                ) : null}
              </div>
            ) : (
              <p className="route-hint">
                Steam grant ещё не выдан проекту. Сначала включите worker-интеграцию в админке.
              </p>
            )}
            <p className="route-hint">
              IMAP для авто-подтверждений задаётся на уровне Steam-аккаунта. Ниже создаётся только контейнер Steam instance.
            </p>
            {steamInstancesQuery.error ? (
              <p className="route-error">
                {steamInstancesQuery.error instanceof Error
                  ? steamInstancesQuery.error.message
                  : "Не удалось загрузить Steam instances."}
              </p>
            ) : null}
            {steamInstancesQuery.isPending ? <p className="route-hint">Загрузка Steam instances...</p> : null}
            {steamInstances.length > 0 ? (
              <ul className="entity-list">
                {steamInstances.map((instance) => (
                  <li key={instance.instanceId} className="entity-list-item">
                    <div>
                      <strong>{instance.displayName}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{instance.isDefault ? "default" : "instance"}</span>
                        <span className="entity-pill">runtime: {instance.runtimeStatus}</span>
                        <span className="entity-pill">rk.{instance.runtimeAccountId.replaceAll("-", "")}</span>
                        {instance.runtimeLastError ? <span className="entity-pill is-pill-danger">error</span> : null}
                      </div>
                      {instance.runtimeLastError ? <p className="route-error">{instance.runtimeLastError}</p> : null}
                    </div>
                    <div className="inline-actions">
                      <a className="button button-ghost" href={`/projects/${projectId}/steam?instanceId=${instance.instanceId}`}>
                        Открыть
                      </a>
                      {!instance.isDefault ? (
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={steamInstanceDefaultMutation.isPending}
                          onClick={() => steamInstanceDefaultMutation.mutate(instance.instanceId)}
                        >
                          Сделать default
                        </button>
                      ) : null}
                      <button
                        type="button"
                        className="button button-ghost"
                        disabled={steamInstanceRuntimeMutation.isPending}
                        onClick={() => steamInstanceRuntimeMutation.mutate({ instanceId: instance.instanceId, operation: "provision" })}
                      >
                        Provision
                      </button>
                      <button
                        type="button"
                        className="button button-ghost"
                        disabled={steamInstanceRuntimeMutation.isPending}
                        onClick={() => steamInstanceRuntimeMutation.mutate({ instanceId: instance.instanceId, operation: "restart" })}
                      >
                        Restart
                      </button>
                      <button
                        type="button"
                        className="button button-ghost"
                        disabled={steamInstanceRuntimeMutation.isPending}
                        onClick={() => steamInstanceRuntimeMutation.mutate({ instanceId: instance.instanceId, operation: "deprovision" })}
                      >
                        Deprovision
                      </button>
                      <button
                        type="button"
                        className="button button-ghost"
                        disabled={steamInstanceDeleteMutation.isPending}
                        onClick={() => steamInstanceDeleteMutation.mutate(instance.instanceId)}
                      >
                        Delete
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            ) : steamInstancesQuery.isPending ? null : (
              <p className="route-hint">Steam instances пока нет. Создайте первый контейнер ниже.</p>
            )}
            <div className="grid-2">
              <label className="field">
                <span>Display name</span>
                <input
                  className="input"
                  value={steamInstanceDisplayName}
                  onChange={(event) => setSteamInstanceDisplayName(event.target.value)}
                  placeholder="steam-instance-1"
                />
              </label>
              <label className="field">
                <span>Proxy host *</span>
                <input className="input" value={steamRuntimeProxyHost} onChange={(event) => setSteamRuntimeProxyHost(event.target.value)} placeholder="127.0.0.1" />
              </label>
              <label className="field">
                <span>Proxy port *</span>
                <input className="input" value={steamRuntimeProxyPort} onChange={(event) => setSteamRuntimeProxyPort(event.target.value)} placeholder="8080" />
              </label>
              <label className="field">
                <span>Proxy login *</span>
                <input className="input" value={steamRuntimeProxyLogin} onChange={(event) => setSteamRuntimeProxyLogin(event.target.value)} placeholder="integration-runtime" />
              </label>
              <label className="field">
                <span>Proxy password *</span>
                <input className="input" type="password" value={steamRuntimeProxyPassword} onChange={(event) => setSteamRuntimeProxyPassword(event.target.value)} />
              </label>
            </div>
            <div className="grid-2">
              <label className="field field-inline">
                <span>Auto provision</span>
                <input
                  type="checkbox"
                  checked={steamInstanceAutoProvision}
                  onChange={(event) => setSteamInstanceAutoProvision(event.target.checked)}
                />
              </label>
              <label className="field field-inline">
                <span>Make default</span>
                <input
                  type="checkbox"
                  checked={steamInstanceMakeDefault}
                  onChange={(event) => setSteamInstanceMakeDefault(event.target.checked)}
                />
              </label>
            </div>
            <div className="hero-actions">
              <button
                type="button"
                className="button button-primary"
                disabled={steamInstanceCreateMutation.isPending || steamGrantItem?.status !== "active"}
                onClick={() => steamInstanceCreateMutation.mutate()}
              >
                Создать Steam instance
              </button>
            </div>
          </article>

          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Доступы и runtime</h3>
            </div>
            {integrationItems.length === 0 ? (
              <p className="route-hint">Интеграции пока не выданы проекту.</p>
            ) : (
              <ul className="entity-list">
                {integrationItems.map((item) => (
                  <li key={item.integrationKey} className="entity-list-item">
                    <div>
                      <strong>{item.integrationKey}</strong>
                      <div className="entity-pills">
                        <span className="entity-pill">{item.integrationType}</span>
                        <span className="entity-pill">{item.status}</span>
                        <span className="entity-pill">scopes: {item.scopes.join(", ") || "n/a"}</span>
                        <span className="entity-pill">credential: {item.credentialStatus ?? "n/a"}</span>
                        {item.runtimeStatus ? <span className="entity-pill">runtime: {item.runtimeStatus}</span> : null}
                        {item.runtimeAccountId ? <span className="entity-pill">rk: rk.{item.runtimeAccountId.replaceAll("-", "")}</span> : null}
                      </div>
                      {item.runtimeLastError ? <p className="route-error">Runtime error: {item.runtimeLastError}</p> : null}
                    </div>
                    {item.integrationType === "worker" ? (
                      <div className="inline-actions">
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={runtimeMutation.isPending}
                          onClick={() =>
                            runtimeMutation.mutate({ integrationKey: item.integrationKey, operation: "provision" })
                          }
                        >
                          Provision
                        </button>
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={runtimeMutation.isPending}
                          onClick={() =>
                            runtimeMutation.mutate({ integrationKey: item.integrationKey, operation: "restart" })
                          }
                        >
                          Restart
                        </button>
                        <button
                          type="button"
                          className="button button-ghost"
                          disabled={runtimeMutation.isPending}
                          onClick={() =>
                            runtimeMutation.mutate({ integrationKey: item.integrationKey, operation: "deprovision" })
                          }
                        >
                          Deprovision
                        </button>
                      </div>
                    ) : null}
                  </li>
                ))}
              </ul>
            )}
          </article>

          <article className="glass-card page-stack">
            <div className="panel-title-row">
              <h3>Telegram bind flow</h3>
            </div>
            <div className="entity-pills">
              <span className="entity-pill">group chats: {telegramSummary.groupChats}</span>
              <span className="entity-pill">user DM chats: {telegramSummary.userDmChats}</span>
            </div>
            <label className="field">
              <span>Username бота (@username)</span>
              <input
                className="input"
                value={telegramBotUsername}
                onChange={(event) => setTelegramBotUsername(event.target.value)}
                placeholder="DigitalDealsCRMBot"
              />
            </label>
            <p className="route-hint">
              Укажите username без `@`, чтобы кнопка открытия Telegram работала сразу.
            </p>
            <div className="inline-actions">
              <button
                type="button"
                className="button button-ghost"
                disabled={telegramCodeMutation.isPending}
                onClick={() => telegramCodeMutation.mutate("group")}
              >
                Код для group-чата
              </button>
              <button
                type="button"
                className="button button-ghost"
                disabled={telegramCodeMutation.isPending}
                onClick={() => telegramCodeMutation.mutate("user")}
              >
                Код для ЛС
              </button>
            </div>
            {telegramLinkCode ? (
              <div className="status-block status-info">
                <h4>Новый link code</h4>
                <p>
                  <strong>{telegramLinkCode.code}</strong> · type: {telegramLinkCode.bindingType}
                </p>
                <label className="field">
                  <span>Команда для привязки</span>
                  <div className="inline-actions telegram-command-row">
                    <input className="input" value={telegramLinkCommand} readOnly />
                    <button
                      type="button"
                      className="button button-primary"
                      disabled={copyingTelegramCommand}
                      onClick={() => {
                        void handleCopyTelegramCommand();
                      }}
                    >
                      Копировать
                    </button>
                  </div>
                </label>
                <div className="inline-actions">
                  {telegramLinkCode.bindingType === "group" ? (
                    <a
                      className="button button-primary"
                      href={telegramGroupDeepLinkUrl || "#"}
                      target="_blank"
                      rel="noreferrer"
                      aria-disabled={!telegramGroupDeepLinkUrl}
                    >
                      Открыть TG (группа)
                    </a>
                  ) : (
                    <a
                      className="button button-primary"
                      href={telegramDeepLinkUrl || "#"}
                      target="_blank"
                      rel="noreferrer"
                      aria-disabled={!telegramDeepLinkUrl}
                    >
                      Открыть TG (ЛС)
                    </a>
                  )}
                  <a
                    className="button button-ghost"
                    href={telegramDirectChatUrl || "#"}
                    target="_blank"
                    rel="noreferrer"
                    aria-disabled={!telegramDirectChatUrl}
                  >
                    Открыть чат с ботом
                  </a>
                </div>
                <p className="route-hint">Действителен до: {new Date(telegramLinkCode.expiresAtUtc).toLocaleString()}</p>
                <p className="route-hint">
                  Если deep-link не сработал, отправьте вручную: <strong>{telegramLinkCommand}</strong>
                </p>
                {telegramBindFlowStatus ? <p className="route-hint">{telegramBindFlowStatus}</p> : null}
              </div>
            ) : (
              <p className="route-hint">Сгенерируйте code и подтвердите его через Telegram-бота.</p>
            )}
          </article>
        </section>

        <aside className="sticky-side">
          <article className="glass-card page-stack panel-card-sticky">
            <div className="panel-title-row">
              <h3>Invoke read/jobs</h3>
            </div>
            <p className="route-hint">
              Здесь оставлен только service invoke (FunPayStat). Steam invoke перенесён во вкладку Steam.
            </p>

            <label className="field">
              <span>Integration</span>
              <select
                className="input"
                value={effectiveIntegrationKey}
                onChange={(event) => setSelectedIntegrationKey(event.target.value)}
                disabled={invokeCandidates.length === 0}
              >
                {invokeCandidates.length === 0 ? (
                  <option value="">Нет доступных интеграций</option>
                ) : (
                  invokeCandidates.map((item) => (
                    <option key={item.integrationKey} value={item.integrationKey}>
                      {item.integrationKey} ({item.integrationType})
                    </option>
                  ))
                )}
              </select>
            </label>

            <label className="field">
              <span>Scope</span>
              <select
                className="input"
                value={invokeScope}
                onChange={(event) => setInvokeScope(event.target.value as "read" | "jobs")}
              >
                <option value="read">read</option>
                <option value="jobs">jobs</option>
              </select>
            </label>

            {effectiveTemplate ? (
              <>
                <label className="field">
                  <span>Шаблон сценария</span>
                  <select
                    className="input"
                    value={effectiveTemplate.id}
                    onChange={(event) => setSelectedTemplateId(event.target.value)}
                  >
                    {availableTemplates.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.title}
                      </option>
                    ))}
                  </select>
                </label>
                <p className="route-hint">{effectiveTemplate.description}</p>
                <button
                  type="button"
                  className="button button-ghost"
                  onClick={() => {
                    setUseAdvancedJson(false);
                    setInvokeParams(buildParamRows(effectiveTemplate.fields));
                    setStatusMessage(`Применён шаблон: ${effectiveTemplate.title}.`);
                  }}
                >
                  Заполнить форму из шаблона
                </button>
              </>
            ) : (
              <p className="route-hint">
                Для выбранной интеграции пока нет готовых шаблонов. Можно использовать ручную форму параметров.
              </p>
            )}

            <div className="panel-title-row">
              <h4>Параметры action</h4>
            </div>
            {!useAdvancedJson ? (
              <div className="page-stack">
                {invokeParams.map((item) => (
                  <div key={item.id} className="grid-2">
                    <label className="field">
                      <span>Ключ</span>
                      <input
                        className="input"
                        value={item.key}
                        onChange={(event) => updateInvokeParam(item.id, { key: event.target.value })}
                        placeholder="например: limit"
                      />
                    </label>
                    <label className="field">
                      <span>Значение</span>
                      <div className="inline-actions">
                        <input
                          className="input"
                          value={item.value}
                          onChange={(event) => updateInvokeParam(item.id, { value: event.target.value })}
                          placeholder="например: 50 / true / text"
                        />
                        <button
                          type="button"
                          className="button button-ghost"
                          onClick={() => removeInvokeParam(item.id)}
                        >
                          Удалить
                        </button>
                      </div>
                    </label>
                  </div>
                ))}
                <div className="inline-actions">
                  <button type="button" className="button button-ghost" onClick={addInvokeParam}>
                    Добавить параметр
                  </button>
                </div>
                <p className="route-hint">
                  Формат значения: число/`true`/`false` распознаются автоматически; объекты и массивы можно передать как
                  JSON в поле значения.
                </p>
              </div>
            ) : (
              <label className="field">
                <span>Advanced payload (JSON object)</span>
                <textarea
                  className="input"
                  rows={8}
                  value={advancedPayloadText}
                  onChange={(event) => setAdvancedPayloadText(event.target.value)}
                />
              </label>
            )}

            <label className="field field-inline">
              <span>Advanced JSON</span>
              <input
                type="checkbox"
                checked={useAdvancedJson}
                onChange={(event) => setUseAdvancedJson(event.target.checked)}
              />
            </label>

            <button
              type="button"
              className="button button-primary"
              disabled={invokeMutation.isPending || !effectiveIntegrationKey}
              onClick={() => invokeMutation.mutate()}
            >
              Выполнить action
            </button>

            {invokeResult ? (
              <label className="field">
                <span>Ответ</span>
                <textarea className="input" rows={12} value={invokeResult} readOnly />
              </label>
            ) : (
              <p className="route-hint">Результат вызова появится здесь.</p>
            )}
          </article>
        </aside>
      </section>

      <section className="panel-card page-stack">
        <div className="panel-title-row">
          <h3>Custom HTTP integrations</h3>
          <div className="inline-actions">
            <button
              type="button"
              className="button button-ghost"
              disabled={customHttpQuery.isFetching || !canManageCustomHttp || !customHttpGrantActive}
              onClick={() => customHttpQuery.refetch()}
            >
              Обновить
            </button>
            <button type="button" className="button button-ghost" onClick={resetCustomHttpForm}>
              Очистить форму
            </button>
          </div>
        </div>

        {!canManageCustomHttp ? (
          <p className="route-hint">Недостаточно прав: требуется permission `project.integrations.custom.manage`.</p>
        ) : null}
        {canManageCustomHttp && !customHttpGrantActive ? (
          <p className="route-hint">
            Для проекта не активирован grant `custom-http` со scope `use`.
          </p>
        ) : null}
        {canManageCustomHttp && customHttpGrantActive && customHttpQuery.isPending ? (
          <p className="route-hint">Загружаем custom HTTP интеграции...</p>
        ) : null}
        {canManageCustomHttp && customHttpGrantActive && customHttpQuery.error ? (
          <p className="route-error">
            {customHttpQuery.error instanceof Error
              ? customHttpQuery.error.message
              : "Не удалось загрузить custom HTTP интеграции."}
          </p>
        ) : null}

        {canManageCustomHttp && customHttpGrantActive ? (
          <>
            <div className="grid-2">
              <label className="field">
                <span>Name</span>
                <input
                  className="input"
                  value={customHttpName}
                  onChange={(event) => setCustomHttpName(event.target.value)}
                  placeholder="Например: My Fulfillment Bot"
                />
              </label>
              <label className="field">
                <span>Base URL (HTTPS)</span>
                <input
                  className="input"
                  value={customHttpBaseUrl}
                  onChange={(event) => setCustomHttpBaseUrl(event.target.value)}
                  placeholder="https://api.example.com"
                />
              </label>
            </div>
            <div className="grid-2">
              <label className="field">
                <span>Bearer token {editingCustomHttpId ? "(optional update)" : "(required)"}</span>
                <input
                  className="input"
                  value={customHttpBearerToken}
                  onChange={(event) => setCustomHttpBearerToken(event.target.value)}
                  placeholder="token"
                />
              </label>
              <label className="field">
                <span>Status</span>
                <select
                  className="input"
                  value={customHttpStatus}
                  onChange={(event) => setCustomHttpStatus(event.target.value as "active" | "disabled")}
                >
                  <option value="active">active</option>
                  <option value="disabled">disabled</option>
                </select>
              </label>
            </div>
            <label className="field">
              <span>Default headers (JSON object)</span>
              <textarea
                className="input"
                rows={5}
                value={customHttpHeadersJson}
                onChange={(event) => setCustomHttpHeadersJson(event.target.value)}
              />
            </label>
            <div className="panel-actions">
              <button
                type="button"
                className="button button-primary"
                disabled={createOrUpdateCustomHttpMutation.isPending}
                onClick={() => createOrUpdateCustomHttpMutation.mutate()}
              >
                {editingCustomHttpId ? "Сохранить изменения" : "Создать интеграцию"}
              </button>
            </div>

            {customHttpItems.length === 0 ? (
              <p className="route-hint">Пока нет custom HTTP интеграций.</p>
            ) : (
              <ul className="entity-list">
                {customHttpItems.map((item) => (
                  <li key={item.id} className="entity-list-item">
                    <div>
                      <strong>{item.name}</strong>
                      <p className="route-hint">{item.baseUrl}</p>
                      <div className="entity-pills">
                        <span className="entity-pill">{item.status}</span>
                        <span className="entity-pill">token: {item.bearerTokenMasked}</span>
                        <span className="entity-pill">
                          tested: {item.lastTestedAtUtc ? new Date(item.lastTestedAtUtc).toLocaleString() : "-"}
                        </span>
                      </div>
                    </div>
                    <div className="inline-actions">
                      <button
                        type="button"
                        className="button button-ghost"
                        onClick={() => startCustomHttpEdit(item)}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="button button-ghost"
                        disabled={testCustomHttpMutation.isPending}
                        onClick={() => testCustomHttpMutation.mutate(item.id)}
                      >
                        Test
                      </button>
                      <button
                        type="button"
                        className="button button-ghost"
                        disabled={deleteCustomHttpMutation.isPending}
                        onClick={() => deleteCustomHttpMutation.mutate(item.id)}
                      >
                        Delete
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            )}

            <div className="stacked-block">
              <h4>Test call payload</h4>
              <div className="grid-2">
                <label className="field">
                  <span>Method</span>
                  <input
                    className="input"
                    value={customHttpTestMethod}
                    onChange={(event) => setCustomHttpTestMethod(event.target.value)}
                    placeholder="POST"
                  />
                </label>
                <label className="field">
                  <span>Path (optional)</span>
                  <input
                    className="input"
                    value={customHttpTestPath}
                    onChange={(event) => setCustomHttpTestPath(event.target.value)}
                    placeholder="/health"
                  />
                </label>
              </div>
              <label className="field">
                <span>Headers JSON</span>
                <textarea
                  className="input"
                  rows={4}
                  value={customHttpTestHeadersJson}
                  onChange={(event) => setCustomHttpTestHeadersJson(event.target.value)}
                />
              </label>
              <label className="field">
                <span>Payload JSON</span>
                <textarea
                  className="input"
                  rows={5}
                  value={customHttpTestPayloadJson}
                  onChange={(event) => setCustomHttpTestPayloadJson(event.target.value)}
                />
              </label>
              {customHttpTestResult ? (
                <label className="field">
                  <span>Последний test result</span>
                  <textarea className="input" rows={8} value={customHttpTestResult} readOnly />
                </label>
              ) : null}
            </div>
          </>
        ) : null}
      </section>

      <p className="route-hint">{statusMessage}</p>
    </div>
  );
}

