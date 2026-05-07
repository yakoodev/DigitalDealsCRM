import type { ReactNode } from "react";

interface PageHeaderProps {
  eyebrow?: string;
  title: string;
  description?: string;
  actions?: ReactNode;
  meta?: ReactNode;
  compact?: boolean;
}

interface ToolbarProps {
  children: ReactNode;
  className?: string;
}

interface StatCardProps {
  label: string;
  value: string;
  hint?: string;
  tone?: "neutral" | "ok" | "warn" | "danger";
}

interface DetailRailProps {
  title: string;
  badge?: ReactNode;
  children: ReactNode;
  sticky?: boolean;
  className?: string;
}

interface EmptyStateProps {
  title: string;
  description?: string;
  actions?: ReactNode;
  className?: string;
}

interface FormSectionProps {
  title: string;
  description?: string;
  children: ReactNode;
  footer?: ReactNode;
  className?: string;
}

interface MessengerLayoutProps {
  children: ReactNode;
  className?: string;
}

interface DataTableProps {
  children: ReactNode;
  caption?: string;
  className?: string;
}

export function PageHeader({
  eyebrow,
  title,
  description,
  actions,
  meta,
  compact,
}: PageHeaderProps) {
  return (
    <header className={`page-header ${compact ? "is-compact" : ""}`.trim()}>
      <div className="page-header__copy">
        {eyebrow ? <p className="module-page-kicker">{eyebrow}</p> : null}
        <h1>{title}</h1>
        {description ? <p className="page-header__description">{description}</p> : null}
      </div>
      {actions || meta ? (
        <div className="page-header__side">
          {meta ? <div className="page-header__meta">{meta}</div> : null}
          {actions ? <div className="page-header__actions">{actions}</div> : null}
        </div>
      ) : null}
    </header>
  );
}

export function Toolbar({ children, className }: ToolbarProps) {
  return <div className={`toolbar ${className ?? ""}`.trim()}>{children}</div>;
}

export function StatCard({ label, value, hint, tone = "neutral" }: StatCardProps) {
  return (
    <article className={`stat-card is-tone-${tone}`}>
      <p>{label}</p>
      <strong>{value}</strong>
      {hint ? <small>{hint}</small> : null}
    </article>
  );
}

export function DetailRail({
  title,
  badge,
  children,
  sticky = true,
  className,
}: DetailRailProps) {
  return (
    <aside className={`detail-rail ${sticky ? "is-sticky" : ""} ${className ?? ""}`.trim()}>
      <div className="panel-title-row">
        <h3>{title}</h3>
        {badge ? <div>{badge}</div> : null}
      </div>
      {children}
    </aside>
  );
}

export function EmptyState({
  title,
  description,
  actions,
  className,
}: EmptyStateProps) {
  return (
    <section className={`empty-state-card ${className ?? ""}`.trim()}>
      <div className="empty-state-card__copy">
        <h3>{title}</h3>
        {description ? <p>{description}</p> : null}
      </div>
      {actions ? <div className="empty-state-card__actions">{actions}</div> : null}
    </section>
  );
}

export function FormSection({
  title,
  description,
  children,
  footer,
  className,
}: FormSectionProps) {
  return (
    <section className={`form-section-card ${className ?? ""}`.trim()}>
      <div className="panel-title-row">
        <div>
          <h3>{title}</h3>
          {description ? <p className="route-hint">{description}</p> : null}
        </div>
      </div>
      <div className="form-section-card__body">{children}</div>
      {footer ? <div className="form-section-card__footer">{footer}</div> : null}
    </section>
  );
}

export function MessengerLayout({ children, className }: MessengerLayoutProps) {
  return <section className={`messenger-layout ${className ?? ""}`.trim()}>{children}</section>;
}

export function DataTable({ children, caption, className }: DataTableProps) {
  return (
    <section className={`data-table-shell ${className ?? ""}`.trim()}>
      {caption ? <p className="data-table-shell__caption">{caption}</p> : null}
      {children}
    </section>
  );
}
