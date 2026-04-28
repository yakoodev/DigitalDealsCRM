"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { DashboardLayout } from "@/components/layout/dashboard-layout";
import { ThemeToggle } from "@/components/theme-toggle";
import { createProjectRequest, listProjectsRequest, type ApiSession } from "@/lib/api-client";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function DashboardPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { session, logout } = useSessionGuard();
  const [newProjectName, setNewProjectName] = useState("");
  const [status, setStatus] = useState("Создайте новый проект или откройте существующий.");

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
    mutationFn: () => createProjectRequest(apiSession, newProjectName.trim()),
    onSuccess: async (project) => {
      await queryClient.invalidateQueries({
        queryKey: ["projects", apiSession.baseUrl, apiSession.token],
      });
      setStatus("Проект создан.");
      setNewProjectName("");
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
  const activeProjects = projects.filter((project) => project.status === "active");
  const pausedProjects = projects.length - activeProjects.length;
  const hasProjects = projects.length > 0;

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
            <Link href="/dashboard" className="sidebar-nav-link is-active">
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
            <button type="button" className="sidebar-nav-link" onClick={logout}>
              Выйти
            </button>
          </nav>

          <ThemeToggle />

          <div className="sidebar-group">
            <p className="sidebar-kicker">Настройки</p>
            <p className="sidebar-muted">Тема, язык и профиль настраиваются из этой панели.</p>
            <p className="sidebar-muted">
              Управление проектами вынесено в центральную область dashboard.
            </p>
          </div>
        </>
      )}
      topbar={(
        <div className="topbar-content">
          <div>
            <p className="module-page-kicker">Control Center</p>
            <h1>Dashboard</h1>
          </div>
          <div className="topbar-actions">
            <Link href="/projects" className="button button-ghost">
              Открыть портфель
            </Link>
          </div>
        </div>
      )}
    >
      <section className="dashboard-grid">
        <article className="hero-card">
          <p className="module-page-kicker">Projects + Analytics</p>
          <h2>Все проекты в одном месте</h2>
          <p>
            Здесь создаются проекты, открываются рабочие разделы и проверяется сводная
            аналитика по активности.
          </p>
          <div className="hero-actions">
            <Link href="/projects" className="button button-primary">
              Перейти к проектам
            </Link>
            {hasProjects ? (
              <Link href={`/projects/${projects[0].id}`} className="button button-ghost">
                Открыть последний проект
              </Link>
            ) : null}
          </div>
        </article>

        <article className="glass-card stats-card">
          <p>Всего проектов</p>
          <strong>{projects.length}</strong>
          <small>Активные: {activeProjects.length}</small>
        </article>

        <article className="glass-card stats-card">
          <p>На паузе</p>
          <strong>{pausedProjects}</strong>
          <small>Требуют проверки</small>
        </article>

        <article className="glass-card stats-card">
          <p>Пользователь</p>
          <strong>{session.profile.displayName}</strong>
          <small>{session.profile.role}</small>
        </article>

        <section className="glass-card page-stack dashboard-create-card">
          <div className="panel-title-row">
            <h3>Создать проект</h3>
          </div>
          <label className="field">
            <span>Название проекта</span>
            <input
              className="input"
              value={newProjectName}
              onChange={(event) => setNewProjectName(event.target.value)}
              placeholder="Название проекта"
            />
          </label>
          <div className="panel-actions">
            <button
              type="button"
              className="button button-primary"
              disabled={createProjectMutation.isPending || !newProjectName.trim()}
              onClick={() => createProjectMutation.mutate()}
            >
              Создать проект
            </button>
            <p className="route-hint">{status}</p>
          </div>
        </section>

        <section className="glass-card portfolio-card">
          <div className="panel-title-row">
            <h3>Проекты</h3>
            <Link href="/projects" className="button button-ghost">
              Весь список
            </Link>
          </div>

          {projectsQuery.isPending ? <p className="route-hint">Загружаем проекты...</p> : null}
          {projectsQuery.error ? (
            <p className="route-error">
              {projectsQuery.error instanceof Error
                ? projectsQuery.error.message
                : "Не удалось получить проекты."}
            </p>
          ) : null}

          {!projectsQuery.isPending && !projectsQuery.error && projects.length === 0 ? (
            <p className="route-hint">Проектов пока нет. Создайте первый в блоке выше.</p>
          ) : (
            <ul className="entity-list compact-list">
              {projects.slice(0, 8).map((project) => (
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
                      Открыть
                    </Link>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      </section>
    </DashboardLayout>
  );
}
