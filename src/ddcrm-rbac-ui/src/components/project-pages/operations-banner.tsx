"use client";

import type { ReactNode } from "react";

interface OperationStat {
  label: string;
  value: string;
}

interface OperationsBannerProps {
  tone: "accounts" | "products" | "messages";
  title: string;
  subtitle: string;
  stats: OperationStat[];
  actions?: ReactNode;
}

export function OperationsBanner({
  tone,
  title,
  subtitle,
  stats,
  actions,
}: OperationsBannerProps) {
  return (
    <section className={`ops-banner ops-banner-${tone}`}>
      <div className="ops-banner-main">
        <p className="workspace-eyebrow">Операционный модуль</p>
        <h2>{title}</h2>
        <p>{subtitle}</p>
      </div>

      <div className="ops-banner-side">
        <div className="ops-banner-stats">
          {stats.map((stat) => (
            <article key={`${stat.label}-${stat.value}`}>
              <span>{stat.label}</span>
              <strong>{stat.value}</strong>
            </article>
          ))}
        </div>
        {actions ? <div className="ops-banner-actions">{actions}</div> : null}
      </div>
    </section>
  );
}
