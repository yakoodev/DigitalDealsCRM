"use client";

import { useEffect, useMemo, useState } from "react";
import { ThemeToggle } from "@/components/theme-toggle";
import { listProjectsRequest } from "@/lib/api-client";
import {
  changePasswordWithSession,
  getDefaultBaseUrl,
  getJwtMeta,
  loadAuthProviders,
  loginWithPassword,
  registerWithPassword,
  type AuthProviderInfo,
  type PlatformSession,
} from "@/lib/auth";

interface AuthScreenProps {
  onAuthenticated: (session: PlatformSession) => void;
}

type AuthMode = "login" | "register";

export function AuthScreen({ onAuthenticated }: AuthScreenProps) {
  const jwtMeta = useMemo(() => getJwtMeta(), []);
  const [statusMessage, setStatusMessage] = useState(
    "Войдите по email и паролю или зарегистрируйте новый аккаунт.",
  );
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoadingProviders, setIsLoadingProviders] = useState(false);
  const [authMode, setAuthMode] = useState<AuthMode>("login");

  const [baseUrl, setBaseUrl] = useState(getDefaultBaseUrl());
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [providers, setProviders] = useState<AuthProviderInfo[]>([]);

  const [pendingSession, setPendingSession] = useState<PlatformSession | null>(null);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");

  const requiresPasswordChange = Boolean(pendingSession);

  const loadProviders = async () => {
    setIsLoadingProviders(true);
    try {
      const items = await loadAuthProviders(baseUrl);
      setProviders(items);
    } catch {
      setProviders([]);
    } finally {
      setIsLoadingProviders(false);
    }
  };

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadProviders();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleAuthSubmit = async () => {
    setIsSubmitting(true);
    setStatusMessage(
      authMode === "register"
        ? "Регистрирую пользователя..."
        : "Проверяю email и пароль...",
    );

    try {
      const authResult =
        authMode === "register"
          ? await registerWithPassword({
              baseUrl,
              email,
              password,
              displayName,
            })
          : await loginWithPassword({
              baseUrl,
              email,
              password,
            });

      if (authResult.requiresPasswordChange) {
        setPendingSession(authResult.session);
        setCurrentPassword(password);
        setStatusMessage(
          "Для этого аккаунта обязательна смена пароля. Задайте новый пароль, чтобы продолжить.",
        );
        return;
      }

      await listProjectsRequest(authResult.session);
      onAuthenticated(authResult.session);
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : "Не удалось выполнить вход.");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handlePasswordChange = async () => {
    if (!pendingSession) {
      return;
    }

    if (newPassword.length < 8) {
      setStatusMessage("Новый пароль должен содержать минимум 8 символов.");
      return;
    }

    if (newPassword !== confirmPassword) {
      setStatusMessage("Подтверждение пароля не совпадает.");
      return;
    }

    setIsSubmitting(true);
    setStatusMessage("Сохраняю новый пароль...");

    try {
      await changePasswordWithSession({
        session: pendingSession,
        currentPassword,
        newPassword,
      });
      await listProjectsRequest(pendingSession);
      setPendingSession(null);
      setNewPassword("");
      setConfirmPassword("");
      onAuthenticated(pendingSession);
    } catch (error) {
      setStatusMessage(
        error instanceof Error ? error.message : "Не удалось сменить пароль.",
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <main className="auth-layout" data-testid="auth-screen">
      <section className="auth-form-card">
        <div className="auth-form-head">
          <div className="auth-brand-block">
            <div className="auth-logo-slot" aria-hidden="true">
              <span>LOGO</span>
            </div>
            <div className="auth-brand-copy">
              <span className="auth-brand-name">DigitalDeals CRM</span>
              <span className="auth-brand-caption">место под логотип</span>
            </div>
          </div>
          <h1>Вход в рабочее пространство</h1>
        </div>

        <ThemeToggle />

        {!requiresPasswordChange ? (
          <>
            <div className="grid-2">
              <button
                type="button"
                className={`button ${authMode === "login" ? "button-primary" : ""}`}
                onClick={() => setAuthMode("login")}
                data-testid="auth-mode-login"
              >
                Вход
              </button>
              <button
                type="button"
                className={`button ${authMode === "register" ? "button-primary" : ""}`}
                onClick={() => setAuthMode("register")}
                data-testid="auth-mode-register"
              >
                Регистрация
              </button>
            </div>

            <label className="field">
              <span>Email</span>
              <input
                className="input"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                placeholder="owner@ddcrm.local"
                data-testid="auth-email"
              />
            </label>

            <label className="field">
              <span>Пароль</span>
              <input
                className="input"
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder="********"
                data-testid="auth-password"
              />
            </label>

            {authMode === "register" ? (
              <label className="field">
                <span>Display name (опционально)</span>
                <input
                  className="input"
                  value={displayName}
                  onChange={(event) => setDisplayName(event.target.value)}
                  placeholder="My Team"
                  data-testid="auth-display-name"
                />
              </label>
            ) : null}

            <button
              type="button"
              className="button button-primary"
              disabled={isSubmitting}
              onClick={handleAuthSubmit}
              data-testid="auth-submit"
            >
              {authMode === "register" ? "Создать аккаунт" : "Войти"}
            </button>

            <details className="details-block">
              <summary>Дополнительные настройки</summary>
              <section className="page-stack mt-3">
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
                <section className="hint-block">
                  <p>
                    JWT issuer/audience: <strong>{jwtMeta.issuer}</strong> /{" "}
                    <strong>{jwtMeta.audience}</strong>
                  </p>
                  <p>
                    Planned providers:{" "}
                    {providers.length > 0
                      ? providers
                          .filter((item) => item.provider !== "local")
                          .map((item) => `${item.displayName} (${item.status})`)
                          .join(", ")
                      : "loading..."}
                  </p>
                  <button
                    type="button"
                    className="button"
                    onClick={loadProviders}
                    disabled={isLoadingProviders}
                    data-testid="auth-refresh-providers"
                  >
                    Обновить providers
                  </button>
                </section>
              </section>
            </details>
          </>
        ) : (
          <>
            <p className="hint">
              Первый вход супер-админа требует обязательной смены пароля.
            </p>

            <label className="field">
              <span>Текущий пароль</span>
              <input
                className="input"
                type="password"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
                data-testid="auth-current-password"
              />
            </label>

            <label className="field">
              <span>Новый пароль</span>
              <input
                className="input"
                type="password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                placeholder="минимум 8 символов"
                data-testid="auth-new-password"
              />
            </label>

            <label className="field">
              <span>Подтвердите новый пароль</span>
              <input
                className="input"
                type="password"
                value={confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
                data-testid="auth-new-password-confirm"
              />
            </label>

            <button
              type="button"
              className="button button-primary"
              disabled={isSubmitting}
              onClick={handlePasswordChange}
              data-testid="auth-change-password-submit"
            >
              Сменить пароль
            </button>
          </>
        )}

        <p className="status" data-testid="auth-status">
          {statusMessage}
        </p>
      </section>
    </main>
  );
}
