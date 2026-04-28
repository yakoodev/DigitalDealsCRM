import type { ReactNode } from "react";

interface ModuleStat {
  label: string;
  value: string;
  hint?: string;
}

interface ModulePageShellProps {
  title: string;
  description: string;
  actions?: ReactNode;
  stats?: ModuleStat[];
  main: ReactNode;
  side: ReactNode;
}

export function ModulePageShell({
  title,
  description,
  actions,
  stats,
  main,
  side,
}: ModulePageShellProps) {
  return (
    <section className="module-page-shell">
      <header className="module-page-header">
        <div>
          <p className="module-page-kicker">Project Module</p>
          <h2>{title}</h2>
          <p>{description}</p>
        </div>
        {actions ? <div className="module-page-actions">{actions}</div> : null}
      </header>

      {stats && stats.length > 0 ? (
        <section className="module-page-stats">
          {stats.map((item) => (
            <article key={item.label} className="metric-tile">
              <p>{item.label}</p>
              <strong>{item.value}</strong>
              {item.hint ? <small>{item.hint}</small> : null}
            </article>
          ))}
        </section>
      ) : null}

      <section className="module-grid">
        <section className="module-main">{main}</section>
        <aside className="module-side">{side}</aside>
      </section>
    </section>
  );
}
