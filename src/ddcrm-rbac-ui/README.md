# DDCRM Platform UI

Платформенный frontend-проект `WP-RBAC-UI` на `Next.js App Router + TypeScript + TanStack Query + Orval`.

## Скрипты

- `npm run generate:api` — генерация клиента из `docs/api-contracts/openapi-external.yaml`.
- `npm run dev` — запуск dev-сервера UI.
- `npm run test:run` — запуск unit-тестов (`vitest`).
- `npm run lint` — eslint-проверка.
- `npm run build` — production сборка (с авто-генерацией Orval через `prebuild`).

## Быстрый запуск

1. Запустите backend (локально или через `docker compose` в корне репозитория).
2. Запустите UI: `npm run dev`.
3. Откройте `http://localhost:3000` и выберите режим входа:
   - `Demo Вход`: преднастроенные пользователи (`owner/admin/moderator`), JWT подписывается локально через env-конфиг.
   - `Ручной JWT`: вход с вашим bearer-токеном и UI-ролью для role-aware guard.
4. После входа доступен route-driven project flow:
   - `/projects` — отдельная страница списка проектов + `Создать проект`;
   - `/projects/[projectId]/accounts` — список аккаунтов проекта и форма `Добавить аккаунт`;
   - `/projects/[projectId]/products` — авто-загрузка `products.list` при открытии вкладки и при смене аккаунта;
   - `/projects/[projectId]/messages` — авто-загрузка `conversations.list`, история чата грузится только после выбора переписки; отправка сообщения выполняет invalidate списка переписок и выбранного чата;
   - `/projects/[projectId]/schemas` — авто-загрузка `products.schemas.list`;
   - выбранный аккаунт сохраняется отдельно по `projectId`, чтобы не терять контекст между вкладками.

## Env для demo-авторизации

- `NEXT_PUBLIC_EXTERNAL_API_JWT_ISSUER` (по умолчанию `ddcrm-local`)
- `NEXT_PUBLIC_EXTERNAL_API_JWT_AUDIENCE` (по умолчанию `ddcrm-api`)
- `NEXT_PUBLIC_EXTERNAL_API_JWT_SIGNING_KEY` (по умолчанию `replace-with-long-random-signing-key`)

Значения должны соответствовать проверке JWT в `Core API`, иначе external endpoint-ы будут возвращать `401`.

## Важно

- UI guard скрывает операции по матрице ролей, но source of truth по доступу остаётся в backend.
- В текущей итерации UX сфокусирован на project workflow (`projects/accounts/products/messages/schemas`) без legacy-разделов `profile/activity/proxy/billing/gateway/members`.
- Секреты/реальные прокси и токены не хранятся в git.
