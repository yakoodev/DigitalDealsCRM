"use client";

import Link from "next/link";
import {
  useEffect,
  useId,
  useRef,
  useState,
  type ReactNode,
} from "react";

export interface HeaderMenuItem {
  href: string;
  label: string;
  active?: boolean;
}

interface HeaderBrandProps {
  label: string;
  href: string;
}

interface HeaderNavProps {
  children: ReactNode;
}

interface HeaderSectionProps {
  children: ReactNode;
  align?: "left" | "right";
}

interface HeaderDropdownProps {
  label: string;
  items: HeaderMenuItem[];
  align?: "left" | "right";
  active?: boolean;
}

interface HeaderLinkProps {
  href: string;
  label: string;
  active?: boolean;
}

interface HeaderStatusProps {
  children: ReactNode;
}

interface HeaderMetaProps {
  children: ReactNode;
}

interface HeaderEmailProps {
  value: string;
}

export function HeaderBrand({ label, href }: HeaderBrandProps) {
  return (
    <Link href={href} className="app-header__brand">
      {label}
    </Link>
  );
}

export function HeaderNav({ children }: HeaderNavProps) {
  return (
    <nav aria-label="Основная навигация" className="app-header__nav">
      {children}
    </nav>
  );
}

export function HeaderSection({ children, align = "left" }: HeaderSectionProps) {
  return (
    <div className={`app-header__section app-header__section--${align}`.trim()}>
      {children}
    </div>
  );
}

export function HeaderDropdown({
  label,
  items,
  align = "left",
  active = false,
}: HeaderDropdownProps) {
  const [isOpen, setIsOpen] = useState(false);
  const anchorRef = useRef<HTMLDivElement | null>(null);
  const menuId = useId();

  useEffect(() => {
    if (!isOpen) {
      return undefined;
    }

    const handleOutsideClick = (event: Event) => {
      if (!anchorRef.current) {
        return;
      }

      if (!anchorRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setIsOpen(false);
      }
    };

    document.addEventListener("pointerdown", handleOutsideClick, true);
    document.addEventListener("click", handleOutsideClick, true);
    document.addEventListener("keydown", handleEscape);

    return () => {
      document.removeEventListener("pointerdown", handleOutsideClick, true);
      document.removeEventListener("click", handleOutsideClick, true);
      document.removeEventListener("keydown", handleEscape);
    };
  }, [isOpen]);

  const handleToggle = () => {
    setIsOpen((previous) => !previous);
  };

  const handleItemClick = () => {
    setIsOpen(false);
  };

  return (
    <div ref={anchorRef} className="dropdown-anchor">
      <button
        type="button"
        className={`button button-ghost button-small dropdown-toggle ${isOpen ? "is-open" : ""} ${active ? "is-active" : ""}`.trim()}
        aria-expanded={isOpen}
        aria-haspopup="menu"
        aria-controls={menuId}
        aria-current={active ? "page" : undefined}
        onClick={handleToggle}
      >
        <span>{label}</span>
        <span className="dropdown-toggle__caret" aria-hidden="true">
          ▾
        </span>
      </button>
      {isOpen ? (
        <div
          id={menuId}
          role="menu"
          className={`dropdown ${align === "right" ? "dropdown--right" : ""}`.trim()}
        >
          <ul className="menu-list">
            {items.map((item) => (
              <li key={`${label}-${item.href}-${item.label}`} role="none">
                <Link
                  href={item.href}
                  role="menuitem"
                  className={`menu-item ${item.active ? "is-active" : ""}`.trim()}
                  onClick={handleItemClick}
                >
                  {item.label}
                </Link>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}

export function HeaderLink({ href, label, active }: HeaderLinkProps) {
  return (
    <Link
      href={href}
      className={`button button-ghost button-small ${active ? "is-active" : ""}`.trim()}
      aria-current={active ? "page" : undefined}
    >
      {label}
    </Link>
  );
}

export function HeaderStatus({ children }: HeaderStatusProps) {
  return <div className="app-header__status">{children}</div>;
}

export function HeaderMeta({ children }: HeaderMetaProps) {
  return <div className="app-header__meta">{children}</div>;
}

export function HeaderEmail({ value }: HeaderEmailProps) {
  return (
    <span className="app-header__email" title={value}>
      {value}
    </span>
  );
}
