"use client";

import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import type { Account } from "@/generated/external-api";
import {
  HeaderBrand,
  HeaderDropdown,
  HeaderEmail,
  HeaderMeta,
  HeaderLink,
  HeaderNav,
  HeaderSection,
  HeaderStatus,
} from "@/components/layout/app-header-primitives";
import { DashboardLayout } from "@/components/layout/dashboard-layout";
import { RouteModalHost } from "@/components/layout/route-modal-host";
import { ThemeToggle } from "@/components/theme-toggle";
import {
  createProjectRequest,
  listAccountsRequest,
  listProjectIntegrationsStatusRequest,
  listProjectsRequest,
  type ApiSession,
  updateProjectRequest,
} from "@/lib/api-client";
import { useSessionGuard } from "@/lib/use-session-guard";

type ProjectSort = "activity" | "name" | "status";

interface ProjectStats {
  totalAccounts: number;
  activeAccounts: number;
  warningAccounts: number;
  platforms: string[];
}

interface ProjectRow {
  id: string;
  name: string;
  status: string;
  stats: ProjectStats;
}

function toStatusTone(status: string) {
  const normalized = status.trim().toLowerCase();
  if (normalized.includes("active") || normalized.includes("ok")) {
    return "status--ok";
  }

  if (normalized.includes("pause") || normalized.includes("draft")) {
    return "status--info";
  }

  if (normalized.includes("warn") || normalized.includes("sync")) {
    return "status--warn";
  }

  return "status--bad";
}

function readProjectStats(accounts: Account[]): ProjectStats {
  const platforms = Array.from(new Set(accounts.map((account) => account.platform))).sort((a, b) => a.localeCompare(b));
  const activeAccounts = accounts.filter((account) => account.businessStatus.toLowerCase() === "active").length;
  const warningAccounts = accounts.length - activeAccounts;

  return {
    totalAccounts: accounts.length,
    activeAccounts,
    warningAccounts,
    platforms,
  };
}

export function ProjectsHubPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { session, logout } = useSessionGuard();

  const [projectSearch, setProjectSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [platformFilter, setPlatformFilter] = useState("all");
  const [sortBy, setSortBy] = useState<ProjectSort>("activity");
  const [statusMessage, setStatusMessage] = useState("Откройте существующий проект или создайте новый.");

  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [newProjectName, setNewProjectName] = useState("");
  const [newProjectType, setNewProjectType] = useState("Продажа цифровых товаров");
  const [newProjectComment, setNewProjectComment] = useState("");

  const [editModalProjectId, setEditModalProjectId] = useState("");
  const [editProjectName, setEditProjectName] = useState("");
  const [editProjectStatus, setEditProjectStatus] = useState("active");

  const apiSession = useMemo<ApiSession>(
    () => ({
      token: session?.token ?? "",
      baseUrl: session?.baseUrl ?? "",
    }),
    [session?.baseUrl, session?.token],
  );

  const projectsQuery = useQuery({
    queryKey: ["projects", apiSession.baseUrl, apiSession.token],
    queryFn: () => listProjectsRequest(apiSession),
    enabled: Boolean(session),
  });

  const projects = useMemo(() => projectsQuery.data ?? [], [projectsQuery.data]);
  const projectIds = useMemo(() => projects.map((project) => project.id), [projects]);

  const accountsQueries = useQueries({
    queries: projectIds.map((projectId) => ({
      queryKey: ["accounts", apiSession.baseUrl, apiSession.token, projectId] as const,
      queryFn: () => listAccountsRequest(apiSession, projectId),
      enabled: Boolean(session) && projectId.length > 0,
      staleTime: 20_000,
    })),
  });

  const integrationsQueries = useQueries({
    queries: projectIds.map((projectId) => ({
      queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId] as const,
      queryFn: () => listProjectIntegrationsStatusRequest(apiSession, projectId),
      enabled: Boolean(session) && projectId.length > 0,
      staleTime: 20_000,
    })),
  });

  const projectRows = useMemo<ProjectRow[]>(() => {
    return projects.map((project, index) => {
      const rawAccounts = accountsQueries[index]?.data ?? [];
      const integrationItems = integrationsQueries[index]?.data?.items ?? [];
      const runtimeAccountIds = new Set<string>();
      const integrationPlatformHints = new Set<string>();

      for (const item of integrationItems) {
        if (item.runtimeAccountId?.trim()) {
          runtimeAccountIds.add(item.runtimeAccountId.trim());
        }

        if (item.integrationType !== "worker") {
          continue;
        }

        const parts = item.integrationKey
          .trim()
          .toLowerCase()
          .split(/[^a-z0-9]+/g)
          .filter((part) => part.length > 0);

        if (parts.length > 0) {
          integrationPlatformHints.add(parts[0]);
        }
      }

      const accounts = rawAccounts.filter((account) => {
        if (runtimeAccountIds.has(account.id)) {
          return false;
        }

        const normalizedPlatform = account.platform.trim().toLowerCase();
        if (!normalizedPlatform) {
          return true;
        }

        return !integrationPlatformHints.has(normalizedPlatform);
      });

      return {
        id: project.id,
        name: project.name,
        status: project.status,
        stats: readProjectStats(accounts),
      };
    });
  }, [accountsQueries, integrationsQueries, projects]);

  const statusOptions = useMemo(
    () => Array.from(new Set(["all", ...projectRows.map((row) => row.status.toLowerCase())])),
    [projectRows],
  );
  const platformOptions = useMemo(
    () => Array.from(new Set(projectRows.flatMap((row) => row.stats.platforms).map((platform) => platform.toLowerCase()))).sort((a, b) => a.localeCompare(b)),
    [projectRows],
  );

  const filteredRows = useMemo(() => {
    const query = projectSearch.trim().toLowerCase();
    const scoped = projectRows.filter((row) => {
      const rowStatus = row.status.toLowerCase();
      const rowPlatforms = row.stats.platforms.map((platform) => platform.toLowerCase());

      if (statusFilter !== "all" && rowStatus !== statusFilter) {
        return false;
      }

      if (platformFilter !== "all" && !rowPlatforms.includes(platformFilter)) {
        return false;
      }

      if (!query) {
        return true;
      }

      const searchable = [
        row.name,
        row.id,
        row.status,
        row.stats.platforms.join(" "),
      ].join(" ").toLowerCase();
      return searchable.includes(query);
    });

    const sorted = [...scoped];
    if (sortBy === "name") {
      sorted.sort((left, right) => left.name.localeCompare(right.name));
      return sorted;
    }

    if (sortBy === "status") {
      sorted.sort((left, right) => left.status.localeCompare(right.status) || left.name.localeCompare(right.name));
      return sorted;
    }

    sorted.sort((left, right) => {
      const leftActivity = left.stats.activeAccounts - left.stats.warningAccounts;
      const rightActivity = right.stats.activeAccounts - right.stats.warningAccounts;
      return rightActivity - leftActivity || right.stats.totalAccounts - left.stats.totalAccounts || left.name.localeCompare(right.name);
    });
    return sorted;
  }, [platformFilter, projectRows, projectSearch, sortBy, statusFilter]);

  const activeProjectsCount = projectRows.filter((row) => row.status.toLowerCase() === "active").length;
  const totalAccountsCount = projectRows.reduce((sum, row) => sum + row.stats.totalAccounts, 0);
  const totalWarningsCount = projectRows.reduce((sum, row) => sum + row.stats.warningAccounts, 0);
  const totalPlatformsCount = new Set(projectRows.flatMap((row) => row.stats.platforms.map((platform) => platform.toLowerCase()))).size;
  const anyAccountsPending = accountsQueries.some((query) => query.isPending);

  const createProjectMutation = useMutation({
    mutationFn: () => createProjectRequest(apiSession, newProjectName.trim()),
    onSuccess: async (project) => {
      await queryClient.invalidateQueries({
        queryKey: ["projects", apiSession.baseUrl, apiSession.token],
      });
      setCreateModalOpen(false);
      setNewProjectName("");
      setNewProjectType("Продажа цифровых товаров");
      setNewProjectComment("");
      setStatusMessage("Проект создан.");
      router.push(`/projects/${project.id}`);
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось создать проект.");
    },
  });

  const updateProjectMutation = useMutation({
    mutationFn: () => {
      if (!editModalProjectId.trim()) {
        throw new Error("Не выбран проект для редактирования.");
      }

      return updateProjectRequest(apiSession, editModalProjectId.trim(), {
        name: editProjectName.trim(),
        status: editProjectStatus.trim(),
      });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["projects", apiSession.baseUrl, apiSession.token],
      });
      setEditModalProjectId("");
      setStatusMessage("Проект обновлён.");
    },
    onError: (error) => {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось обновить проект.");
    },
  });

  const openEditModal = (row: ProjectRow) => {
    setEditModalProjectId(row.id);
    setEditProjectName(row.name);
    setEditProjectStatus(row.status.toLowerCase());
  };

  if (!session) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>Проверяем сессию...</h1>
        </section>
      </main>
    );
  }

  return (
    <DashboardLayout
      header={(
        <>
          <HeaderSection align="left">
            <HeaderBrand label="DDCRM Platform" href="/projects" />
            <HeaderNav>
              <HeaderLink href="/projects" label="Dashboard" active />
              {session.profile.isSystemAdmin ? (
                <HeaderDropdown
                  label="Admin"
                  active={false}
                  items={[
                    { href: "/admin/account-manager", label: "Обзор" },
                    { href: "/admin/account-manager/servers", label: "Worker servers" },
                    { href: "/admin/account-manager/templates", label: "Templates" },
                    { href: "/admin/account-manager/integrations", label: "Integrations" },
                  ]}
                />
              ) : null}
            </HeaderNav>
          </HeaderSection>
          <HeaderSection align="right">
            <HeaderStatus>
              <span className="status status--ok">{activeProjectsCount} active</span>
              <HeaderMeta>
                <HeaderEmail value={session.profile.email} />
                <ThemeToggle />
                <button type="button" className="button button-ghost button-small" onClick={logout}>
                  Выйти
                </button>
              </HeaderMeta>
            </HeaderStatus>
          </HeaderSection>
        </>
      )}
    >
      <section className="page-head">
        <div className="page-head__row">
          <div>
            <h1 className="page-title">Dashboard</h1>
          </div>
          <div className="inline">
            <button type="button" className="button button-primary" onClick={() => setCreateModalOpen(true)}>
              + Создать проект
            </button>
          </div>
        </div>
        <div className={`status ${statusMessage.toLowerCase().includes("не удалось") ? "status--bad" : "status--info"}`}>
          {statusMessage}
        </div>
      </section>

      <section className="kpi-grid">
        <article className="kpi">
          <div className="card__meta">Проекты</div>
          <strong>{projectRows.length}</strong>
          <div className="text-small text-muted">{activeProjectsCount} активных</div>
        </article>
        <article className="kpi">
          <div className="card__meta">Аккаунты</div>
          <strong>{totalAccountsCount}</strong>
          <div className="text-small text-muted">{totalWarningsCount} требуют проверки</div>
        </article>
        <article className="kpi">
          <div className="card__meta">Площадки</div>
          <strong>{totalPlatformsCount}</strong>
          <div className="text-small text-muted">в активном портфеле</div>
        </article>
        <article className="kpi">
          <div className="card__meta">Роль</div>
          <strong>{session.profile.role}</strong>
          <div className="text-small text-muted">{session.profile.displayName}</div>
        </article>
      </section>

      <section className="toolbar">
        <div className="field field-grow">
          <label htmlFor="project-search">Поиск</label>
          <input
            id="project-search"
            className="input"
            value={projectSearch}
            onChange={(event) => setProjectSearch(event.target.value)}
            placeholder="Название, id, площадка"
          />
        </div>
        <div className="field">
          <label htmlFor="project-status-filter">Статус</label>
          <select
            id="project-status-filter"
            className="input"
            value={statusFilter}
            onChange={(event) => setStatusFilter(event.target.value)}
          >
            {statusOptions.map((option) => (
              <option key={option} value={option}>
                {option === "all" ? "Все статусы" : option}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="project-platform-filter">Площадка</label>
          <select
            id="project-platform-filter"
            className="input"
            value={platformFilter}
            onChange={(event) => setPlatformFilter(event.target.value)}
          >
            <option value="all">Все площадки</option>
            {platformOptions.map((platform) => (
              <option key={platform} value={platform}>
                {platform}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="project-sort">Сортировка</label>
          <select
            id="project-sort"
            className="input"
            value={sortBy}
            onChange={(event) => setSortBy(event.target.value as ProjectSort)}
          >
            <option value="activity">По активности</option>
            <option value="name">По названию</option>
            <option value="status">По статусу</option>
          </select>
        </div>
      </section>

      {projectsQuery.isPending ? <p className="route-hint">Загружаем проекты...</p> : null}
      {projectsQuery.error ? (
        <p className="route-error">
          {projectsQuery.error instanceof Error ? projectsQuery.error.message : "Не удалось загрузить проекты."}
        </p>
      ) : null}
      {anyAccountsPending ? <p className="route-hint">Подтягиваем данные по аккаунтам...</p> : null}

      {!projectsQuery.isPending && !projectsQuery.error && filteredRows.length === 0 ? (
        <section className="empty-state">
          Проекты не найдены. Измените фильтры или создайте новый проект.
        </section>
      ) : null}

      {filteredRows.length > 0 ? (
        <section className="project-list">
          {filteredRows.map((row) => (
            <article key={row.id} className="card project-card">
              <div className="card__head">
                <div className="min-w-0">
                  <div className="inline mb-2">
                    <h2 className="card__title">{row.name}</h2>
                    <span className={`status ${toStatusTone(row.status)}`}>{row.status}</span>
                  </div>
                  <div className="card__meta">{row.id}</div>
                </div>
                <div className="inline text-nowrap">
                  <Link className="button button-small button-primary" href={`/projects/${row.id}`}>
                    Открыть
                  </Link>
                  <button
                    type="button"
                    className="button button-small button-ghost"
                    onClick={() => openEditModal(row)}
                  >
                    Редактировать
                  </button>
                </div>
              </div>
              <div className="project-card__metrics">
                <div className="metric-box"><b>{row.stats.totalAccounts}</b><span className="text-small text-muted">аккаунтов</span></div>
                <div className="metric-box"><b>{row.stats.activeAccounts}</b><span className="text-small text-muted">online</span></div>
                <div className="metric-box"><b>{row.stats.warningAccounts}</b><span className="text-small text-muted">warning</span></div>
                <div className="metric-box"><b>{row.stats.platforms.length}</b><span className="text-small text-muted">площадок</span></div>
                <div className="metric-box"><b>{Math.max(1, row.stats.activeAccounts)}</b><span className="text-small text-muted">воркеров</span></div>
                <div className="metric-box"><b>{row.status.toLowerCase() === "active" ? "ok" : "hold"}</b><span className="text-small text-muted">сервисы</span></div>
              </div>
              <div className="project-card__footer">
                <div className="inline">
                  {row.stats.platforms.length === 0 ? <span className="badge">no accounts</span> : null}
                  {row.stats.platforms.slice(0, 4).map((platform) => (
                    <span key={`${row.id}-${platform}`} className="badge">{platform}</span>
                  ))}
                </div>
                <div className="inline text-small">
                  <span className={`status ${toStatusTone(row.status)}`}>
                    {row.stats.warningAccounts === 0 ? "Все сервисы работают" : "Есть предупреждения"}
                  </span>
                  <span className="text-muted">Команда: {Math.max(1, row.stats.activeAccounts)}</span>
                </div>
              </div>
            </article>
          ))}
        </section>
      ) : null}

      <RouteModalHost
        isOpen={createModalOpen}
        title="Создать проект"
        description="Создание нового проекта и переход в его рабочее пространство."
        onClose={() => setCreateModalOpen(false)}
      >
        <section className="page-stack">
          <label className="field">
            <span>Название</span>
            <input
              className="input"
              value={newProjectName}
              onChange={(event) => setNewProjectName(event.target.value)}
              placeholder="Например: Yakoo Store"
            />
          </label>
          <label className="field">
            <span>Тип проекта</span>
            <select
              className="input"
              value={newProjectType}
              onChange={(event) => setNewProjectType(event.target.value)}
            >
              <option>Продажа цифровых товаров</option>
              <option>Тестовый проект</option>
              <option>Архивный проект</option>
            </select>
          </label>
          <label className="field">
            <span>Комментарий</span>
            <textarea
              className="input textarea"
              value={newProjectComment}
              onChange={(event) => setNewProjectComment(event.target.value)}
              placeholder="Коротко для команды"
            />
          </label>
          <div className="inline">
            <button
              type="button"
              className="button button-primary"
              disabled={createProjectMutation.isPending || !newProjectName.trim()}
              onClick={() => createProjectMutation.mutate()}
            >
              Создать
            </button>
            <button type="button" className="button button-ghost" onClick={() => setCreateModalOpen(false)}>
              Отмена
            </button>
          </div>
        </section>
      </RouteModalHost>

      <RouteModalHost
        isOpen={editModalProjectId.length > 0}
        title="Редактировать проект"
        description="Измените имя/статус проекта."
        onClose={() => setEditModalProjectId("")}
      >
        <section className="page-stack">
          <label className="field">
            <span>Название</span>
            <input
              className="input"
              value={editProjectName}
              onChange={(event) => setEditProjectName(event.target.value)}
            />
          </label>
          <label className="field">
            <span>Статус</span>
            <select
              className="input"
              value={editProjectStatus}
              onChange={(event) => setEditProjectStatus(event.target.value)}
            >
              <option value="active">active</option>
              <option value="paused">paused</option>
              <option value="archived">archived</option>
            </select>
          </label>
          <div className="inline">
            <button
              type="button"
              className="button button-primary"
              disabled={updateProjectMutation.isPending || !editProjectName.trim()}
              onClick={() => updateProjectMutation.mutate()}
            >
              Сохранить
            </button>
            <button type="button" className="button button-ghost" onClick={() => setEditModalProjectId("")}>
              Отмена
            </button>
          </div>
        </section>
      </RouteModalHost>
    </DashboardLayout>
  );
}
