"use client";

import { useEffect, type ReactNode } from "react";

interface RouteModalHostProps {
  isOpen: boolean;
  title: string;
  description?: string;
  onClose: () => void;
  children: ReactNode;
}

export function RouteModalHost({
  isOpen,
  title,
  description,
  onClose,
  children,
}: RouteModalHostProps) {
  useEffect(() => {
    if (!isOpen) {
      return undefined;
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [isOpen, onClose]);

  if (!isOpen) {
    return null;
  }

  return (
    <section className="route-modal-layer" role="presentation">
      <button
        type="button"
        className="route-modal-backdrop"
        aria-label="Закрыть модальное окно"
        onClick={onClose}
      />

      <article className="route-modal" role="dialog" aria-modal="true" aria-label={title}>
        <header className="route-modal-header">
          <div>
            <h3>{title}</h3>
            {description ? <p>{description}</p> : null}
          </div>
          <button type="button" className="button button-ghost" onClick={onClose}>
            Закрыть
          </button>
        </header>
        <section className="route-modal-content">{children}</section>
      </article>
    </section>
  );
}
