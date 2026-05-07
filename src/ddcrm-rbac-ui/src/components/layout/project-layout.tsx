"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useMemo, type ReactNode } from "react";
import type { Project } from "@/generated/external-api";
import { DashboardLayout } from "@/components/layout/dashboard-layout";
import { ThemeToggle } from "@/components/theme-toggle";
import {
  listAccountsRequest,
  listProjectsRequest,
  type ApiSession,
} from "@/lib/api-client";
import type { PlatformSession } from "@/lib/auth";

const tabMeta = {
  overview: {
    label: "Обзор",
    hint: "Статистика и ключевые действия",
  },
  accounts: {
    label: "Аккаунты",
    hint: "Подключения и lifecycle",
  },
  products: {
    label: "Товары",
    hint: "Каталог и изменения",
  },
  offers: {
    label: "Offers",
    hint: "Единые предложения CRM и variants",
  },
  workflows: {
    label: "Workflows",
    hint: "Draft/publish и история исполнения блок-схем",
  },
  messages: {
    label: "Сообщения",
    hint: "Переписки и ответы",
  },
  integrations: {
    label: "Интеграции",
    hint: "Grant-ы, runtime worker и invoke read/jobs",
  },
  steam: {
    label: "Steam",
    hint: "Управление Steam аккаунтами и jobs интеграции",
  },
} as const;

export type ProjectTab = keyof typeof tabMeta;

interface ProjectLayoutContext {
  apiSession: ApiSession;
  project: Project;
  projects: Project[];
}

interface ProjectLayoutProps {
  session: PlatformSession;
  projectId: string;
  activeTab: ProjectTab;
  onLogout: () => void;
  children: (context: ProjectLayoutContext) => ReactNode;
}

function tabHref(projectId: string, tab: ProjectTab) {
  if (tab === "overview") {
    return `/projects/${projectId}`;
  }

  return `/projects/${projectId}/${tab}`;
}

export function ProjectLayout({
  session,
  projectId,
  activeTab,
  onLogout,
  children,
}: ProjectLayoutProps) {
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

  const projects = useMemo(() => projectsQuery.data ?? [], [projectsQuery.data]);
  const activeProject = projects.find((project) => project.id === projectId) ?? null;

  const projectAccountsQuery = useQuery({
    queryKey: [
      "project-layout-accounts",
      apiSession.baseUrl,
      apiSession.token,
      activeProject?.id ?? "",
    ] as const,
    queryFn: () => listAccountsRequest(apiSession, activeProject?.id ?? ""),
    enabled: Boolean(activeProject?.id),
    staleTime: 15_000,
  });

  const projectAccounts = projectAccountsQuery.data ?? [];
  const activeAccountsCount = projectAccounts.filter(
    (account) => account.businessStatus === "active",
  ).length;
  const pausedAccountsCount = projectAccounts.length - activeAccountsCount;
  const visibleTabs = useMemo(
    () => Object.keys(tabMeta) as ProjectTab[],
    [],
  );

  if (projectsQuery.isPending) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>Загружаем проекты...</h1>
        </section>
      </main>
    );
  }

  if (projectsQuery.error) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
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
    <DashboardLayout
      sidebar={(
        <>
          <div className="sidebar-group">
            <p className="sidebar-kicker">DDCRM Platform</p>
            <h2>{session.profile.displayName}</h2>
            <p className="sidebar-muted">{session.profile.email}</p>
            <p className="sidebar-muted">Role: {session.profile.role}</p>
          </div>

          <nav className="sidebar-nav">
            <Link href="/dashboard" className="sidebar-nav-link">
              Dashboard
            </Link>
            <Link href="/projects" className="sidebar-nav-link">
              Проекты
            </Link>
            {session.profile.isSystemAdmin ? (
              <Link href="/admin/account-manager" className="sidebar-nav-link">
                Admin
              </Link>
            ) : null}
            <button type="button" className="sidebar-nav-link" onClick={onLogout}>
              Выйти
            </button>
          </nav>

          <ThemeToggle />

          {activeProject ? (
            <>
              <div className="sidebar-group">
                <h3>{activeProject.name}</h3>
                <p className="sidebar-muted">Status: {activeProject.status}</p>
                <p className="sidebar-muted">ID: {activeProject.id}</p>
              </div>

              <div className="sidebar-group">
                <p className="sidebar-kicker">Раздел проекта</p>
                <nav className="sidebar-nav">
                  {visibleTabs.map((tab) => (
                    <Link
                      key={tab}
                      href={tabHref(activeProject.id, tab)}
                      className={`sidebar-nav-link ${tab === activeTab ? "is-active" : ""}`}
                    >
                      {tabMeta[tab].label}
                    </Link>
                  ))}
                </nav>
                <p className="sidebar-muted">{tabMeta[activeTab].hint}</p>
              </div>

              <div className="sidebar-group">
                <p className="sidebar-kicker">Статистика проекта</p>
                {projectAccountsQuery.isPending ? (
                  <p className="sidebar-muted">Считаем аккаунты...</p>
                ) : projectAccountsQuery.error ? (
                  <p className="route-error">
                    {projectAccountsQuery.error instanceof Error
                      ? projectAccountsQuery.error.message
                      : "Не удалось получить статистику аккаунтов."}
                  </p>
                ) : (
                  <>
                    <p className="sidebar-muted">Аккаунтов: {projectAccounts.length}</p>
                    <p className="sidebar-muted">Активные: {activeAccountsCount}</p>
                    <p className="sidebar-muted">Неактивные: {pausedAccountsCount}</p>
                  </>
                )}
              </div>

              <div className="sidebar-group">
                <p className="sidebar-kicker">Контекст</p>
                <p className="sidebar-muted">
                  Подробности по операциям и техническим данным доступны в `Details`
                  внутри активного раздела.
                </p>
              </div>
            </>
          ) : (
            <div className="sidebar-group">
              <p className="sidebar-muted">Проект не выбран. Откройте проект из портфеля.</p>
            </div>
          )}
        </>
      )}
      topbar={(
        activeProject ? (
          <div className="topbar-content">
            <div>
              <p className="module-page-kicker">Project Workspace</p>
              <h1>{activeProject.name}</h1>
              <p>
                {tabMeta[activeTab].label} · {tabMeta[activeTab].hint}
              </p>
            </div>
            <div className="topbar-actions">
              <Link href="/projects" className="button button-ghost">
                К портфелю
              </Link>
            </div>
          </div>
        ) : (
          <div className="topbar-content">
            <div>
              <p className="module-page-kicker">Project Workspace</p>
              <h1>Project not selected</h1>
            </div>
          </div>
        )
      )}
    >
      {activeProject ? (
        children({
          apiSession,
          project: activeProject,
          projects,
        })
      ) : (
        <section className="glass-card">
          {projects.length === 0 ? (
            <>
              <h1>Проектов пока нет</h1>
              <p>Создайте проект на странице `/dashboard` или `/projects`.</p>
            </>
          ) : (
            <>
              <h1>Открываем проект...</h1>
              <p>Выберите проект на странице `/projects`.</p>
            </>
          )}
        </section>
      )}
    </DashboardLayout>
  );
}
