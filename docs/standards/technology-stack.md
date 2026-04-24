# DDCRM — Technology Stack (канонический)

## Назначение
Единый источник утверждённого технологического стека DDCRM для backend, worker-контуров и frontend.

Правило:
- выбор и изменение стека фиксируются только здесь;
- остальные документы используют ссылки на этот стандарт без дублирования полного списка технологий.

## 1. Backend (платформенные сервисы)
Обязательный стек для платформенных сервисов:
- язык: `C#`;
- платформа: `.NET` (LTS-профиль);
- web/API слой: `ASP.NET Core`.

К платформенным сервисам относятся:
- Core;
- IAM;
- Accounts Manager;
- Account API Gateway;
- Billing;
- Entitlement;
- internal/admin API сервисы.

## 2. Worker-контур
- контракт worker-ов обязателен по `docs/api-contracts/openapi-worker.yaml`;
- worker runtime может быть polyglot при условии полной контрактной совместимости;
- тестовый воркер (симулятор площадок) в DDCRM реализуется на `C#/.NET`;
- если боевой worker реализуется не на `C#`, команда обязана документировать это решение и подтвердить прохождение `docs/testing/worker-contract-checklist.md`.

## 3. Frontend
Обязательный стек frontend:
- `React`;
- `Next.js` (App Router);
- `TypeScript`.

Обязательный data/API слой frontend:
- `TanStack Query v5` для работы с server-state;
- `Orval` для генерации типобезопасных TS-клиентов из OpenAPI-контрактов.

Допустимый fallback-генератор:
- `openapi-generator` (`typescript-fetch`) по согласованию команды, если `Orval` не закрывает конкретный кейс.

## 4. Изменение стека
Изменение стека допускается только при выполнении всех условий:
- обновлён этот канонический документ;
- синхронизированы зависимые документы (roadmap, work-packages, ТЗ, AGENTS);
- зафиксирован план миграции и риски совместимости.

## 5. Связанные документы
- `docs/standards/source-of-truth-map.md`
- `docs/standards/documentation-governance.md`
- `docs/spec/technical-specification.md`
- `docs/implementation/delivery-work-packages.md`
- `docs/roadmap/full-product-roadmap.md`
- `docs/standards/test-worker-governance.md`
- `AGENTS.md`
