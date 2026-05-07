import type { ReactNode } from "react";
import { PageHeader, StatCard } from "@/components/ui/page-primitives";

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
      <PageHeader
        eyebrow="Project Module"
        title={title}
        description={description}
        actions={actions}
      />

      {stats && stats.length > 0 ? (
        <section className="module-page-stats">
          {stats.map((item) => (
            <StatCard
              key={item.label}
              label={item.label}
              value={item.value}
              hint={item.hint}
            />
          ))}
        </section>
      ) : null}

      <section className="module-grid">
        <section className="module-main">{main}</section>
        <aside className="module-side-shell">{side}</aside>
      </section>
    </section>
  );
}
