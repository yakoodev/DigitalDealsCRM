"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import type { Project } from "@/generated/external-api";
import {
  createProjectRequest,
  listAccountsRequest,
  listProjectsRequest,
  type ApiSession,
} from "@/lib/api-client";
import type { PlatformSession } from "@/lib/auth";

const projectTabMeta = {
  accounts: {
    label: "Аккаунты",
    hint: "Подключение площадок",
  },
  products: {
    label: "Товары",
    hint: "Каталог и публикации",
  },
  messages: {
    label: "Сообщения",
    hint: "Диалоги и ответы",
  },
  schemas: {
    label: "Схемы",
    hint: "Поля и валидация",
  },
} as const;

export type ProjectTab = keyof typeof projectTabMeta;

interface ProjectShellRenderContext {
  apiSession: ApiSession;
  project: Project;
  projects: Project[];
}

interface ProjectShellProps {
  session: PlatformSession;
  projectId: string;
  activeTab: ProjectTab;
  onLogout: () => void;
  children: (context: ProjectShellRenderContext) => ReactNode;
}

function getProjectTabHref(projectId: string, tab: ProjectTab) {
  return `/projects/${projectId}/${tab}`;
}

export function ProjectShell({
  session,
  projectId,
  activeTab,
  onLogout,
  children,
}: ProjectShellProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [newProjectName, setNewProjectName] = useState("Новый проект");
  const [projectSearch, setProjectSearch] = useState("");

  const apiSession = useMemo<ApiSession>(
    () => ({
      token: session.token,
      baseUrl: session.baseUrl,
    }),
    [session.baseUrl, session.token],
  );

  const projectsQuery = useQuery({
    queryKey: ["projects", apiSession.baseUrl, apiSession.token],
    queryFn: () => listProjectsRequest(apiSession),
  });

  const createProjectMutation = useMutation({
    mutationFn: () => createProjectRequest(apiSession, newProjectName.trim()),
    onSuccess: async (project) => {
      await queryClient.invalidateQueries({
        queryKey: ["projects", apiSession.baseUrl, apiSession.token],
      });
      setNewProjectName("Новый проект");
      router.push(getProjectTabHref(project.id, "accounts"));
    },
  });

  const projects = useMemo(() => projectsQuery.data ?? [], [projectsQuery.data]);
  const activeProject = projects.find((project) => project.id === projectId) ?? null;
  const activeProjectsCount = projects.filter((project) => project.status === "active").length;

  const filteredProjects = useMemo(() => {
    const query = projectSearch.trim().toLowerCase();
    const sorted = [...projects].sort((left, right) => {
      if (left.status === right.status) {
        return left.name.localeCompare(right.name, "ru-RU");
      }

      if (left.status === "active") {
        return -1;
      }

      if (right.status === "active") {
        return 1;
      }

      return left.status.localeCompare(right.status, "ru-RU");
    });

    if (!query) {
      return sorted;
    }

    return sorted.filter((project) => {
      const searchable = `${project.name} ${project.id} ${project.status}`.toLowerCase();
      return searchable.includes(query);
    });
  }, [projectSearch, projects]);

  const projectAccountsQuery = useQuery({
    queryKey: [
      "project-shell-accounts",
      apiSession.baseUrl,
      apiSession.token,
      activeProject?.id ?? "",
    ],
    queryFn: () => listAccountsRequest(apiSession, activeProject?.id ?? ""),
    enabled: Boolean(activeProject?.id),
    staleTime: 15_000,
  });

  const projectAccounts = projectAccountsQuery.data ?? [];
  const projectAccountsActiveCount = projectAccounts.filter(
    (account) => account.businessStatus === "active",
  ).length;

  useEffect(() => {
    if (projectsQuery.isPending || projects.length === 0 || activeProject) {
      return;
    }

    router.replace(getProjectTabHref(projects[0].id, activeTab));
  }, [activeProject, activeTab, projects, projectsQuery.isPending, router]);

  if (projectsQuery.isPending) {
    return (
      <main className="workspace-layout" data-testid="project-shell-loading">
        <section className="workspace-main-card">
          <h1>Загружаем проекты...</h1>
        </section>
      </main>
    );
  }

  if (projectsQuery.error) {
    return (
      <main className="workspace-layout" data-testid="project-shell-error">
        <section className="workspace-main-card">
          <h1>Не удалось загрузить проекты</h1>
          <p className="route-error">
            {projectsQuery.error instanceof Error
              ? projectsQuery.error.message
              : "Неизвестная ошибка."}
          </p>
        </section>
      </main>
    );
  }

  return (
    <main className="workspace-layout" data-testid="project-shell">
      <aside className="workspace-sidebar">
        <div className="brand-block">
          <p className="brand-eyebrow">DDCRM PLATFORM</p>
          <h2>Control Plane</h2>
          <p>{session.profile.displayName}</p>
          <p>{session.profile.email}</p>
        </div>

        <button type="button" className="button button-ghost" onClick={onLogout}>
          Выйти
        </button>
        <Link href="/projects" className="button button-ghost">
          Все проекты
        </Link>

        <div className="stacked-block sidebar-section">
          <h3>Новый проект</h3>
          <label className="field">
            <span>Название проекта</span>
            <input
              className="input"
              value={newProjectName}
              onChange={(event) => setNewProjectName(event.target.value)}
              placeholder="Название проекта"
            />
          </label>
          <button
            type="button"
            className="button button-primary"
            disabled={createProjectMutation.isPending || !newProjectName.trim()}
            onClick={() => createProjectMutation.mutate()}
          >
            Добавить проект
          </button>
        </div>

        <div className="stacked-block sidebar-section">
          <label className="field">
            <span>Поиск проектов</span>
            <input
              className="input"
              value={projectSearch}
              onChange={(event) => setProjectSearch(event.target.value)}
              placeholder="Название, статус, id"
            />
          </label>

          <nav className="project-nav" aria-label="Projects">
            {filteredProjects.map((project) => (
              <Link
                key={project.id}
                className={`project-nav-link ${project.id === projectId ? "is-active" : ""}`}
                href={getProjectTabHref(project.id, activeTab)}
              >
                <span>{project.name}</span>
                <small>{project.status}</small>
              </Link>
            ))}
          </nav>

          {filteredProjects.length === 0 ? (
            <p className="sidebar-hint">По вашему фильтру проекты не найдены.</p>
          ) : null}
        </div>
      </aside>

      <section className="workspace-main">
        {projects.length === 0 ? (
          <section className="workspace-main-card" data-testid="project-shell-empty">
            <h1>Проектов пока нет</h1>
            <p>Создайте первый проект слева, чтобы открыть вкладки аккаунтов и модулей.</p>
          </section>
        ) : activeProject ? (
          <>
            <header className="workspace-header workspace-header-elevated">
              <div>
                <p className="workspace-eyebrow">Проект</p>
                <h1>{activeProject.name}</h1>
                <p className="workspace-meta-line">
                  ID: {activeProject.id} · Статус: {activeProject.status}
                </p>
              </div>
              <span className="workspace-pill">{activeProject.status}</span>
            </header>

            <section className="workspace-metrics-grid" aria-label="Project metrics">
              <article className="metric-card">
                <p>Портфель</p>
                <strong>{projects.length}</strong>
                <small>Активные: {activeProjectsCount}</small>
              </article>
              <article className="metric-card">
                <p>Аккаунты проекта</p>
                <strong>
                  {projectAccountsQuery.isPending
                    ? "…"
                    : projectAccountsQuery.error
                      ? "n/a"
                      : projectAccounts.length}
                </strong>
                <small>
                  {projectAccountsQuery.error
                    ? "Не удалось получить список аккаунтов."
                    : `Активные: ${projectAccountsActiveCount}`}
                </small>
              </article>
              <article className="metric-card">
                <p>Операционный режим</p>
                <strong>{activeProject.status === "active" ? "Online" : "Paused"}</strong>
                <small>Route-driven workflow без лишних секций.</small>
              </article>
            </section>

            <nav className="tab-nav" aria-label="Project tabs">
              {(Object.keys(projectTabMeta) as ProjectTab[]).map((tab) => (
                <Link
                  key={tab}
                  href={getProjectTabHref(activeProject.id, tab)}
                  className={`tab-link ${tab === activeTab ? "is-active" : ""}`}
                >
                  <span>{projectTabMeta[tab].label}</span>
                  <small>{projectTabMeta[tab].hint}</small>
                </Link>
              ))}
            </nav>

            <section className="workspace-main-card">
              {children({
                apiSession,
                project: activeProject,
                projects,
              })}
            </section>
          </>
        ) : (
          <section className="workspace-main-card" data-testid="project-shell-redirecting">
            <h1>Открываем проект...</h1>
          </section>
        )}
      </section>
    </main>
  );
}
