"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import type { AccountCreateRequest, ProxyConfig } from "@/generated/external-api";
import type { ApiSession } from "@/lib/api-client";
import {
  addProjectMemberRequest,
  buildRouteKey,
  changeProjectMemberRoleRequest,
  changePlanRequest,
  createAccountRequest,
  createPaymentRequest,
  createProjectRequest,
  getMaskedProxyCredentialsRequest,
  listAccountsRequest,
  listProjectsRequest,
  proxyAccountActionRequest,
  purchaseAddonRequest,
  revealProxyCredentialsRequest,
  removeProjectMemberRequest,
  updateProxyCredentialsRequest,
} from "@/lib/api-client";
import {
  readJwtInfo,
  updateSessionRole,
  type PlatformSession,
} from "@/lib/auth";
import {
  hasPermission,
  projectPermissions,
  projectRoles,
  type ProjectRole,
} from "@/lib/rbac";

interface PlatformConsoleProps {
  session: PlatformSession;
  onSessionChange: (session: PlatformSession) => void;
  onLogout: () => void;
}

type MainSection = "projects" | "profile" | "activity";

type ProjectTab =
  | "overview"
  | "members"
  | "accounts"
  | "products"
  | "messages"
  | "orders"
  | "proxy"
  | "billing"
  | "gateway";

type ModuleResultKey = "products" | "messages" | "orders";

const mainSectionLabels: Record<MainSection, string> = {
  projects: "Проекты и аккаунты",
  profile: "Личный Кабинет",
  activity: "Журнал Активности",
};

const projectTabLabels: Record<ProjectTab, string> = {
  overview: "Обзор",
  members: "Участники",
  accounts: "Аккаунты",
  products: "Товары",
  messages: "Сообщения",
  orders: "Схемы",
  proxy: "Proxy",
  billing: "Billing",
  gateway: "Gateway",
};

const DEFAULT_PRODUCTS_PAYLOAD = '{"status":"active","limit":20}';
const DEFAULT_MESSAGES_PAYLOAD =
  '{"conversationId":"conv-100","text":"Тестовое сообщение","limit":20,"onlyUnread":false}';
const DEFAULT_ORDERS_PAYLOAD = '{"schemaId":"digital_goods.v1"}';
const PROJECT_MODULE_STATE_STORAGE_KEY = "ddcrm-platform.project-modules";
const OPENED_PROJECT_STORAGE_KEY = "ddcrm-platform.opened-project-id";
const PROJECT_ACTIVITY_STORAGE_KEY = "ddcrm-platform.project-activity";
const PROJECT_ACTIVITY_MAX_ITEMS = 30;

interface StoredProjectModuleState {
  productsPayload: string;
  messagesPayload: string;
  ordersPayload: string;
}

type StoredProjectModuleStateMap = Record<string, StoredProjectModuleState>;
type StoredProjectActivityMap = Record<string, string[]>;

interface ParsedBulkUserIds {
  validUserIds: string[];
  invalidEntries: string[];
  duplicateUserIds: string[];
}

interface BulkMemberActionFailure {
  userId: string;
  reason: string;
}

interface BulkMemberActionResult {
  attempted: number;
  succeeded: string[];
  failed: BulkMemberActionFailure[];
  invalidEntries: string[];
  duplicateUserIds: string[];
}

function createDefaultProjectModuleState(): StoredProjectModuleState {
  return {
    productsPayload: DEFAULT_PRODUCTS_PAYLOAD,
    messagesPayload: DEFAULT_MESSAGES_PAYLOAD,
    ordersPayload: DEFAULT_ORDERS_PAYLOAD,
  };
}

function readStoredProjectModuleStateMap(): StoredProjectModuleStateMap {
  if (typeof window === "undefined") {
    return {};
  }

  const raw = localStorage.getItem(PROJECT_MODULE_STATE_STORAGE_KEY);
  if (!raw) {
    return {};
  }

  try {
    const parsed = JSON.parse(raw) as StoredProjectModuleStateMap;
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      return {};
    }

    return parsed;
  } catch {
    return {};
  }
}

function readStoredProjectModuleState(projectId: string): StoredProjectModuleState {
  const map = readStoredProjectModuleStateMap();
  const existing = map[projectId];
  if (!existing) {
    return createDefaultProjectModuleState();
  }

  return {
    productsPayload:
      typeof existing.productsPayload === "string"
        ? existing.productsPayload
        : DEFAULT_PRODUCTS_PAYLOAD,
    messagesPayload:
      typeof existing.messagesPayload === "string"
        ? existing.messagesPayload
        : DEFAULT_MESSAGES_PAYLOAD,
    ordersPayload:
      typeof existing.ordersPayload === "string"
        ? existing.ordersPayload
        : DEFAULT_ORDERS_PAYLOAD,
  };
}

function writeStoredProjectModuleState(
  projectId: string,
  value: StoredProjectModuleState,
) {
  if (typeof window === "undefined") {
    return;
  }

  const map = readStoredProjectModuleStateMap();
  map[projectId] = value;
  localStorage.setItem(PROJECT_MODULE_STATE_STORAGE_KEY, JSON.stringify(map));
}

function readStoredProjectActivityMap(): StoredProjectActivityMap {
  if (typeof window === "undefined") {
    return {};
  }

  const raw = localStorage.getItem(PROJECT_ACTIVITY_STORAGE_KEY);
  if (!raw) {
    return {};
  }

  try {
    const parsed = JSON.parse(raw) as StoredProjectActivityMap;
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      return {};
    }

    const normalized: StoredProjectActivityMap = {};
    for (const [projectId, entries] of Object.entries(parsed)) {
      if (!Array.isArray(entries)) {
        continue;
      }

      normalized[projectId] = entries
        .filter((entry): entry is string => typeof entry === "string")
        .slice(0, PROJECT_ACTIVITY_MAX_ITEMS);
    }

    return normalized;
  } catch {
    return {};
  }
}

function writeStoredProjectActivityMap(value: StoredProjectActivityMap) {
  if (typeof window === "undefined") {
    return;
  }

  localStorage.setItem(PROJECT_ACTIVITY_STORAGE_KEY, JSON.stringify(value));
}

function readStoredOpenedProjectId(): string {
  if (typeof window === "undefined") {
    return "";
  }

  const raw = localStorage.getItem(OPENED_PROJECT_STORAGE_KEY);
  if (!raw || typeof raw !== "string") {
    return "";
  }

  return raw.trim();
}

function writeStoredOpenedProjectId(projectId: string) {
  if (typeof window === "undefined") {
    return;
  }

  if (!projectId.trim()) {
    localStorage.removeItem(OPENED_PROJECT_STORAGE_KEY);
    return;
  }

  localStorage.setItem(OPENED_PROJECT_STORAGE_KEY, projectId.trim());
}

function parseJsonInput(value: string) {
  if (!value.trim()) {
    return undefined;
  }

  const parsed = JSON.parse(value) as unknown;
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    throw new Error("Payload должен быть JSON-объектом.");
  }

  return parsed as Record<string, unknown>;
}

function normalizeObjectResult(result: unknown): Record<string, unknown> {
  if (typeof result === "object" && result !== null && !Array.isArray(result)) {
    return result as Record<string, unknown>;
  }

  return {
    value: result,
  };
}

function isGuid(value: string) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(
    value,
  );
}

function extractResultRows(result: Record<string, unknown>): Record<string, unknown>[] {
  const arrayKeys = ["items", "listings", "orders", "messages", "rows", "results"];

  for (const key of arrayKeys) {
    const candidate = result[key];
    if (Array.isArray(candidate) && candidate.length > 0) {
      const objectRows = candidate.filter(
        (item): item is Record<string, unknown> =>
          typeof item === "object" && item !== null && !Array.isArray(item),
      );
      if (objectRows.length > 0) {
        return objectRows;
      }
    }
  }

  return [result];
}

function collectResultColumns(rows: Record<string, unknown>[]): string[] {
  const columns = new Set<string>();
  for (const row of rows) {
    for (const key of Object.keys(row)) {
      columns.add(key);
      if (columns.size >= 12) {
        break;
      }
    }
    if (columns.size >= 12) {
      break;
    }
  }

  return [...columns];
}

function formatResultCell(value: unknown): string {
  if (value === null || value === undefined) {
    return "";
  }

  if (typeof value === "string") {
    return value;
  }

  if (typeof value === "number" || typeof value === "boolean") {
    return String(value);
  }

  const serialized = JSON.stringify(value);
  if (!serialized) {
    return "";
  }

  if (serialized.length <= 120) {
    return serialized;
  }

  return `${serialized.slice(0, 117)}...`;
}

function includesFilterTerm(value: unknown, filterTerm: string): boolean {
  if (!filterTerm) {
    return true;
  }

  if (value === null || value === undefined) {
    return false;
  }

  return String(value).toLowerCase().includes(filterTerm);
}

function toStableTestId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9-]+/g, "-");
  return normalized.replace(/^-+|-+$/g, "");
}

function parseBulkUserIds(value: string): ParsedBulkUserIds {
  const tokens = value
    .split(/[\s,;]+/)
    .map((token) => token.trim())
    .filter((token) => token.length > 0);

  const validUserIds: string[] = [];
  const invalidEntries: string[] = [];
  const duplicateUserIds: string[] = [];
  const seen = new Set<string>();

  for (const token of tokens) {
    if (!isGuid(token)) {
      invalidEntries.push(token);
      continue;
    }

    const normalized = token.toLowerCase();
    if (seen.has(normalized)) {
      duplicateUserIds.push(token);
      continue;
    }

    seen.add(normalized);
    validUserIds.push(token);
  }

  return {
    validUserIds,
    invalidEntries,
    duplicateUserIds,
  };
}

async function runBulkMemberAction(
  rawInput: string,
  execute: (userId: string) => Promise<void>,
): Promise<BulkMemberActionResult> {
  const parsed = parseBulkUserIds(rawInput);
  if (parsed.validUserIds.length === 0) {
    throw new Error(
      "В bulk-списке не найдено ни одного валидного GUID. Добавьте хотя бы один userId.",
    );
  }

  const succeeded: string[] = [];
  const failed: BulkMemberActionFailure[] = [];

  for (const userId of parsed.validUserIds) {
    try {
      await execute(userId);
      succeeded.push(userId);
    } catch (error) {
      failed.push({
        userId,
        reason: error instanceof Error ? error.message : "Неизвестная ошибка",
      });
    }
  }

  return {
    attempted: parsed.validUserIds.length,
    succeeded,
    failed,
    invalidEntries: parsed.invalidEntries,
    duplicateUserIds: parsed.duplicateUserIds,
  };
}

function formatBulkResultStatus(actionLabel: string, result: BulkMemberActionResult): string {
  const stats: string[] = [
    `${actionLabel}: success ${result.succeeded.length}/${result.attempted}`,
  ];

  if (result.failed.length > 0) {
    stats.push(`ошибки ${result.failed.length}`);
  }

  if (result.invalidEntries.length > 0) {
    stats.push(`невалидные ${result.invalidEntries.length}`);
  }

  if (result.duplicateUserIds.length > 0) {
    stats.push(`дубликаты ${result.duplicateUserIds.length}`);
  }

  return stats.join(" · ");
}

export function PlatformConsole({
  session,
  onSessionChange,
  onLogout,
}: PlatformConsoleProps) {
  const queryClient = useQueryClient();
  const apiSession = useMemo<ApiSession>(
    () => ({
      token: session.token,
      baseUrl: session.baseUrl,
    }),
    [session.baseUrl, session.token],
  );

  const [activeSection, setActiveSection] = useState<MainSection>("projects");
  const [statusMessage, setStatusMessage] = useState("Платформа готова к работе.");
  const [activityLog, setActivityLog] = useState<string[]>([]);
  const [projectActivityMap, setProjectActivityMap] = useState<StoredProjectActivityMap>(
    () => readStoredProjectActivityMap(),
  );

  const [openedProjectId, setOpenedProjectId] = useState(() =>
    readStoredOpenedProjectId(),
  );
  const [projectFilterInput, setProjectFilterInput] = useState("");
  const [activeProjectTab, setActiveProjectTab] = useState<ProjectTab>("accounts");
  const [selectedAccountId, setSelectedAccountId] = useState("");
  const [accountFilterInput, setAccountFilterInput] = useState("");
  const [projectNameInput, setProjectNameInput] = useState("Новый проект DDCRM");
  const [memberUserIdInput, setMemberUserIdInput] = useState("");
  const [memberRoleInput, setMemberRoleInput] = useState<Exclude<ProjectRole, "owner">>(
    "admin",
  );
  const [memberChangeUserIdInput, setMemberChangeUserIdInput] = useState("");
  const [memberChangeRoleInput, setMemberChangeRoleInput] = useState<ProjectRole>("admin");
  const [memberRemoveUserIdInput, setMemberRemoveUserIdInput] = useState("");
  const [bulkInviteUserIdsInput, setBulkInviteUserIdsInput] = useState("");
  const [bulkInviteRoleInput, setBulkInviteRoleInput] = useState<
    Exclude<ProjectRole, "owner">
  >("moderator");
  const [bulkRoleChangeUserIdsInput, setBulkRoleChangeUserIdsInput] = useState("");
  const [bulkRoleChangeRoleInput, setBulkRoleChangeRoleInput] =
    useState<ProjectRole>("moderator");
  const [bulkRemoveUserIdsInput, setBulkRemoveUserIdsInput] = useState("");
  const [memberBulkResult, setMemberBulkResult] = useState<{
    actionLabel: string;
    result: BulkMemberActionResult;
  } | null>(null);
  const [memberActivityLog, setMemberActivityLog] = useState<string[]>([]);

  const [createPlatformInput, setCreatePlatformInput] = useState("funpay");
  const [createDisplayNameInput, setCreateDisplayNameInput] = useState(
    "FunPay Test Account",
  );
  const [createProxyHostInput, setCreateProxyHostInput] = useState("");
  const [createProxyPortInput, setCreateProxyPortInput] = useState("1508");
  const [createProxyLoginInput, setCreateProxyLoginInput] = useState("");
  const [createProxyPasswordInput, setCreateProxyPasswordInput] = useState("");

  const [updateReasonInput, setUpdateReasonInput] = useState("UI proxy update");
  const [updateProxyHostInput, setUpdateProxyHostInput] = useState("");
  const [updateProxyPortInput, setUpdateProxyPortInput] = useState("1508");
  const [updateProxyLoginInput, setUpdateProxyLoginInput] = useState("");
  const [updateProxyPasswordInput, setUpdateProxyPasswordInput] = useState("");

  const [revealReasonInput, setRevealReasonInput] = useState("UI proxy reveal");
  const [revealedProxyConfig, setRevealedProxyConfig] = useState<ProxyConfig | null>(
    null,
  );

  const [productsPayloadInput, setProductsPayloadInput] = useState(() => {
    const projectId = readStoredOpenedProjectId();
    if (!projectId) {
      return DEFAULT_PRODUCTS_PAYLOAD;
    }

    return readStoredProjectModuleState(projectId).productsPayload;
  });
  const [messagesPayloadInput, setMessagesPayloadInput] = useState(() => {
    const projectId = readStoredOpenedProjectId();
    if (!projectId) {
      return DEFAULT_MESSAGES_PAYLOAD;
    }

    return readStoredProjectModuleState(projectId).messagesPayload;
  });
  const [ordersPayloadInput, setOrdersPayloadInput] = useState(() => {
    const projectId = readStoredOpenedProjectId();
    if (!projectId) {
      return DEFAULT_ORDERS_PAYLOAD;
    }

    return readStoredProjectModuleState(projectId).ordersPayload;
  });
  const [moduleResults, setModuleResults] = useState<
    Record<ModuleResultKey, Record<string, unknown> | null>
  >({
    products: null,
    messages: null,
    orders: null,
  });

  const [paymentAmountInput, setPaymentAmountInput] = useState("99.99");
  const [paymentCurrencyInput, setPaymentCurrencyInput] = useState("USD");
  const [planKeyInput, setPlanKeyInput] = useState("pro");
  const [addonIdInput, setAddonIdInput] = useState("extra-workers");
  const [billingResult, setBillingResult] = useState<Record<string, unknown> | null>(
    null,
  );

  const [gatewayRouteKeyInput, setGatewayRouteKeyInput] = useState("");
  const [gatewayActionInput, setGatewayActionInput] = useState("ext.market.sync");
  const [gatewayPayloadInput, setGatewayPayloadInput] = useState(
    '{"scope":"inventory"}',
  );
  const [gatewayResult, setGatewayResult] = useState<Record<string, unknown> | null>(
    null,
  );

  const [roleDraft, setRoleDraft] = useState<ProjectRole>(session.profile.role);

  const activeRole = session.profile.role;
  const jwtInfo = useMemo(() => readJwtInfo(session.token), [session.token]);

  const canManageAccountLifecycle = hasPermission(
    activeRole,
    projectPermissions.accountsLifecycleManage,
  );
  const canRevealProxy = hasPermission(
    activeRole,
    projectPermissions.proxyCredentialsReveal,
  );
  const canUpdateProxy = hasPermission(
    activeRole,
    projectPermissions.proxyCredentialsUpdate,
  );
  const canViewBilling = hasPermission(activeRole, projectPermissions.billingView);
  const canChangeBilling = hasPermission(
    activeRole,
    projectPermissions.billingChangePlan,
  );
  const canUseGateway = hasPermission(activeRole, projectPermissions.workersOperate);
  const canOperateModules = hasPermission(
    activeRole,
    projectPermissions.modulesOperate,
  );
  const canInviteMembers = hasPermission(
    activeRole,
    projectPermissions.membersInvite,
  );
  const canChangeMemberRoles = hasPermission(
    activeRole,
    projectPermissions.rolesChange,
  );
  const canRemoveMembers = hasPermission(
    activeRole,
    projectPermissions.membersRemove,
  );

  const logEvent = (message: string) => {
    setActivityLog((previous) => [
      `${new Date().toLocaleTimeString("ru-RU")} - ${message}`,
      ...previous.slice(0, 24),
    ]);
  };

  const logMemberEvent = (message: string) => {
    setMemberActivityLog((previous) => [
      `${new Date().toLocaleTimeString("ru-RU")} - ${message}`,
      ...previous.slice(0, 24),
    ]);
  };

  const logProjectEvent = (message: string, projectIdOverride?: string) => {
    const projectId = (projectIdOverride ?? activeProjectId).trim();
    if (!projectId) {
      return;
    }

    const entry = `${new Date().toLocaleTimeString("ru-RU")} - ${message}`;
    setProjectActivityMap((previous) => {
      const nextEntries = [entry, ...(previous[projectId] ?? [])].slice(
        0,
        PROJECT_ACTIVITY_MAX_ITEMS,
      );
      const nextMap: StoredProjectActivityMap = {
        ...previous,
        [projectId]: nextEntries,
      };

      writeStoredProjectActivityMap(nextMap);
      return nextMap;
    });
  };

  const projectsQuery = useQuery({
    queryKey: ["projects", apiSession.baseUrl, apiSession.token],
    queryFn: () => listProjectsRequest(apiSession),
  });

  const projectOptions = useMemo(() => projectsQuery.data ?? [], [projectsQuery.data]);
  const projectFilterTerm = useMemo(
    () => projectFilterInput.trim().toLowerCase(),
    [projectFilterInput],
  );
  const filteredProjectOptions = useMemo(
    () =>
      projectOptions.filter(
        (project) =>
          includesFilterTerm(project.name, projectFilterTerm) ||
          includesFilterTerm(project.status, projectFilterTerm) ||
          includesFilterTerm(project.id, projectFilterTerm),
      ),
    [projectFilterTerm, projectOptions],
  );

  const activeProjectId =
    projectOptions.length === 0
      ? ""
      : projectOptions.some((project) => project.id === openedProjectId)
        ? openedProjectId
        : projectOptions[0].id;

  const openedProject =
    projectOptions.find((project) => project.id === activeProjectId) ?? null;
  const projectActivityLog = activeProjectId
    ? projectActivityMap[activeProjectId] ?? []
    : [];

  const accountsQuery = useQuery({
    queryKey: ["accounts", apiSession.baseUrl, apiSession.token, activeProjectId],
    queryFn: () => listAccountsRequest(apiSession, activeProjectId),
    enabled: Boolean(activeProjectId),
  });

  const accountOptions = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data]);
  const accountFilterTerm = useMemo(
    () => accountFilterInput.trim().toLowerCase(),
    [accountFilterInput],
  );
  const filteredAccountOptions = useMemo(
    () =>
      accountOptions.filter(
        (account) =>
          includesFilterTerm(account.displayName, accountFilterTerm) ||
          includesFilterTerm(account.platform, accountFilterTerm) ||
          includesFilterTerm(account.businessStatus, accountFilterTerm) ||
          includesFilterTerm(account.id, accountFilterTerm),
      ),
    [accountFilterTerm, accountOptions],
  );

  const effectiveAccountId = useMemo(() => {
    if (accountOptions.length === 0) {
      return "";
    }

    const isSelectedAvailable = accountOptions.some(
      (account) => account.id === selectedAccountId,
    );

    return isSelectedAvailable ? selectedAccountId : accountOptions[0].id;
  }, [accountOptions, selectedAccountId]);
  const selectedAccount = useMemo(
    () => accountOptions.find((account) => account.id === effectiveAccountId) ?? null,
    [accountOptions, effectiveAccountId],
  );

  const effectiveGatewayRouteKey = useMemo(() => {
    const normalized = gatewayRouteKeyInput.trim();
    if (normalized) {
      return normalized;
    }

    return effectiveAccountId ? buildRouteKey(effectiveAccountId) : "";
  }, [effectiveAccountId, gatewayRouteKeyInput]);

  const maskedProxyQuery = useQuery({
    queryKey: [
      "masked-proxy",
      apiSession.baseUrl,
      apiSession.token,
      activeProjectId,
      effectiveAccountId,
    ],
    queryFn: () =>
      getMaskedProxyCredentialsRequest(
        apiSession,
        activeProjectId,
        effectiveAccountId,
      ),
    enabled: Boolean(activeProjectId && effectiveAccountId),
  });

  const createProjectMutation = useMutation({
    mutationFn: async () => {
      const normalizedName = projectNameInput.trim();
      if (!normalizedName) {
        throw new Error("Название проекта обязательно.");
      }

      return createProjectRequest(apiSession, normalizedName);
    },
    onSuccess: async (project) => {
      setStatusMessage(`Проект "${project.name}" создан.`);
      logEvent(`createProject -> ${project.id}`);
      logProjectEvent(`Проект создан: ${project.name}`, project.id);
      setOpenedProjectId(project.id);
      writeStoredOpenedProjectId(project.id);
      setSelectedAccountId("");
      setAccountFilterInput("");
      setActiveProjectTab("accounts");
      applyProjectModuleState(project.id);
      await queryClient.invalidateQueries({ queryKey: ["projects"] });
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка createProject.");
    },
  });

  const addMemberMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      const userId = memberUserIdInput.trim();
      if (!isGuid(userId)) {
        throw new Error("userId должен быть GUID.");
      }

      return addProjectMemberRequest(apiSession, activeProjectId, {
        userId,
        role: memberRoleInput,
      });
    },
    onSuccess: () => {
      setStatusMessage("Участник добавлен в проект.");
      logEvent(`addMember -> ${memberUserIdInput.trim()}`);
      logMemberEvent(`add -> ${memberUserIdInput.trim()} (${memberRoleInput})`);
      logProjectEvent(`Участник добавлен: ${memberUserIdInput.trim()} (${memberRoleInput})`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка addMember.");
    },
  });

  const changeMemberRoleMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      const userId = memberChangeUserIdInput.trim();
      if (!isGuid(userId)) {
        throw new Error("userId должен быть GUID.");
      }

      return changeProjectMemberRoleRequest(
        apiSession,
        activeProjectId,
        userId,
        memberChangeRoleInput,
      );
    },
    onSuccess: () => {
      setStatusMessage("Роль участника обновлена.");
      logEvent(`changeMemberRole -> ${memberChangeUserIdInput.trim()} (${memberChangeRoleInput})`);
      logMemberEvent(
        `changeRole -> ${memberChangeUserIdInput.trim()} (${memberChangeRoleInput})`,
      );
      logProjectEvent(
        `Роль изменена: ${memberChangeUserIdInput.trim()} -> ${memberChangeRoleInput}`,
      );
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error ? error.message : "Ошибка changeMemberRole.",
      );
    },
  });

  const removeMemberMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      const userId = memberRemoveUserIdInput.trim();
      if (!isGuid(userId)) {
        throw new Error("userId должен быть GUID.");
      }

      return removeProjectMemberRequest(apiSession, activeProjectId, userId);
    },
    onSuccess: () => {
      setStatusMessage("Участник удалён из проекта.");
      logEvent(`removeMember -> ${memberRemoveUserIdInput.trim()}`);
      logMemberEvent(`remove -> ${memberRemoveUserIdInput.trim()}`);
      logProjectEvent(`Участник удалён: ${memberRemoveUserIdInput.trim()}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка removeMember.");
    },
  });

  const bulkInviteMembersMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      const role = bulkInviteRoleInput;
      const result = await runBulkMemberAction(
        bulkInviteUserIdsInput,
        async (userId) => {
          await addProjectMemberRequest(apiSession, activeProjectId, { userId, role });
        },
      );

      return { role, result };
    },
    onSuccess: ({ role, result }) => {
      const actionLabel = `Bulk add (${role})`;
      setMemberBulkResult({ actionLabel, result });
      setStatusMessage(formatBulkResultStatus("Bulk add", result));
      logEvent(
        `${actionLabel} -> success ${result.succeeded.length}/${result.attempted}`,
      );
      logMemberEvent(
        `${actionLabel} -> success ${result.succeeded.length}/${result.attempted}`,
      );
      logProjectEvent(formatBulkResultStatus(actionLabel, result));
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error ? error.message : "Ошибка bulk add members.",
      );
    },
  });

  const bulkChangeMemberRoleMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      const role = bulkRoleChangeRoleInput;
      const result = await runBulkMemberAction(
        bulkRoleChangeUserIdsInput,
        async (userId) => {
          await changeProjectMemberRoleRequest(
            apiSession,
            activeProjectId,
            userId,
            role,
          );
        },
      );

      return { role, result };
    },
    onSuccess: ({ role, result }) => {
      const actionLabel = `Bulk change role (${role})`;
      setMemberBulkResult({ actionLabel, result });
      setStatusMessage(formatBulkResultStatus("Bulk change role", result));
      logEvent(
        `${actionLabel} -> success ${result.succeeded.length}/${result.attempted}`,
      );
      logMemberEvent(
        `${actionLabel} -> success ${result.succeeded.length}/${result.attempted}`,
      );
      logProjectEvent(formatBulkResultStatus(actionLabel, result));
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : "Ошибка bulk change member role.",
      );
    },
  });

  const bulkRemoveMemberMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      const result = await runBulkMemberAction(bulkRemoveUserIdsInput, async (userId) => {
        await removeProjectMemberRequest(apiSession, activeProjectId, userId);
      });

      return result;
    },
    onSuccess: (result) => {
      const actionLabel = "Bulk remove";
      setMemberBulkResult({ actionLabel, result });
      setStatusMessage(formatBulkResultStatus(actionLabel, result));
      logEvent(
        `${actionLabel} -> success ${result.succeeded.length}/${result.attempted}`,
      );
      logMemberEvent(
        `${actionLabel} -> success ${result.succeeded.length}/${result.attempted}`,
      );
      logProjectEvent(formatBulkResultStatus(actionLabel, result));
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error ? error.message : "Ошибка bulk remove members.",
      );
    },
  });

  const createAccountMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      const proxyPort = Number(createProxyPortInput);
      if (!Number.isInteger(proxyPort) || proxyPort < 1 || proxyPort > 65_535) {
        throw new Error("Порт proxy должен быть в диапазоне 1..65535.");
      }

      const payload: AccountCreateRequest = {
        platform: createPlatformInput.trim(),
        displayName: createDisplayNameInput.trim(),
        proxyConfig: {
          host: createProxyHostInput.trim(),
          port: proxyPort,
          login: createProxyLoginInput.trim(),
          password: createProxyPasswordInput.trim(),
        },
      };

      if (!payload.platform || !payload.displayName) {
        throw new Error("platform и displayName обязательны.");
      }

      if (
        !payload.proxyConfig.host ||
        !payload.proxyConfig.login ||
        !payload.proxyConfig.password
      ) {
        throw new Error("Все поля proxyConfig обязательны.");
      }

      return createAccountRequest(apiSession, activeProjectId, payload);
    },
    onSuccess: async (account) => {
      setStatusMessage(`Аккаунт "${account.displayName}" добавлен в проект.`);
      logEvent(`createAccount -> ${account.id}`);
      logProjectEvent(`Аккаунт добавлен: ${account.displayName} (${account.platform})`);
      setSelectedAccountId(account.id);
      setGatewayRouteKeyInput(buildRouteKey(account.id));
      await queryClient.invalidateQueries({ queryKey: ["accounts"] });
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка createAccount.");
    },
  });

  const updateProxyMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId || !effectiveAccountId) {
        throw new Error("Нужно выбрать проект и аккаунт.");
      }

      const proxyPort = Number(updateProxyPortInput);
      if (!Number.isInteger(proxyPort) || proxyPort < 1 || proxyPort > 65_535) {
        throw new Error("Порт proxy должен быть в диапазоне 1..65535.");
      }

      await updateProxyCredentialsRequest(
        apiSession,
        activeProjectId,
        effectiveAccountId,
        {
          reason: updateReasonInput.trim(),
          proxyConfig: {
            host: updateProxyHostInput.trim(),
            port: proxyPort,
            login: updateProxyLoginInput.trim(),
            password: updateProxyPasswordInput.trim(),
          },
        },
      );
    },
    onSuccess: async () => {
      setStatusMessage("Proxy credentials обновлены.");
      logEvent(`updateAccountProxyCredentials -> ${effectiveAccountId}`);
      logProjectEvent(`Proxy обновлен для аккаунта: ${effectiveAccountId}`);
      await queryClient.invalidateQueries({ queryKey: ["masked-proxy"] });
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : "Ошибка updateAccountProxyCredentials.",
      );
    },
  });

  const revealProxyMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId || !effectiveAccountId) {
        throw new Error("Нужно выбрать проект и аккаунт.");
      }

      return revealProxyCredentialsRequest(apiSession, activeProjectId, effectiveAccountId, {
        reason: revealReasonInput.trim(),
      });
    },
    onSuccess: (proxyConfig) => {
      setRevealedProxyConfig(proxyConfig);
      setStatusMessage("Полные proxy credentials получены.");
      logEvent(`revealAccountProxyCredentials -> ${effectiveAccountId}`);
      logProjectEvent(`Proxy reveal выполнен для аккаунта: ${effectiveAccountId}`);
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : "Ошибка revealAccountProxyCredentials.",
      );
    },
  });

  const moduleActionMutation = useMutation({
    mutationFn: async (params: {
      resultKey: ModuleResultKey;
      action: string;
      payloadInput: string;
      successMessage: string;
    }) => {
      if (!effectiveAccountId) {
        throw new Error("Выберите аккаунт проекта.");
      }

      const payload = parseJsonInput(params.payloadInput);
      const result = await proxyAccountActionRequest(
        apiSession,
        buildRouteKey(effectiveAccountId),
        params.action,
        payload,
      );

      return {
        ...params,
        result,
      };
    },
    onSuccess: ({ resultKey, result, successMessage, action }) => {
      setModuleResults((previous) => ({
        ...previous,
        [resultKey]: normalizeObjectResult(result),
      }));
      setStatusMessage(successMessage);
      logEvent(`${action} -> ${effectiveAccountId}`);
      logProjectEvent(`${action} -> account ${effectiveAccountId}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка module action.");
    },
  });

  const createPaymentMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      const amount = Number(paymentAmountInput);
      if (Number.isNaN(amount) || amount <= 0) {
        throw new Error("Сумма платежа должна быть числом больше нуля.");
      }

      return createPaymentRequest(apiSession, activeProjectId, {
        amount,
        currency: paymentCurrencyInput.trim() || "USD",
        operation: "ui.create-payment",
      });
    },
    onSuccess: (result) => {
      setBillingResult(normalizeObjectResult(result));
      setStatusMessage("Платёж создан через Core/Billing.");
      logEvent(`createPayment -> ${activeProjectId}`);
      logProjectEvent(`Billing: createPayment (${paymentAmountInput} ${paymentCurrencyInput})`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка createPayment.");
    },
  });

  const changePlanMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      await changePlanRequest(apiSession, activeProjectId, {
        planKey: planKeyInput.trim(),
        reason: "ui.change-plan",
      });
    },
    onSuccess: () => {
      setStatusMessage("План проекта изменён.");
      logEvent(`changePlan -> ${activeProjectId}`);
      logProjectEvent(`Billing: plan changed -> ${planKeyInput.trim()}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка changePlan.");
    },
  });

  const purchaseAddonMutation = useMutation({
    mutationFn: async () => {
      if (!activeProjectId) {
        throw new Error("Сначала откройте проект.");
      }

      return purchaseAddonRequest(apiSession, activeProjectId, addonIdInput.trim(), {
        source: "ui.purchase-addon",
      });
    },
    onSuccess: (result) => {
      setBillingResult(normalizeObjectResult(result));
      setStatusMessage("Add-on приобретён.");
      logEvent(`purchaseAddon -> ${activeProjectId}`);
      logProjectEvent(`Billing: add-on purchased -> ${addonIdInput.trim()}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Ошибка purchaseAddon.");
    },
  });

  const gatewayMutation = useMutation({
    mutationFn: async () => {
      const routeKey = effectiveGatewayRouteKey;
      const action = gatewayActionInput.trim();

      if (!routeKey || !action) {
        throw new Error("routeKey и action обязательны.");
      }

      const payload = parseJsonInput(gatewayPayloadInput);
      return proxyAccountActionRequest(apiSession, routeKey, action, payload);
    },
    onSuccess: (result) => {
      setGatewayResult(normalizeObjectResult(result));
      setStatusMessage("Gateway proxy вызов выполнен.");
      logEvent(`proxyAccountApiAction -> ${gatewayActionInput}`);
      logProjectEvent(`Gateway action: ${gatewayActionInput.trim()}`);
    },
    onError: (error) => {
      setStatusMessage(
        error instanceof Error ? error.message : "Ошибка proxyAccountApiAction.",
      );
    },
  });

  const isAnyMemberOperationPending =
    addMemberMutation.isPending ||
    changeMemberRoleMutation.isPending ||
    removeMemberMutation.isPending ||
    bulkInviteMembersMutation.isPending ||
    bulkChangeMemberRoleMutation.isPending ||
    bulkRemoveMemberMutation.isPending;
  const moduleResultReadyCount = (
    Object.values(moduleResults) as (Record<string, unknown> | null)[]
  ).filter((result) => result !== null).length;
  const integrationPulse = {
    coreExternal:
      projectsQuery.error instanceof Error
        ? `Ошибка: ${projectsQuery.error.message}`
        : projectsQuery.isPending
          ? "Синхронизация..."
          : "OK",
    accountsSync: !activeProjectId
      ? "Проект не выбран"
      : accountsQuery.error instanceof Error
        ? `Ошибка: ${accountsQuery.error.message}`
        : accountsQuery.isPending
          ? "Синхронизация..."
          : `OK (${accountOptions.length})`,
    proxyChannel:
      !activeProjectId || !effectiveAccountId
        ? "Нет активного аккаунта"
        : maskedProxyQuery.error instanceof Error
          ? `Ошибка: ${maskedProxyQuery.error.message}`
          : maskedProxyQuery.isPending
            ? "Проверка..."
            : maskedProxyQuery.data?.configured
              ? "Configured"
              : "Not configured",
    gatewayChannel: !canUseGateway
      ? "Нет права project.workers.operate"
      : !effectiveGatewayRouteKey
        ? "routeKey не определён"
        : "Ready",
  };

  const updateRole = () => {
    if (roleDraft === session.profile.role) {
      setStatusMessage("UI-роль уже активна.");
      return;
    }

    const updated = updateSessionRole(session, roleDraft);
    onSessionChange(updated);
    setStatusMessage(`UI-роль переключена на ${roleDraft}.`);
    logEvent(`uiRoleSwitch -> ${roleDraft}`);
  };

  const openProject = (projectId: string) => {
    setOpenedProjectId(projectId);
    writeStoredOpenedProjectId(projectId);
    setActiveProjectTab("accounts");
    setSelectedAccountId("");
    setAccountFilterInput("");
    setGatewayRouteKeyInput("");
    setRevealedProxyConfig(null);
    setMemberActivityLog([]);
    setBulkInviteUserIdsInput("");
    setBulkRoleChangeUserIdsInput("");
    setBulkRemoveUserIdsInput("");
    setMemberBulkResult(null);
    applyProjectModuleState(projectId);
    setStatusMessage("Проект открыт.");
    logEvent(`openProject -> ${projectId}`);
    logProjectEvent("Проект открыт в UI", projectId);
  };

  const selectAccount = (accountId: string) => {
    setSelectedAccountId(accountId);
    setGatewayRouteKeyInput(buildRouteKey(accountId));
  };

  const clearProjectActivityLog = () => {
    if (!activeProjectId) {
      return;
    }

    setProjectActivityMap((previous) => {
      const nextMap: StoredProjectActivityMap = {
        ...previous,
        [activeProjectId]: [],
      };
      writeStoredProjectActivityMap(nextMap);
      return nextMap;
    });

    setStatusMessage("История операций проекта очищена.");
    logEvent(`clearProjectActivityLog -> ${activeProjectId}`);
  };

  const applyProjectModuleState = (projectId: string) => {
    const state = readStoredProjectModuleState(projectId);
    setProductsPayloadInput(state.productsPayload);
    setMessagesPayloadInput(state.messagesPayload);
    setOrdersPayloadInput(state.ordersPayload);
  };

  const patchActiveProjectModuleState = (
    patch: Partial<StoredProjectModuleState>,
  ) => {
    if (!activeProjectId) {
      return;
    }

    const nextState: StoredProjectModuleState = {
      ...readStoredProjectModuleState(activeProjectId),
      ...patch,
    };

    writeStoredProjectModuleState(activeProjectId, nextState);
  };

  const handleProductsPayloadInputChange = (value: string) => {
    setProductsPayloadInput(value);
    patchActiveProjectModuleState({ productsPayload: value });
  };

  const handleMessagesPayloadInputChange = (value: string) => {
    setMessagesPayloadInput(value);
    patchActiveProjectModuleState({ messagesPayload: value });
  };

  const handleOrdersPayloadInputChange = (value: string) => {
    setOrdersPayloadInput(value);
    patchActiveProjectModuleState({ ordersPayload: value });
  };

  const renderModuleResultTable = (
    result: Record<string, unknown> | null,
    emptyMessage: string,
  ) => {
    if (!result) {
      return <p className="hint">{emptyMessage}</p>;
    }

    const rows = extractResultRows(result);
    if (rows.length === 0) {
      return <p className="hint">{emptyMessage}</p>;
    }

    const columns = collectResultColumns(rows);
    if (columns.length === 0) {
      return <p className="hint">{emptyMessage}</p>;
    }

    return (
      <div className="result-table-wrap">
        <table className="result-table">
          <thead>
            <tr>
              {columns.map((column) => (
                <th key={column}>{column}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, rowIndex) => (
              <tr key={`${rowIndex}-${Object.keys(row).join("-")}`}>
                {columns.map((column) => (
                  <td key={`${rowIndex}-${column}`}>{formatResultCell(row[column])}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  };

  const renderAccountSelector = (label = "Аккаунт проекта") => {
    if (!activeProjectId) {
      return <p className="hint">Сначала откройте проект.</p>;
    }

    if (accountsQuery.isPending) {
      return <p className="hint">Загрузка аккаунтов...</p>;
    }

    if (accountsQuery.error) {
      return (
        <p className="error">
          {accountsQuery.error instanceof Error
            ? accountsQuery.error.message
            : "Ошибка listAccounts."}
        </p>
      );
    }

    if (accountOptions.length === 0) {
      return <p className="hint">В проекте пока нет аккаунтов. Добавьте первый аккаунт.</p>;
    }

    return (
      <label className="field">
        <span>{label}</span>
        <select
          className="input"
          value={effectiveAccountId}
          onChange={(event) => selectAccount(event.target.value)}
        >
          {accountOptions.map((account) => (
            <option key={account.id} value={account.id}>
              {account.displayName} · {account.platform} · {account.businessStatus}
            </option>
          ))}
        </select>
      </label>
    );
  };

  useEffect(() => {
    writeStoredOpenedProjectId(activeProjectId);
  }, [activeProjectId]);

  return (
    <main className="platform-shell" data-testid="platform-console">
      <aside className="platform-sidebar" data-testid="platform-sidebar">
        <div className="platform-brand">
          <p className="eyebrow">DDCRM PLATFORM</p>
          <h1>Control Center</h1>
          <p className="hint">
            {session.profile.displayName} · {session.profile.email}
          </p>
        </div>

        <nav className="platform-nav" data-testid="main-sections-nav">
          {(Object.keys(mainSectionLabels) as MainSection[]).map((section) => (
            <button
              key={section}
              type="button"
              className={`platform-nav-item ${activeSection === section ? "is-active" : ""}`}
              onClick={() => setActiveSection(section)}
              data-testid={`main-section-${section}`}
            >
              {mainSectionLabels[section]}
            </button>
          ))}
        </nav>

        {openedProject ? (
          <div className="project-chip" data-testid="opened-project-chip">
            <p className="eyebrow">Открыт Проект</p>
            <strong data-testid="opened-project-chip-name">{openedProject.name}</strong>
          </div>
        ) : null}

        <div className="platform-side-footer">
          <p className="hint mono">baseUrl: {session.baseUrl}</p>
          <button type="button" className="button button-ghost" onClick={onLogout}>
            Выйти
          </button>
        </div>
      </aside>

      <section className="platform-main">
        <header className="platform-header">
          <div>
            <p className="eyebrow">Рабочая область</p>
            <h2 data-testid="workspace-title">{mainSectionLabels[activeSection]}</h2>
          </div>
          <div className="platform-status" data-testid="platform-status">
            <span className="status-dot" />
            <span data-testid="platform-status-message">{statusMessage}</span>
          </div>
        </header>

        {activeSection === "projects" ? (
          <div className="projects-layout">
            <section className="panel" data-testid="projects-panel">
              <h3>Мои проекты</h3>
              <p className="hint">Всего проектов: {projectOptions.length}</p>
              <div className="workflow-note" data-testid="projects-workflow">
                <p>
                  <strong>Сценарий теста:</strong> выберите или создайте проект, откройте
                  его и проверьте вкладки Аккаунты, Товары, Сообщения и другие модули.
                </p>
              </div>
              <label className="field">
                <span>Поиск проекта (name / status / id)</span>
                <div className="filter-row">
                  <input
                    className="input"
                    value={projectFilterInput}
                    onChange={(event) => setProjectFilterInput(event.target.value)}
                    placeholder="Например: demo, active, projectId"
                    data-testid="project-filter-input"
                  />
                  {projectFilterInput.trim() ? (
                    <button
                      type="button"
                      className="button button-ghost"
                      onClick={() => setProjectFilterInput("")}
                    >
                      Очистить
                    </button>
                  ) : null}
                </div>
              </label>

              {projectsQuery.isPending ? (
                <p className="hint">Загрузка проектов...</p>
              ) : projectsQuery.error ? (
                <p className="error">
                  {projectsQuery.error instanceof Error
                    ? projectsQuery.error.message
                    : "Ошибка listProjects."}
                </p>
              ) : projectOptions.length === 0 ? (
                <div className="empty-state">
                  <p>Проектов пока нет. Создайте первый проект.</p>
                </div>
              ) : filteredProjectOptions.length === 0 ? (
                <div className="empty-state">
                  <p>По текущему фильтру проекты не найдены.</p>
                  <p className="hint">Попробуйте изменить или очистить поиск.</p>
                </div>
              ) : (
                <ul className="project-list" data-testid="project-list">
                  {filteredProjectOptions.map((project) => (
                    <li key={project.id}>
                      <button
                        type="button"
                        className={`project-item ${openedProject?.id === project.id ? "is-active" : ""}`}
                        onClick={() => openProject(project.id)}
                        data-testid={`project-item-${toStableTestId(project.id)}`}
                      >
                        <strong>{project.name}</strong>
                        <span>{project.status}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              {openedProject &&
              projectFilterTerm &&
              !filteredProjectOptions.some((project) => project.id === openedProject.id) ? (
                <div className="empty-state">
                  <p>
                    Открыт проект <strong>{openedProject.name}</strong>, но он скрыт
                    фильтром.
                  </p>
                  <button
                    type="button"
                    className="button button-ghost"
                    onClick={() => setProjectFilterInput("")}
                  >
                    Показать открытый проект
                  </button>
                </div>
              ) : null}

              <div className="stack">
                <label className="field">
                  <span>Название нового проекта</span>
                  <input
                    className="input"
                    value={projectNameInput}
                    onChange={(event) => setProjectNameInput(event.target.value)}
                    placeholder="Например: Продажи FunPay RU"
                    data-testid="create-project-name-input"
                  />
                </label>
                <button
                  className="button button-primary"
                  disabled={createProjectMutation.isPending}
                  onClick={() => createProjectMutation.mutate()}
                  type="button"
                  data-testid="create-project-button"
                >
                  Создать проект
                </button>
              </div>
            </section>

            <div className="project-workspace" data-testid="project-workspace">
              {openedProject ? (
                <>
                  <section className="panel">
                    <div className="project-workspace-header">
                      <div>
                        <p className="eyebrow">Проект</p>
                        <h3 data-testid="opened-project-name">{openedProject.name}</h3>
                        <p className="hint mono" data-testid="opened-project-id">
                          projectId: {openedProject.id}
                        </p>
                      </div>
                      <div className="hint" data-testid="opened-project-accounts-count">
                        Аккаунтов в проекте: {accountOptions.length}
                      </div>
                    </div>

                    <div className="project-tab-row" data-testid="project-tabs">
                      {(Object.keys(projectTabLabels) as ProjectTab[]).map((tab) => (
                        <button
                          key={tab}
                          type="button"
                          className={`project-tab-btn ${activeProjectTab === tab ? "is-active" : ""}`}
                          onClick={() => setActiveProjectTab(tab)}
                          data-testid={`project-tab-${tab}`}
                        >
                          {projectTabLabels[tab]}
                        </button>
                      ))}
                    </div>
                  </section>

                  {activeProjectTab === "overview" ? (
                    <div className="dashboard-grid">
                      <section className="panel">
                        <h3>Сводка проекта</h3>
                        <div className="metric-grid">
                          <article className="metric-card">
                            <p>Status</p>
                            <strong>{openedProject.status}</strong>
                            <span>Текущий бизнес-статус проекта</span>
                          </article>
                          <article className="metric-card">
                            <p>Аккаунты</p>
                            <strong>{accountOptions.length}</strong>
                            <span>Подключено аккаунтов в проекте</span>
                          </article>
                          <article className="metric-card">
                            <p>UI роль</p>
                            <strong>{activeRole}</strong>
                            <span>Текущая роль в интерфейсе</span>
                          </article>
                          <article className="metric-card">
                            <p>Модульные ответы</p>
                            <strong>{moduleResultReadyCount}/3</strong>
                            <span>Товары, сообщения, заказы</span>
                          </article>
                        </div>

                        <div className="stack">
                          <h3>Операционный пульс</h3>
                          <ul className="activity-list">
                            <li>
                              Core external API: <strong>{integrationPulse.coreExternal}</strong>
                            </li>
                            <li>
                              Accounts sync: <strong>{integrationPulse.accountsSync}</strong>
                            </li>
                            <li>
                              Proxy channel: <strong>{integrationPulse.proxyChannel}</strong>
                            </li>
                            <li>
                              Gateway channel: <strong>{integrationPulse.gatewayChannel}</strong>
                            </li>
                          </ul>
                        </div>

                        <ul className="activity-list">
                          <li>
                            projectId: <span className="mono">{openedProject.id}</span>
                          </li>
                          <li>
                            Выбранный аккаунт:{" "}
                            {selectedAccount
                              ? `${selectedAccount.displayName} (${selectedAccount.platform})`
                              : "не выбран"}
                          </li>
                          <li>
                            Gateway routeKey:{" "}
                            <span className="mono">
                              {effectiveGatewayRouteKey || "n/a"}
                            </span>
                          </li>
                        </ul>

                        {memberBulkResult ? (
                          <div className="stack">
                            <p className="hint">Последняя bulk-операция участников:</p>
                            <p className="hint">
                              {formatBulkResultStatus(
                                memberBulkResult.actionLabel,
                                memberBulkResult.result,
                              )}
                            </p>
                          </div>
                        ) : null}
                      </section>

                      <section className="panel">
                        <h3>Быстрые действия</h3>
                        <p className="hint">
                          Переходы по ключевым вкладкам рабочего пространства проекта.
                        </p>
                        <div className="quick-action-grid">
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={() => setActiveProjectTab("accounts")}
                          >
                            Открыть аккаунты
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={() => setActiveProjectTab("members")}
                          >
                            Открыть участников
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={() => setActiveProjectTab("products")}
                            disabled={!canOperateModules || !canUseGateway}
                          >
                            Открыть товары
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={() => setActiveProjectTab("messages")}
                            disabled={!canOperateModules || !canUseGateway}
                          >
                            Открыть сообщения
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={() => setActiveProjectTab("orders")}
                            disabled={!canOperateModules || !canUseGateway}
                          >
                            Открыть заказы
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={() => setActiveProjectTab("proxy")}
                            disabled={!canRevealProxy && !canUpdateProxy}
                          >
                            Открыть proxy
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={() => setActiveProjectTab("billing")}
                            disabled={!canViewBilling}
                          >
                            Открыть billing
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={() => setActiveProjectTab("gateway")}
                            disabled={!canUseGateway}
                          >
                            Открыть gateway
                          </button>
                        </div>
                      </section>

                      <section className="panel">
                        <div className="project-workspace-header">
                          <h3>Последние действия проекта</h3>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={clearProjectActivityLog}
                            disabled={projectActivityLog.length === 0}
                          >
                            Очистить
                          </button>
                        </div>
                        {projectActivityLog.length === 0 ? (
                          <p className="hint">
                            Для этого проекта действий пока нет. Запустите любую
                            операцию во вкладках проекта.
                          </p>
                        ) : (
                          <ul className="activity-list">
                            {projectActivityLog.map((entry, index) => (
                              <li key={`${index}-${entry}`}>{entry}</li>
                            ))}
                          </ul>
                        )}
                      </section>
                    </div>
                  ) : null}

                  {activeProjectTab === "members" ? (
                    <div className="dashboard-grid">
                      <section className="panel">
                        <h3>Участники проекта</h3>
                        <p className="hint">
                          В external API нет отдельного `listMembers`, поэтому здесь
                          доступны операции управления участниками.
                        </p>

                        <div className="stack">
                          <p className="hint">
                            Введите `userId` участника вручную (GUID), затем выберите роль.
                          </p>

                          <div className="stack">
                            <h3>Добавить участника</h3>
                            {canInviteMembers ? (
                              <>
                                <div className="grid-2">
                                  <label className="field">
                                    <span>User ID (GUID)</span>
                                    <input
                                      className="input"
                                      value={memberUserIdInput}
                                      onChange={(event) =>
                                        setMemberUserIdInput(event.target.value)
                                      }
                                      placeholder="22222222-2222-2222-2222-222222222222"
                                    />
                                  </label>
                                  <label className="field">
                                    <span>Role</span>
                                    <select
                                      className="input"
                                      value={memberRoleInput}
                                      onChange={(event) =>
                                        setMemberRoleInput(
                                          event.target.value as Exclude<ProjectRole, "owner">,
                                        )
                                      }
                                    >
                                      <option value="admin">admin</option>
                                      <option value="moderator">moderator</option>
                                    </select>
                                  </label>
                                </div>
                                <button
                                  type="button"
                                  className="button button-primary"
                                  disabled={isAnyMemberOperationPending}
                                  onClick={() => addMemberMutation.mutate()}
                                >
                                  Добавить участника
                                </button>
                              </>
                            ) : (
                              <p className="hint">
                                Для роли {activeRole} скрыта операция
                                `project.members.invite`.
                              </p>
                            )}
                          </div>

                          <div className="stack">
                            <h3>Сменить роль</h3>
                            {canChangeMemberRoles ? (
                              <>
                                <div className="grid-2">
                                  <label className="field">
                                    <span>User ID (GUID)</span>
                                    <input
                                      className="input"
                                      value={memberChangeUserIdInput}
                                      onChange={(event) =>
                                        setMemberChangeUserIdInput(event.target.value)
                                      }
                                      placeholder="33333333-3333-3333-3333-333333333333"
                                    />
                                  </label>
                                  <label className="field">
                                    <span>Новая роль</span>
                                    <select
                                      className="input"
                                      value={memberChangeRoleInput}
                                      onChange={(event) =>
                                        setMemberChangeRoleInput(
                                          event.target.value as ProjectRole,
                                        )
                                      }
                                    >
                                      {projectRoles.map((role) => (
                                        <option key={role} value={role}>
                                          {role}
                                        </option>
                                      ))}
                                    </select>
                                  </label>
                                </div>
                                <button
                                  type="button"
                                  className="button button-primary"
                                  disabled={isAnyMemberOperationPending}
                                  onClick={() => changeMemberRoleMutation.mutate()}
                                >
                                  Применить роль
                                </button>
                              </>
                            ) : (
                              <p className="hint">
                                Для роли {activeRole} скрыта операция
                                `project.roles.change`.
                              </p>
                            )}
                          </div>

                          <div className="stack">
                            <h3>Удалить участника</h3>
                            {canRemoveMembers ? (
                              <>
                                <label className="field">
                                  <span>User ID (GUID)</span>
                                  <input
                                    className="input"
                                    value={memberRemoveUserIdInput}
                                    onChange={(event) =>
                                      setMemberRemoveUserIdInput(event.target.value)
                                    }
                                    placeholder="33333333-3333-3333-3333-333333333333"
                                  />
                                </label>
                                <button
                                  type="button"
                                  className="button button-primary"
                                  disabled={isAnyMemberOperationPending}
                                  onClick={() => removeMemberMutation.mutate()}
                                >
                                  Удалить участника
                                </button>
                              </>
                            ) : (
                              <p className="hint">
                                Для роли {activeRole} скрыта операция
                                `project.members.remove`.
                              </p>
                            )}
                          </div>

                          <div className="stack">
                            <h3>Пакетные операции (bulk)</h3>
                            <p className="hint">
                              Поддерживаются разделители: новая строка, пробел, запятая
                              и `;`.
                            </p>

                            <div className="stack">
                              <h3>Bulk add</h3>
                              {canInviteMembers ? (
                                <>
                                  <label className="field">
                                    <span>User IDs (GUID list)</span>
                                    <textarea
                                      className="input textarea"
                                      value={bulkInviteUserIdsInput}
                                      onChange={(event) =>
                                        setBulkInviteUserIdsInput(event.target.value)
                                      }
                                      placeholder="22222222-2222-2222-2222-222222222222&#10;33333333-3333-3333-3333-333333333333"
                                    />
                                  </label>
                                  <div className="inline">
                                    <select
                                      className="input"
                                      value={bulkInviteRoleInput}
                                      onChange={(event) =>
                                        setBulkInviteRoleInput(
                                          event.target.value as Exclude<ProjectRole, "owner">,
                                        )
                                      }
                                    >
                                      <option value="admin">admin</option>
                                      <option value="moderator">moderator</option>
                                    </select>
                                    <button
                                      type="button"
                                      className="button button-primary"
                                      disabled={isAnyMemberOperationPending}
                                      onClick={() => bulkInviteMembersMutation.mutate()}
                                    >
                                      Запустить bulk add
                                    </button>
                                  </div>
                                </>
                              ) : (
                                <p className="hint">
                                  Для роли {activeRole} скрыта операция
                                  `project.members.invite`.
                                </p>
                              )}
                            </div>

                            <div className="stack">
                              <h3>Bulk change role</h3>
                              {canChangeMemberRoles ? (
                                <>
                                  <label className="field">
                                    <span>User IDs (GUID list)</span>
                                    <textarea
                                      className="input textarea"
                                      value={bulkRoleChangeUserIdsInput}
                                      onChange={(event) =>
                                        setBulkRoleChangeUserIdsInput(event.target.value)
                                      }
                                      placeholder="22222222-2222-2222-2222-222222222222&#10;33333333-3333-3333-3333-333333333333"
                                    />
                                  </label>
                                  <div className="inline">
                                    <select
                                      className="input"
                                      value={bulkRoleChangeRoleInput}
                                      onChange={(event) =>
                                        setBulkRoleChangeRoleInput(
                                          event.target.value as ProjectRole,
                                        )
                                      }
                                    >
                                      {projectRoles.map((role) => (
                                        <option key={role} value={role}>
                                          {role}
                                        </option>
                                      ))}
                                    </select>
                                    <button
                                      type="button"
                                      className="button button-primary"
                                      disabled={isAnyMemberOperationPending}
                                      onClick={() => bulkChangeMemberRoleMutation.mutate()}
                                    >
                                      Запустить bulk change
                                    </button>
                                  </div>
                                </>
                              ) : (
                                <p className="hint">
                                  Для роли {activeRole} скрыта операция
                                  `project.roles.change`.
                                </p>
                              )}
                            </div>

                            <div className="stack">
                              <h3>Bulk remove</h3>
                              {canRemoveMembers ? (
                                <>
                                  <label className="field">
                                    <span>User IDs (GUID list)</span>
                                    <textarea
                                      className="input textarea"
                                      value={bulkRemoveUserIdsInput}
                                      onChange={(event) =>
                                        setBulkRemoveUserIdsInput(event.target.value)
                                      }
                                      placeholder="22222222-2222-2222-2222-222222222222&#10;33333333-3333-3333-3333-333333333333"
                                    />
                                  </label>
                                  <button
                                    type="button"
                                    className="button button-primary"
                                    disabled={isAnyMemberOperationPending}
                                    onClick={() => bulkRemoveMemberMutation.mutate()}
                                  >
                                    Запустить bulk remove
                                  </button>
                                </>
                              ) : (
                                <p className="hint">
                                  Для роли {activeRole} скрыта операция
                                  `project.members.remove`.
                                </p>
                              )}
                            </div>
                          </div>
                        </div>
                      </section>

                      <section className="panel">
                        <h3>Локальная история операций</h3>
                        {memberActivityLog.length === 0 ? (
                          <p className="hint">
                            Операций по участникам в этой сессии пока не было.
                          </p>
                        ) : (
                          <ul className="activity-list">
                            {memberActivityLog.map((entry) => (
                              <li key={entry}>{entry}</li>
                            ))}
                          </ul>
                        )}

                        {memberBulkResult ? (
                          <div className="stack">
                            <h3>Отчёт последней bulk-операции</h3>
                            <p className="hint">
                              {formatBulkResultStatus(
                                memberBulkResult.actionLabel,
                                memberBulkResult.result,
                              )}
                            </p>
                            <pre className="pre">
                              {JSON.stringify(memberBulkResult.result, null, 2)}
                            </pre>
                          </div>
                        ) : null}
                      </section>
                    </div>
                  ) : null}

                  {activeProjectTab === "accounts" ? (
                    <div className="dashboard-grid">
                      <section className="panel" data-testid="accounts-list-panel">
                        <h3>Доступные аккаунты</h3>
                        <p className="hint">Всего аккаунтов: {accountOptions.length}</p>
                        <label className="field">
                          <span>Поиск аккаунта (name / platform / status / id)</span>
                          <div className="filter-row">
                            <input
                              className="input"
                              value={accountFilterInput}
                              onChange={(event) =>
                                setAccountFilterInput(event.target.value)
                              }
                              placeholder="Например: funpay, active, accountId"
                              data-testid="account-filter-input"
                            />
                            {accountFilterInput.trim() ? (
                              <button
                                type="button"
                                className="button button-ghost"
                                onClick={() => setAccountFilterInput("")}
                              >
                                Очистить
                              </button>
                            ) : null}
                          </div>
                        </label>
                        {accountsQuery.isPending ? (
                          <p className="hint">Загрузка аккаунтов...</p>
                        ) : accountsQuery.error ? (
                          <p className="error">
                            {accountsQuery.error instanceof Error
                              ? accountsQuery.error.message
                              : "Ошибка listAccounts."}
                          </p>
                        ) : accountOptions.length === 0 ? (
                          <div className="empty-state">
                            <p>В этом проекте пока нет аккаунтов.</p>
                          </div>
                        ) : filteredAccountOptions.length === 0 ? (
                          <div className="empty-state">
                            <p>По текущему фильтру аккаунты не найдены.</p>
                            <p className="hint">Очистите поиск или уточните запрос.</p>
                          </div>
                        ) : (
                          <ul className="account-list" data-testid="account-list">
                            {filteredAccountOptions.map((account) => (
                              <li key={account.id}>
                                <button
                                  type="button"
                                  className={`account-item ${effectiveAccountId === account.id ? "is-active" : ""}`}
                                  onClick={() => selectAccount(account.id)}
                                  data-testid={`account-item-${toStableTestId(account.id)}`}
                                >
                                  <strong>{account.displayName}</strong>
                                  <span>
                                    {account.platform} · {account.businessStatus}
                                  </span>
                                </button>
                              </li>
                            ))}
                          </ul>
                        )}
                        {effectiveAccountId &&
                        accountFilterTerm &&
                        !filteredAccountOptions.some(
                          (account) => account.id === effectiveAccountId,
                        ) ? (
                          <div className="empty-state">
                            <p>Выбранный аккаунт скрыт фильтром.</p>
                            <button
                              type="button"
                              className="button button-ghost"
                              onClick={() => setAccountFilterInput("")}
                            >
                              Показать выбранный аккаунт
                            </button>
                          </div>
                        ) : null}
                      </section>

                      <section className="panel" data-testid="create-account-panel">
                        <h3>Добавить аккаунт</h3>
                        {canManageAccountLifecycle ? (
                          <div className="stack">
                            <div className="grid-2">
                              <label className="field">
                                <span>Platform</span>
                                <input
                                  className="input"
                                  value={createPlatformInput}
                                  onChange={(event) => setCreatePlatformInput(event.target.value)}
                                  data-testid="create-account-platform-input"
                                />
                              </label>
                              <label className="field">
                                <span>Display Name</span>
                                <input
                                  className="input"
                                  value={createDisplayNameInput}
                                  onChange={(event) =>
                                    setCreateDisplayNameInput(event.target.value)
                                  }
                                  data-testid="create-account-display-name-input"
                                />
                              </label>
                            </div>

                            <div className="grid-4">
                              <input
                                className="input"
                                placeholder="proxy host"
                                value={createProxyHostInput}
                                onChange={(event) =>
                                  setCreateProxyHostInput(event.target.value)
                                }
                                data-testid="create-account-proxy-host-input"
                              />
                              <input
                                className="input"
                                placeholder="port"
                                value={createProxyPortInput}
                                onChange={(event) =>
                                  setCreateProxyPortInput(event.target.value)
                                }
                                data-testid="create-account-proxy-port-input"
                              />
                              <input
                                className="input"
                                placeholder="login"
                                value={createProxyLoginInput}
                                onChange={(event) =>
                                  setCreateProxyLoginInput(event.target.value)
                                }
                                data-testid="create-account-proxy-login-input"
                              />
                              <input
                                className="input"
                                placeholder="password"
                                type="password"
                                value={createProxyPasswordInput}
                                onChange={(event) =>
                                  setCreateProxyPasswordInput(event.target.value)
                                }
                                data-testid="create-account-proxy-password-input"
                              />
                            </div>

                            <button
                              className="button button-primary"
                              disabled={createAccountMutation.isPending}
                              onClick={() => createAccountMutation.mutate()}
                              type="button"
                              data-testid="create-account-button"
                            >
                              Добавить аккаунт
                            </button>
                          </div>
                        ) : (
                          <p className="hint">
                            Для роли {activeRole} скрыта операция
                            `project.accounts.lifecycle.manage`.
                          </p>
                        )}
                      </section>
                    </div>
                  ) : null}

                  {activeProjectTab === "products" ? (
                    <section className="panel">
                      <h3>Товары</h3>
                      <p className="hint">
                        MVP: вкладка работает через action `products.list` в worker-контуре `v2`.
                      </p>
                      <p className="hint">
                        Параметры запроса сохраняются отдельно для каждого проекта.
                      </p>
                      {canOperateModules && canUseGateway ? (
                        <div className="stack">
                          {renderAccountSelector("Аккаунт для работы с товарами")}
                          <label className="field">
                            <span>Payload (JSON)</span>
                            <textarea
                              className="input textarea"
                              value={productsPayloadInput}
                              onChange={(event) =>
                                handleProductsPayloadInputChange(event.target.value)
                              }
                            />
                          </label>
                          <button
                            type="button"
                            className="button button-primary"
                            disabled={moduleActionMutation.isPending || !effectiveAccountId}
                            onClick={() =>
                              moduleActionMutation.mutate({
                                resultKey: "products",
                                action: "products.list",
                                payloadInput: productsPayloadInput,
                                successMessage: "Товары получены.",
                              })
                            }
                          >
                            Обновить товары
                          </button>
                          {renderModuleResultTable(
                            moduleResults.products,
                            "Выполните запрос, чтобы увидеть таблицу товаров.",
                          )}
                          {moduleResults.products ? (
                            <pre className="pre">{JSON.stringify(moduleResults.products, null, 2)}</pre>
                          ) : null}
                        </div>
                      ) : (
                        <p className="hint">У роли {activeRole} нет доступа к модулю товаров.</p>
                      )}
                    </section>
                  ) : null}

                  {activeProjectTab === "messages" ? (
                    <section className="panel">
                      <h3>Сообщения и чаты</h3>
                      <p className="hint">
                        Вкладка работает через `v2` action-ы:
                        `conversations.list`, `conversations.messages.list`, `conversations.messages.send`.
                      </p>
                      <p className="hint">
                        Параметры запроса сохраняются отдельно для каждого проекта.
                      </p>
                      {canOperateModules && canUseGateway ? (
                        <div className="stack">
                          {renderAccountSelector("Аккаунт для работы с сообщениями")}
                          <label className="field">
                            <span>Payload (JSON)</span>
                            <textarea
                              className="input textarea"
                              value={messagesPayloadInput}
                              onChange={(event) =>
                                handleMessagesPayloadInputChange(event.target.value)
                              }
                            />
                          </label>
                          <button
                            type="button"
                            className="button button-primary"
                            disabled={moduleActionMutation.isPending || !effectiveAccountId}
                            onClick={() =>
                              moduleActionMutation.mutate({
                                resultKey: "messages",
                                action: "conversations.list",
                                payloadInput: messagesPayloadInput,
                                successMessage: "Список переписок получен.",
                              })
                            }
                          >
                            Список переписок
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            disabled={moduleActionMutation.isPending || !effectiveAccountId}
                            onClick={() =>
                              moduleActionMutation.mutate({
                                resultKey: "messages",
                                action: "conversations.messages.list",
                                payloadInput: messagesPayloadInput,
                                successMessage: "История переписки получена.",
                              })
                            }
                          >
                            История переписки
                          </button>
                          <button
                            type="button"
                            className="button button-primary"
                            disabled={moduleActionMutation.isPending || !effectiveAccountId}
                            onClick={() =>
                              moduleActionMutation.mutate({
                                resultKey: "messages",
                                action: "conversations.messages.send",
                                payloadInput: messagesPayloadInput,
                                successMessage: "Сообщение отправлено.",
                              })
                            }
                          >
                            Отправить сообщение
                          </button>
                          {renderModuleResultTable(
                            moduleResults.messages,
                            "Выполните action, чтобы увидеть таблицу ответа.",
                          )}
                          {moduleResults.messages ? (
                            <pre className="pre">{JSON.stringify(moduleResults.messages, null, 2)}</pre>
                          ) : null}
                        </div>
                      ) : (
                        <p className="hint">У роли {activeRole} нет доступа к модулю сообщений.</p>
                      )}
                    </section>
                  ) : null}

                  {activeProjectTab === "orders" ? (
                    <section className="panel">
                      <h3>Схемы товаров</h3>
                      <p className="hint">
                        MVP: вкладка работает через action `products.schemas.list`.
                      </p>
                      <p className="hint">
                        Параметры запроса сохраняются отдельно для каждого проекта.
                      </p>
                      {canOperateModules && canUseGateway ? (
                        <div className="stack">
                          {renderAccountSelector("Аккаунт для работы с заказами")}
                          <label className="field">
                            <span>Payload (JSON)</span>
                            <textarea
                              className="input textarea"
                              value={ordersPayloadInput}
                              onChange={(event) =>
                                handleOrdersPayloadInputChange(event.target.value)
                              }
                            />
                          </label>
                          <button
                            type="button"
                            className="button button-primary"
                            disabled={moduleActionMutation.isPending || !effectiveAccountId}
                            onClick={() =>
                              moduleActionMutation.mutate({
                                resultKey: "orders",
                                action: "products.schemas.list",
                                payloadInput: ordersPayloadInput,
                                successMessage: "Схемы товаров получены.",
                              })
                            }
                          >
                            Обновить схемы
                          </button>
                          {renderModuleResultTable(
                            moduleResults.orders,
                            "Выполните запрос, чтобы увидеть доступные схемы.",
                          )}
                          {moduleResults.orders ? (
                            <pre className="pre">{JSON.stringify(moduleResults.orders, null, 2)}</pre>
                          ) : null}
                        </div>
                      ) : (
                        <p className="hint">У роли {activeRole} нет доступа к модулю заказов.</p>
                      )}
                    </section>
                  ) : null}

                  {activeProjectTab === "proxy" ? (
                    <div className="dashboard-grid">
                      <section className="panel">
                        <h3>Proxy state</h3>
                        {renderAccountSelector("Аккаунт для proxy-операций")}

                        {maskedProxyQuery.isPending ? (
                          <p className="hint">Загрузка маскированных данных...</p>
                        ) : maskedProxyQuery.error ? (
                          <p className="error">
                            {maskedProxyQuery.error instanceof Error
                              ? maskedProxyQuery.error.message
                              : "Ошибка getAccountProxyCredentialsMasked."}
                          </p>
                        ) : maskedProxyQuery.data ? (
                          <div className="hint">
                            configured={String(maskedProxyQuery.data.configured)} · host=
                            {maskedProxyQuery.data.hostMasked ?? "n/a"} · login=
                            {maskedProxyQuery.data.loginMasked ?? "n/a"}
                          </div>
                        ) : null}

                        {canRevealProxy ? (
                          <div className="inline">
                            <input
                              className="input"
                              value={revealReasonInput}
                              onChange={(event) => setRevealReasonInput(event.target.value)}
                              placeholder="reason (min 3)"
                            />
                            <button
                              className="button button-primary"
                              disabled={revealProxyMutation.isPending || !effectiveAccountId}
                              onClick={() => revealProxyMutation.mutate()}
                              type="button"
                            >
                              Reveal
                            </button>
                          </div>
                        ) : (
                          <p className="hint">Для роли {activeRole} скрыта операция reveal.</p>
                        )}

                        {revealedProxyConfig ? (
                          <pre className="pre">{JSON.stringify(revealedProxyConfig, null, 2)}</pre>
                        ) : null}
                      </section>

                      <section className="panel">
                        <h3>Update proxy</h3>
                        {canUpdateProxy ? (
                          <div className="stack">
                            <input
                              className="input"
                              value={updateReasonInput}
                              onChange={(event) => setUpdateReasonInput(event.target.value)}
                              placeholder="reason"
                            />
                            <div className="grid-4">
                              <input
                                className="input"
                                placeholder="host"
                                value={updateProxyHostInput}
                                onChange={(event) => setUpdateProxyHostInput(event.target.value)}
                              />
                              <input
                                className="input"
                                placeholder="port"
                                value={updateProxyPortInput}
                                onChange={(event) => setUpdateProxyPortInput(event.target.value)}
                              />
                              <input
                                className="input"
                                placeholder="login"
                                value={updateProxyLoginInput}
                                onChange={(event) => setUpdateProxyLoginInput(event.target.value)}
                              />
                              <input
                                className="input"
                                placeholder="password"
                                type="password"
                                value={updateProxyPasswordInput}
                                onChange={(event) =>
                                  setUpdateProxyPasswordInput(event.target.value)
                                }
                              />
                            </div>
                            <button
                              className="button button-primary"
                              disabled={updateProxyMutation.isPending || !effectiveAccountId}
                              onClick={() => updateProxyMutation.mutate()}
                              type="button"
                            >
                              Обновить proxy
                            </button>
                          </div>
                        ) : (
                          <p className="hint">Для роли {activeRole} скрыта операция update.</p>
                        )}
                      </section>
                    </div>
                  ) : null}

                  {activeProjectTab === "billing" ? (
                    <section className="panel">
                      <h3>Billing проекта</h3>
                      {canViewBilling ? (
                        <div className="stack">
                          <div className="grid-2">
                            <input
                              className="input"
                              value={paymentAmountInput}
                              onChange={(event) => setPaymentAmountInput(event.target.value)}
                              placeholder="amount"
                            />
                            <input
                              className="input"
                              value={paymentCurrencyInput}
                              onChange={(event) => setPaymentCurrencyInput(event.target.value)}
                              placeholder="currency"
                            />
                          </div>

                          <button
                            className="button button-primary"
                            disabled={!canChangeBilling || createPaymentMutation.isPending}
                            onClick={() => createPaymentMutation.mutate()}
                            type="button"
                          >
                            Создать платёж
                          </button>

                          <div className="inline">
                            <input
                              className="input"
                              value={planKeyInput}
                              onChange={(event) => setPlanKeyInput(event.target.value)}
                              placeholder="planKey"
                            />
                            <button
                              className="button button-primary"
                              disabled={!canChangeBilling || changePlanMutation.isPending}
                              onClick={() => changePlanMutation.mutate()}
                              type="button"
                            >
                              Сменить план
                            </button>
                          </div>

                          <div className="inline">
                            <input
                              className="input"
                              value={addonIdInput}
                              onChange={(event) => setAddonIdInput(event.target.value)}
                              placeholder="addonId"
                            />
                            <button
                              className="button button-primary"
                              disabled={!canChangeBilling || purchaseAddonMutation.isPending}
                              onClick={() => purchaseAddonMutation.mutate()}
                              type="button"
                            >
                              Купить add-on
                            </button>
                          </div>

                          {billingResult ? (
                            <pre className="pre">{JSON.stringify(billingResult, null, 2)}</pre>
                          ) : null}
                        </div>
                      ) : (
                        <p className="hint">Финансовый блок скрыт для роли {activeRole}.</p>
                      )}
                    </section>
                  ) : null}

                  {activeProjectTab === "gateway" ? (
                    <section className="panel">
                      <h3>Gateway Console</h3>
                      {canUseGateway ? (
                        <div className="stack">
                          {renderAccountSelector("Аккаунт для формирования routeKey")}
                          <div className="grid-2">
                            <input
                              className="input"
                              value={effectiveGatewayRouteKey}
                              onChange={(event) => setGatewayRouteKeyInput(event.target.value)}
                              placeholder="routeKey"
                            />
                            <input
                              className="input"
                              value={gatewayActionInput}
                              onChange={(event) => setGatewayActionInput(event.target.value)}
                              placeholder="action"
                            />
                          </div>

                          <textarea
                            className="input textarea"
                            value={gatewayPayloadInput}
                            onChange={(event) => setGatewayPayloadInput(event.target.value)}
                            placeholder='{"scope":"inventory"}'
                          />

                          <button
                            className="button button-primary"
                            disabled={gatewayMutation.isPending}
                            onClick={() => gatewayMutation.mutate()}
                            type="button"
                          >
                            Выполнить action
                          </button>

                          {gatewayResult ? (
                            <pre className="pre">{JSON.stringify(gatewayResult, null, 2)}</pre>
                          ) : null}
                        </div>
                      ) : (
                        <p className="hint">
                          Для роли {activeRole} скрыт gateway proxy (нужно право
                          `project.workers.operate`).
                        </p>
                      )}
                    </section>
                  ) : null}
                </>
              ) : (
                <section className="panel empty-state" data-testid="open-project-empty-state">
                  <h3>Откройте проект</h3>
                  <p>
                    Слева отображаются все доступные проекты. Нажмите на проект, чтобы
                    открыть его рабочее пространство с вкладками.
                  </p>
                </section>
              )}
            </div>
          </div>
        ) : null}

        {activeSection === "profile" ? (
          <div className="dashboard-grid">
            <section className="panel">
              <h3>Профиль пользователя</h3>
              <ul className="activity-list">
                <li>Имя: {session.profile.displayName}</li>
                <li>Email: {session.profile.email}</li>
                <li>User ID: {session.profile.userId}</li>
                <li>Auth mode: {session.profile.authMode}</li>
                <li>
                  Вход выполнен:{" "}
                  {new Date(session.profile.loggedInAt).toLocaleString("ru-RU")}
                </li>
              </ul>
            </section>

            <section className="panel">
              <h3>Сессия и роли</h3>
              <p className="hint">JWT subject: {jwtInfo.subject ?? "не определён"}</p>
              <p className="hint">
                JWT expires:{" "}
                {jwtInfo.expiresAt
                  ? new Date(jwtInfo.expiresAt).toLocaleString("ru-RU")
                  : "не определён"}
              </p>

              <div className="inline">
                <select
                  className="input"
                  value={roleDraft}
                  onChange={(event) => setRoleDraft(event.target.value as ProjectRole)}
                >
                  {projectRoles.map((role) => (
                    <option key={role} value={role}>
                      {role}
                    </option>
                  ))}
                </select>
                <button type="button" className="button button-primary" onClick={updateRole}>
                  Применить UI-роль
                </button>
              </div>

              <ul className="permission-list">
                {Object.values(projectPermissions).map((permission) => (
                  <li key={permission}>
                    <span>{permission}</span>
                    <strong>
                      {hasPermission(activeRole, permission) ? "allowed" : "denied"}
                    </strong>
                  </li>
                ))}
              </ul>
            </section>
          </div>
        ) : null}

        {activeSection === "activity" ? (
          <section className="panel">
            <h3>Журнал активности</h3>
            {activityLog.length === 0 ? (
              <p className="hint">Операции пока не запускались.</p>
            ) : (
              <ul className="activity-list">
                {activityLog.map((entry) => (
                  <li key={entry}>{entry}</li>
                ))}
              </ul>
            )}
          </section>
        ) : null}
      </section>
    </main>
  );
}
