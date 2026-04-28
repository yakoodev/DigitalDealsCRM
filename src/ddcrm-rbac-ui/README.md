# DDCRM Platform UI

Frontend проекта `WP-RBAC-UI` на `Next.js App Router + TypeScript + TanStack Query + Orval`.

## Скрипты

- `npm run generate:api` — генерация клиента из `docs/api-contracts/openapi-external.yaml`.
- `npm run dev` — запуск dev-сервера UI.
- `npm run test:run` — запуск unit/component тестов (`vitest`).
- `npm run lint` — eslint.
- `npm run build` — production сборка (`prebuild` включает генерацию Orval).

## Route Map (W9)

- `/login` — авторизация (demo/manual JWT).
- `/dashboard` — стартовый экран после логина (`Dashboard root`).
- `/projects` — портфель проектов + создание проекта.
- `/admin/account-manager` — системный admin overview для AccountManager control plane.
- `/admin/account-manager/servers` — настройка worker server registry.
- `/admin/account-manager/templates` — настройка platform templates (runtime/autospawn).
- `/projects/[projectId]` — overview проекта (аккаунты + live KPI по товарам/перепискам + быстрые переходы).
- `/projects/[projectId]/accounts` — аккаунты проекта (modal-first create/manage).
- `/projects/[projectId]/products` — товары проекта (modal-first create/edit).
- `/projects/[projectId]/messages` — переписки проекта; история чата открывается в modal-first thread.

## IA / Sidebar Policy

- Левый sidebar не используется для выбора проектов.
- На `/dashboard` и `/projects` sidebar показывает профиль/настройки и навигацию.
- Для пользователей с system-claim доступна отдельная admin-навигация на `/admin/account-manager/*`.
- На `/projects/[projectId]` и `/projects/[projectId]/*` sidebar становится контекстным: статус проекта, статистика аккаунтов, навигация по вкладкам (`overview/accounts/products/messages`).
- Выбор и создание проектов выполняются в центральной области `dashboard/projects`, а не в боковой панели.

## Route-bound modal URL contract

- `accounts`:
  - create: `?modal=create`
  - manage: `?modal=manage&accountId=<...>`
- `products`:
  - create: `?modal=create&accountId=<...>`
  - edit: `?modal=edit&accountId=<...>&productId=<...>`
- `messages`:
  - thread: `?modal=thread&accountId=<...>&conversationId=<...>`

Закрытие модалки удаляет только modal-параметры и сохраняет остальной контекст URL.

## Legacy operation routes

Старые route-ы оставлены только как redirect:

- `/projects/[projectId]/accounts/new` -> `/projects/[projectId]/accounts?modal=create`
- `/projects/[projectId]/accounts/manage` -> `/projects/[projectId]/accounts?modal=manage&accountId=<...>`
- `/projects/[projectId]/products/new` -> `/projects/[projectId]/products?modal=create&accountId=<...>`
- `/projects/[projectId]/products/edit` -> `/projects/[projectId]/products?modal=edit&accountId=<...>&productId=<...>`
- `/projects/[projectId]/messages/thread` -> `/projects/[projectId]/messages?modal=thread&accountId=<...>&conversationId=<...>`

## Env для demo JWT

- `NEXT_PUBLIC_EXTERNAL_API_JWT_ISSUER` (`ddcrm-local` по умолчанию)
- `NEXT_PUBLIC_EXTERNAL_API_JWT_AUDIENCE` (`ddcrm-api` по умолчанию)
- `NEXT_PUBLIC_EXTERNAL_API_JWT_SIGNING_KEY` (`replace-with-long-random-signing-key` по умолчанию)

Значения должны совпадать с конфигурацией JWT в Core API, иначе external API вернет `401`.

## Принципы

- Backend OpenAPI-контракты не меняются UI-слоем.
- UI role-aware: чувствительные account lifecycle/proxy операции доступны только owner/admin.
- System admin зона `/admin/account-manager/*` доступна только при JWT claim `system.accountManager.manage`.
- Техполя (`id/requestId/worker instance`) показываются в `Details`-блоках, а не в основном потоке.
