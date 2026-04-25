"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { createProjectRequest, listProjectsRequest, type ApiSession } from "@/lib/api-client";
import { useSessionGuard } from "@/lib/use-session-guard";

export default function ProjectsPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { session, logout } = useSessionGuard();
  const [projectName, setProjectName] = useState("Новый проект");
  const [projectSearch, setProjectSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [status, setStatus] = useState("Выберите проект или создайте новый.");

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
      setProjectName("Новый проект");
      router.push(`/projects/${project.id}/accounts`);
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось создать проект.");
    },
  });

  const projects = useMemo(() => {
    const data = projectsQuery.data ?? [];
    return [...data].sort((left, right) => left.name.localeCompare(right.name, "ru-RU"));
  }, [projectsQuery.data]);

  const statusOptions = useMemo(() => {
    const options = new Set<string>(["all"]);
    for (const project of projects) {
      options.add(project.status);
    }

    return [...options];
  }, [projects]);

  const filteredProjects = useMemo(() => {
    const query = projectSearch.trim().toLowerCase();
    return projects.filter((project) => {
      const passesStatus = statusFilter === "all" || project.status === statusFilter;
      const searchable = `${project.name} ${project.id} ${project.status}`.toLowerCase();
      const passesSearch = !query || searchable.includes(query);
      return passesStatus && passesSearch;
    });
  }, [projectSearch, projects, statusFilter]);

  const activeCount = projects.filter((project) => project.status === "active").length;
  const maintenanceCount = projects.filter((project) => project.status !== "active").length;

  if (!session) {
    return (
      <main className="workspace-layout">
        <section className="workspace-main-card">
          <h1>Проверяем сессию...</h1>
        </section>
      </main>
    );
  }

  return (
    <main className="workspace-layout" data-testid="projects-page">
      <aside className="workspace-sidebar">
        <div className="brand-block">
          <p className="brand-eyebrow">DDCRM PLATFORM</p>
          <h2>Проекты</h2>
          <p>{session.profile.displayName}</p>
          <p>{session.profile.email}</p>
        </div>
        <button type="button" className="button button-ghost" onClick={logout}>
          Выйти
        </button>
      </aside>

      <section className="workspace-main">
        <header className="workspace-header">
          <div>
            <p className="workspace-eyebrow">Рабочая область</p>
            <h1>Портфель проектов</h1>
            <p className="workspace-meta-line">
              Сначала выбираем проект, затем работаем по route: аккаунты, товары, сообщения и схемы.
            </p>
          </div>
        </header>

        <section className="workspace-metrics-grid">
          <article className="metric-card">
            <p>Всего проектов</p>
            <strong>{projects.length}</strong>
            <small>В портфеле платформы</small>
          </article>
          <article className="metric-card">
            <p>Активные</p>
            <strong>{activeCount}</strong>
            <small>Готовы к рабочим операциям</small>
          </article>
          <article className="metric-card">
            <p>На паузе</p>
            <strong>{maintenanceCount}</strong>
            <small>Требуют проверки статуса</small>
          </article>
        </section>

        <section className="workspace-main-card">
          <div className="split-grid">
            <section className="panel-card">
              <h3>Все проекты</h3>
              <div className="inline-filters">
                <label className="field">
                  <span>Поиск</span>
                  <input
                    className="input"
                    value={projectSearch}
                    onChange={(event) => setProjectSearch(event.target.value)}
                    placeholder="Название, статус, id"
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
                        {option === "all" ? "Все статусы" : option}
                      </option>
                    ))}
                  </select>
                </label>
              </div>

              {projectsQuery.isPending ? (
                <p className="route-hint">Загружаем проекты...</p>
              ) : projectsQuery.error ? (
                <p className="route-error">
                  {projectsQuery.error instanceof Error
                    ? projectsQuery.error.message
                    : "Не удалось получить проекты."}
                </p>
              ) : projects.length === 0 ? (
                <p className="route-hint">
                  Проектов пока нет. Создайте первый проект в соседнем блоке.
                </p>
              ) : filteredProjects.length === 0 ? (
                <p className="route-hint">По выбранным фильтрам проекты не найдены.</p>
              ) : (
                <ul className="entity-list">
                  {filteredProjects.map((project) => (
                    <li key={project.id} className="entity-list-item">
                      <div>
                        <strong>{project.name}</strong>
                        <p>{project.status}</p>
                        <small>{project.id}</small>
                      </div>
                      <Link
                        className="button button-primary"
                        href={`/projects/${project.id}/accounts`}
                      >
                        Открыть
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <section className="panel-card page-stack">
              <div>
                <h3>Создать проект</h3>
                <p className="route-hint">
                  Новый проект сразу откроется на вкладке аккаунтов, чтобы можно было подключить площадку.
                </p>
              </div>
              <div className="stacked-block">
                <label className="field">
                  <span>Название проекта</span>
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
                  Создать проект
                </button>
                <p className="route-hint">{status}</p>
              </div>

              <div className="panel-card panel-soft">
                <h3>Операционный фокус</h3>
                <p className="route-hint">
                  После создания проекта пройдите базовую цепочку: аккаунт → товары → сообщения → схемы.
                </p>
                <ul className="entity-list compact-list">
                  <li className="entity-list-item">
                    <div>
                      <strong>1. Подключите аккаунт площадки</strong>
                      <p>Proxy + display name + платформа</p>
                    </div>
                  </li>
                  <li className="entity-list-item">
                    <div>
                      <strong>2. Проверьте загрузку товаров</strong>
                      <p>Список должен подтянуться автоматически</p>
                    </div>
                  </li>
                  <li className="entity-list-item">
                    <div>
                      <strong>3. Откройте сообщения</strong>
                      <p>Сначала список переписок, затем выбранный чат</p>
                    </div>
                  </li>
                </ul>
              </div>
            </section>
          </div>
        </section>
      </section>
    </main>
  );
}
