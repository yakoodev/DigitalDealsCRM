# DDCRM RBAC UI

Базовый frontend-проект `WP-RBAC-UI` на `Next.js App Router + TypeScript + TanStack Query + Orval`.

## Скрипты

- `npm run generate:api` — генерация клиента из `docs/api-contracts/openapi-external.yaml`.
- `npm run dev` — запуск dev-сервера UI.
- `npm run lint` — eslint-проверка.
- `npm run build` — production сборка (с авто-генерацией Orval через `prebuild`).

## Быстрый запуск

1. Запустите backend (локально или через `docker compose` в корне репозитория).
2. Откройте UI и вставьте `Bearer JWT` + `Core API Base URL` (по умолчанию `http://localhost:5073`).
3. Выберите UI-роль (`owner/admin/moderator`) для role-aware guard.

## Важно

- UI guard скрывает операции по матрице ролей, но source of truth по доступу остается в backend.
- Секреты/реальные прокси и токены не хранятся в git.
