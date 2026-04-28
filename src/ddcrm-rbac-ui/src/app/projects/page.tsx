"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { DashboardLayout } from "@/components/layout/dashboard-layout";
import { ThemeToggle } from "@/components/theme-toggle";
import { createProjectRequest, listProjectsRequest, type ApiSession } from "@/lib/api-client";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectsPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { session, logout } = useSessionGuard();
  const [projectName, setProjectName] = useState("");
  const [projectSearch, setProjectSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [status, setStatus] = useState("Создайте проект или откройте уже существующий.");

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

  const createProjectMutation = useMutation({
    mutationFn: () => createProjectRequest(apiSession, projectName.trim()),
    onSuccess: async (project) => {
      await queryClient.invalidateQueries({
        queryKey: ["projects", apiSession.baseUrl, apiSession.token],
      });
      setStatus("Проект создан.");
      setProjectName("");
      router.push(`/projects/${project.id}`);
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось создать проект.");
    },
  });

  if (!session) {
    return (
      <main className="loading-shell">
        <section className="glass-card">
          <h1>Проверяем сессию...</h1>
        </section>
      </main>
    );
  }

  const projects = projectsQuery.data ?? [];
  const statusOptions = Array.from(new Set(["all", ...projects.map((project) => project.status)]));
  const filteredProjects = projects.filter((project) => {
    const passesStatus = statusFilter === "all" || project.status === statusFilter;
    const query = projectSearch.trim().toLowerCase();
    const searchable = `${project.name} ${project.id} ${project.status}`.toLowerCase();
    return passesStatus && (!query || searchable.includes(query));
  });

  const activeCount = projects.filter((project) => project.status === "active").length;

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
            <Link href="/projects" className="sidebar-nav-link is-active">
              Проекты
            </Link>
            {session.profile.isSystemAdmin ? (
              <Link href="/admin/account-manager" className="sidebar-nav-link">
                Admin
              </Link>
            ) : null}
            <button type="button" className="sidebar-nav-link" onClick={logout}>
              Выйти
            </button>
          </nav>

          <ThemeToggle />

          <div className="sidebar-group">
            <p className="sidebar-kicker">Подсказка</p>
            <p className="sidebar-muted">
              Выбор проекта выполняется только на этой странице и на dashboard.
            </p>
            <p className="sidebar-muted">
              На страницах конкретного проекта левое меню показывает контекст проекта.
            </p>
          </div>
        </>
      )}
      topbar={(
        <div className="topbar-content">
          <div>
            <p className="module-page-kicker">Portfolio</p>
            <h1>Projects</h1>
          </div>
          <div className="topbar-actions">
            <span className="pill">{activeCount} active</span>
            <span className="pill pill-muted">{projects.length - activeCount} paused</span>
          </div>
        </div>
      )}
    >
      <section className="portfolio-layout">
        <section className="glass-card page-stack">
          <div className="panel-title-row">
            <h2>Портфель проектов</h2>
          </div>
          <p className="route-hint">
            Откройте проект и работайте по вкладкам: accounts, products, messages.
          </p>

          <div className="inline-filters">
            <label className="field">
              <span>Поиск</span>
              <input
                className="input"
                value={projectSearch}
                onChange={(event) => setProjectSearch(event.target.value)}
                placeholder="name / status / id"
              />
            </label>
            <label className="field">
              <span>Статус</span>
              <select
                className="input"
                value={statusFilter}
                onChange={(event) => setStatusFilter(event.target.value)}
              >
                {statusOptions.map((option) => (
                  <option key={option} value={option}>
                    {option === "all" ? "Все" : option}
                  </option>
                ))}
              </select>
            </label>
          </div>

          {projectsQuery.isPending ? <p className="route-hint">Загружаем проекты...</p> : null}
          {projectsQuery.error ? (
            <p className="route-error">
              {projectsQuery.error instanceof Error
                ? projectsQuery.error.message
                : "Не удалось получить проекты."}
            </p>
          ) : null}

          {!projectsQuery.isPending && !projectsQuery.error && filteredProjects.length === 0 ? (
            <p className="route-hint">Проекты не найдены.</p>
          ) : (
            <ul className="entity-list">
              {filteredProjects.map((project) => (
                <li key={project.id} className="entity-list-item">
                  <div>
                    <strong>{project.name}</strong>
                    <div className="entity-pills">
                      <span className="entity-pill">{project.status}</span>
                      <span className="entity-pill">{project.id}</span>
                    </div>
                  </div>
                  <div className="inline-actions">
                    <Link className="button button-primary" href={`/projects/${project.id}`}>
                      Открыть проект
                    </Link>
                    <Link className="button button-ghost" href={`/projects/${project.id}/accounts`}>
                      Аккаунты
                    </Link>
                    <Link className="button button-ghost" href={`/projects/${project.id}/products`}>
                      Товары
                    </Link>
                    <Link className="button button-ghost" href={`/projects/${project.id}/messages`}>
                      Сообщения
                    </Link>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>

        <aside className="glass-card page-stack">
          <h3>Создать проект</h3>
          <label className="field">
            <span>Название</span>
            <input
              className="input"
              value={projectName}
              onChange={(event) => setProjectName(event.target.value)}
              placeholder="Название проекта"
            />
          </label>
          <button
            type="button"
            className="button button-primary"
            disabled={createProjectMutation.isPending || !projectName.trim()}
            onClick={() => createProjectMutation.mutate()}
          >
            Создать
          </button>
          <p className="route-hint">{status}</p>
        </aside>
      </section>
    </DashboardLayout>
  );
}
