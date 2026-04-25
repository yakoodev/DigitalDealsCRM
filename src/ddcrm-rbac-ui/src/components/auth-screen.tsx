"use client";

import { useMemo, useState } from "react";
import {
  authenticateDemo,
  authenticateManual,
  demoUsers,
  getDefaultBaseUrl,
  getJwtMeta,
  type PlatformSession,
} from "@/lib/auth";
import { projectRoles, type ProjectRole } from "@/lib/rbac";

interface AuthScreenProps {
  onAuthenticated: (session: PlatformSession) => void;
}

type AuthMode = "demo" | "manual";

export function AuthScreen({ onAuthenticated }: AuthScreenProps) {
  const jwtMeta = useMemo(() => getJwtMeta(), []);

  const [authMode, setAuthMode] = useState<AuthMode>("demo");
  const [statusMessage, setStatusMessage] = useState(
    "Войдите в платформу, чтобы открыть список проектов и рабочие вкладки проекта.",
  );
  const [isSubmitting, setIsSubmitting] = useState(false);

  const [baseUrl, setBaseUrl] = useState(getDefaultBaseUrl());

  const [demoEmail, setDemoEmail] = useState(demoUsers[0].email);
  const [demoPassword, setDemoPassword] = useState(demoUsers[0].password);

  const [manualToken, setManualToken] = useState("");
  const [manualRole, setManualRole] = useState<ProjectRole>("owner");
  const [manualDisplayName, setManualDisplayName] = useState("Manual User");
  const [manualEmail, setManualEmail] = useState("manual@ddcrm.local");
  const [manualUserId, setManualUserId] = useState("");

  const selectedDemoUser = useMemo(
    () => demoUsers.find((user) => user.email === demoEmail) ?? demoUsers[0],
    [demoEmail],
  );

  const handleDemoSignIn = async () => {
    setIsSubmitting(true);
    setStatusMessage("Проверяю demo-аккаунт и создаю JWT...");

    try {
      const session = await authenticateDemo({
        email: demoEmail,
        password: demoPassword,
        baseUrl,
      });
      onAuthenticated(session);
    } catch (error) {
      setStatusMessage(
        error instanceof Error ? error.message : "Не удалось выполнить demo-вход.",
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleManualSignIn = () => {
    setIsSubmitting(true);
    setStatusMessage("Проверяю параметры ручного входа...");

    try {
      const session = authenticateManual({
        token: manualToken,
        baseUrl,
        displayName: manualDisplayName,
        email: manualEmail,
        role: manualRole,
        userId: manualUserId,
      });
      onAuthenticated(session);
    } catch (error) {
      setStatusMessage(
        error instanceof Error ? error.message : "Не удалось выполнить ручной вход.",
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <main className="auth-shell" data-testid="auth-screen">
      <section className="auth-hero">
        <p className="auth-eyebrow">DDCRM PLATFORM</p>
        <h1>Единый кабинет управления проектами и маркетплейс-аккаунтами</h1>
        <p>
          Route-driven интерфейс DDCRM: вход в платформу, список проектов и
          рабочие вкладки проекта (аккаунты, товары, сообщения, схемы).
        </p>

        <div className="auth-features">
          <div className="auth-feature-card">
            <h2>Авторизация</h2>
            <p>
              Demo-вход создаёт JWT совместимый с локальным external API. Можно
              войти и с вашим собственным токеном.
            </p>
          </div>
          <div className="auth-feature-card">
            <h2>Project Workflow</h2>
            <p>
              После входа пользователь попадает в список проектов, открывает
              конкретный проект и работает с его вкладками.
            </p>
          </div>
          <div className="auth-feature-card">
            <h2>Операционные Вкладки</h2>
            <p>
              Аккаунты, товары, сообщения и schemas работают через единый
              API-layer на TanStack Query + Orval.
            </p>
          </div>
        </div>
      </section>

      <section className="auth-panel">
        <div className="auth-mode-switch">
          <button
            className={`button ${authMode === "demo" ? "button-primary" : "button-ghost"}`}
            onClick={() => setAuthMode("demo")}
            type="button"
            data-testid="auth-mode-demo"
          >
            Demo Вход
          </button>
          <button
            className={`button ${authMode === "manual" ? "button-primary" : "button-ghost"}`}
            onClick={() => setAuthMode("manual")}
            type="button"
            data-testid="auth-mode-manual"
          >
            Ручной JWT
          </button>
        </div>

        <label className="field">
          <span>Core API Base URL</span>
          <input
            className="input"
            value={baseUrl}
            onChange={(event) => setBaseUrl(event.target.value)}
            placeholder="http://localhost:5073"
            data-testid="auth-base-url"
          />
        </label>

        {authMode === "demo" ? (
          <>
            <label className="field">
              <span>Demo User</span>
              <select
                className="input"
                value={demoEmail}
                onChange={(event) => {
                  const nextEmail = event.target.value;
                  setDemoEmail(nextEmail);
                  const user = demoUsers.find((candidate) => candidate.email === nextEmail);
                  if (user) {
                    setDemoPassword(user.password);
                  }
                }}
                data-testid="auth-demo-user-select"
              >
                {demoUsers.map((user) => (
                  <option key={user.email} value={user.email}>
                    {user.displayName} · {user.email} · role={user.role}
                  </option>
                ))}
              </select>
            </label>

            <label className="field">
              <span>Password</span>
              <input
                type="password"
                className="input"
                value={demoPassword}
                onChange={(event) => setDemoPassword(event.target.value)}
                data-testid="auth-demo-password"
              />
            </label>

            <div className="auth-hint-card">
              <p>
                JWT issuer/audience для demo-подписи:
                <strong>
                  {" "}
                  {jwtMeta.issuer} / {jwtMeta.audience}
                </strong>
              </p>
              <p>
                Выбранный пользователь:
                <strong>
                  {" "}
                  {selectedDemoUser.displayName} ({selectedDemoUser.userId})
                </strong>
              </p>
            </div>

            <button
              type="button"
              className="button button-primary"
              disabled={isSubmitting}
              onClick={handleDemoSignIn}
              data-testid="auth-demo-submit"
            >
              Войти В Платформу
            </button>
          </>
        ) : (
          <>
            <label className="field">
              <span>Bearer JWT</span>
              <textarea
                className="input textarea"
                value={manualToken}
                onChange={(event) => setManualToken(event.target.value)}
                placeholder="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
                data-testid="auth-manual-token"
              />
            </label>

            <div className="grid-2">
              <label className="field">
                <span>Display Name</span>
                <input
                  className="input"
                  value={manualDisplayName}
                  onChange={(event) => setManualDisplayName(event.target.value)}
                  data-testid="auth-manual-display-name"
                />
              </label>
              <label className="field">
                <span>Email</span>
                <input
                  className="input"
                  value={manualEmail}
                  onChange={(event) => setManualEmail(event.target.value)}
                  data-testid="auth-manual-email"
                />
              </label>
            </div>

            <div className="grid-2">
              <label className="field">
                <span>UI Role</span>
                <select
                  className="input"
                  value={manualRole}
                  onChange={(event) => setManualRole(event.target.value as ProjectRole)}
                  data-testid="auth-manual-role"
                >
                  {projectRoles.map((role) => (
                    <option key={role} value={role}>
                      {role}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>User ID (optional)</span>
                <input
                  className="input"
                  value={manualUserId}
                  onChange={(event) => setManualUserId(event.target.value)}
                  placeholder="GUID; если пусто, берётся из JWT sub"
                  data-testid="auth-manual-user-id"
                />
              </label>
            </div>

            <button
              type="button"
              className="button button-primary"
              disabled={isSubmitting}
              onClick={handleManualSignIn}
              data-testid="auth-manual-submit"
            >
              Продолжить С JWT
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
