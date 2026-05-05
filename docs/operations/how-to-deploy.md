# Как Развернуть DDCRM (Compose + K8s Reference)

Документ описывает production-like подходы. Kubernetes манифесты/Helm в рамках этой задачи не поставляются.

## 1. Production-like Docker Compose

### 1.1 Порядок
1. Подготовьте env/secret значения.
2. Соберите нужные образы (`./eng/build-local-images.ps1 all`).
3. Запустите контур `docker compose up -d`.
4. Проверьте `/health` для внешнего и внутренних API.
5. Проведите smoke-сценарии создания template/account.

### 1.2 Обязательные env и secret mapping
- JWT/External API:
  - `EXTERNAL_API_JWT_ISSUER`
  - `EXTERNAL_API_JWT_AUDIENCE`
  - `EXTERNAL_API_JWT_SIGNING_KEY` (secret)
  - `EXTERNAL_API_AUTH_TOKEN_LIFETIME_MINUTES`
- Super-admin bootstrap:
  - `EXTERNAL_API_SUPER_ADMIN_EMAIL` (secret-like)
  - `EXTERNAL_API_SUPER_ADMIN_PASSWORD` (secret)
  - `EXTERNAL_API_SUPER_ADMIN_DISPLAY_NAME`
- Future auth providers flags:
  - `EXTERNAL_API_AUTH_PROVIDER_TELEGRAM_ENABLED`
  - `EXTERNAL_API_AUTH_PROVIDER_GITHUB_ENABLED`
  - `EXTERNAL_API_AUTH_PROVIDER_GOOGLE_ENABLED`
- Internal service auth:
  - `INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS` (secret)
  - `INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN` (secret)
  - `WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS` (secret)
  - `WORKER_API_SERVICE_AUTH_CLIENT_TOKEN` (secret)
- База данных:
  - `*_DB_CONNECTION`/`ConnectionStrings__*` (secret)
- Шифрование worker/integration:
  - `WORKER_PROXY_CREDENTIALS_ENCRYPTION_KEY` (secret)
  - `ACCOUNT_MANAGER_AUTOSPAWN_REGISTRY_SECRET_ENCRYPTION_KEY` (secret)

### 1.3 Rollout notes
- Сначала деплойте внутренние API (`route-registry`, `accounts-manager`, `worker-api`, `gateway`), затем UI.
- Перед переключением трафика проверьте health endpoints и доступность `/v1/projects`.
- На новом окружении после старта account-type каталог пустой, это ожидаемое поведение.
- Template для каждой платформы создается вручную админом через API/UI.

## 2. Kubernetes Reference Architecture

### 2.1 Контур
- Namespace `ddcrm`.
- Ingress/LoadBalancer:
  - `ui` (Next.js)
  - `core-api` / `gateway-api` (HTTP)
- Internal services (ClusterIP):
  - `iam-api`
  - `route-registry-api`
  - `accounts-manager-api`
  - `billing-api`
  - `entitlement-api`
  - `worker-api`
- PostgreSQL: managed service или StatefulSet (production рекомендуется managed DB).

### 2.2 Конфигурация
- Non-secret параметры: ConfigMap.
- Tokens/keys/passwords: Secret.
- Ротация секретов через controlled rollout (rolling update по сервисам).

### 2.3 Rollout/rollback
- Rollout:
  1. Обновить ConfigMap/Secret.
  2. Прокатить stateless сервисы волнами.
  3. Проверить readiness/liveness + `/health`.
- Rollback:
  1. Вернуть предыдущий image tag.
  2. Вернуть предыдущие secret values при несовместимом изменении.
  3. Перепроверить критические API (`/v1/projects`, `/v1/admin/account-manager/account-types`).

## 3. Полный ручной reset БД
- Для тестовых/стендовых контуров ручной сброс описан в [db-reset.md](./db-reset.md).
