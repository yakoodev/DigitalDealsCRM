"use client";

import { useQuery } from "@tanstack/react-query";
import { useMemo, type ReactNode } from "react";
import type { Project } from "@/generated/external-api";
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
import { ThemeToggle } from "@/components/theme-toggle";
import {
  listProjectsRequest,
  type ApiSession,
} from "@/lib/api-client";
import type { PlatformSession } from "@/lib/auth";

const tabMeta = {
  overview: {
    label: "Dashboard",
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
    label: "Офферы",
    hint: "Единые предложения CRM и variants",
  },
  workflows: {
    label: "Flow",
    hint: "Legacy route: объединено с Offers",
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
  const visibleTabs = useMemo(
    () => ["overview", "accounts", "products", "offers", "messages", "integrations", "steam"] as ProjectTab[],
    [],
  );
  const projectTabItems = useMemo(
    () =>
      activeProject
        ? visibleTabs.map((tab) => ({
            href: tabHref(activeProject.id, tab),
            label: tabMeta[tab].label,
            active: tab === activeTab,
          }))
        : [],
    [activeProject, activeTab, visibleTabs],
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
      header={(
        <>
          <HeaderSection align="left">
            <HeaderBrand label="DDCRM Platform" href="/projects" />
            <HeaderNav>
              <HeaderLink href="/projects" label="Dashboard" />
              {activeProject ? (
                <HeaderDropdown
                  label={activeProject.name}
                  active
                  items={projectTabItems}
                />
              ) : null}
              {session.profile.isSystemAdmin ? (
                <HeaderDropdown
                  label="Admin"
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
              <span className={`status ${activeProject?.status === "active" ? "status--ok" : "status--warn"}`}>
                {activeProject?.status ?? "no project"}
              </span>
              <HeaderMeta>
                <HeaderEmail value={session.profile.email} />
                <ThemeToggle />
                <button type="button" className="button button-ghost button-small" onClick={onLogout}>
                  Выйти
                </button>
              </HeaderMeta>
            </HeaderStatus>
          </HeaderSection>
        </>
      )}
    >
      <section className="page-wide">
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
      </section>
    </DashboardLayout>
  );
}
