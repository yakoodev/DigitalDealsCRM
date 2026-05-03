"use client";

import { useState } from "react";
import { ThemeToggle } from "@/components/theme-toggle";
import {
  authenticateDemo,
  demoUsers,
  getDefaultBaseUrl,
  type PlatformSession,
} from "@/lib/auth";
import { listProjectsRequest } from "@/lib/api-client";

interface AuthScreenProps {
  onAuthenticated: (session: PlatformSession) => void;
}

export function AuthScreen({ onAuthenticated }: AuthScreenProps) {
  const [statusMessage, setStatusMessage] = useState(
    "Введите email и пароль администратора для входа в DDCRM.",
  );
  const [isSubmitting, setIsSubmitting] = useState(false);

  const [baseUrl, setBaseUrl] = useState(getDefaultBaseUrl());
  const [email, setEmail] = useState(demoUsers[0]?.email ?? "admin@ddcrm.local");
  const [password, setPassword] = useState(demoUsers[0]?.password ?? "");

  const handlePasswordSignIn = async () => {
    setIsSubmitting(true);
    setStatusMessage("Проверяю учётные данные...");

    try {
      const session = await authenticateDemo({
        email,
        password,
        baseUrl,
      });
      await listProjectsRequest({
        token: session.token,
        baseUrl: session.baseUrl,
      });
      onAuthenticated(session);
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось выполнить вход.");
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <main className="auth-layout" data-testid="auth-screen">
      <section className="auth-hero-card">
        <p className="module-page-kicker">DigitalDeals CRM</p>
        <h1>Вход в DDCRM</h1>
        <p>
          Базовый flow: email и пароль администратора. После входа откроется dashboard.
        </p>
        <div className="auth-hero-stats">
          <article>
            <span>Role</span>
            <strong>System Admin</strong>
          </article>
          <article>
            <span>Theme</span>
            <strong>System / Light / Dark</strong>
          </article>
          <article>
            <span>Auth</span>
            <strong>Password</strong>
          </article>
        </div>
      </section>

      <section className="auth-form-card">
        <ThemeToggle />

        <label className="field">
          <span>Core API Base URL</span>
          <input
            className="input"
            value={baseUrl}
            onChange={(event) => setBaseUrl(event.target.value)}
            placeholder="http://localhost:15073"
            data-testid="auth-base-url"
          />
        </label>

        <label className="field">
          <span>Email</span>
          <input
            className="input"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            placeholder="admin@ddcrm.local"
            data-testid="auth-email"
          />
        </label>

        <label className="field">
          <span>Password</span>
          <input
            type="password"
            className="input"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            data-testid="auth-password"
          />
        </label>

        <button
          type="button"
          className="button button-primary"
          disabled={isSubmitting}
          onClick={handlePasswordSignIn}
          data-testid="auth-submit"
        >
          Войти
        </button>

        <p className="status" data-testid="auth-status">
          {statusMessage}
        </p>
      </section>
    </main>
  );
}
