# DDCRM — Delivery Work Packages

## Назначение
Исполняемый план реализации: кто и в каком порядке может брать работу без разночтений с каноникой.

Правило:
- этот документ определяет структуру работ и критерии завершения пакетов;
- продуктовые/технические правила берутся из канонических документов по `docs/standards/source-of-truth-map.md`.

## 1. Зависимости пакетов
| ID | Пакет | Основные зависимости |
|---|---|---|
| `WP-PLATFORM-CORE` | Core API + Core DB baseline | — |
| `WP-IAM` | membership/roles/permission checks | `WP-PLATFORM-CORE` |
| `WP-ROUTE-REGISTRY` | Route Registry DB + internal route API | `WP-PLATFORM-CORE` |
| `WP-ACCOUNTS-MANAGER` | lifecycle orchestration | `WP-PLATFORM-CORE`, `WP-ROUTE-REGISTRY` |
| `WP-GATEWAY` | auth+entitlement+route proxy | `WP-IAM`, `WP-ROUTE-REGISTRY` |
| `WP-WORKER-CONTRACT` | базовая worker реализация по OpenAPI | `WP-ACCOUNTS-MANAGER`, `WP-GATEWAY` |
| `WP-BILLING` | payment/subscription/webhook | `WP-PLATFORM-CORE` |
| `WP-ENTITLEMENT` | access/limits recalculation | `WP-BILLING`, `WP-PLATFORM-CORE` |
| `WP-RBAC-UI` | ограничение UI по ролям | `WP-IAM`, `WP-GATEWAY` |
| `WP-OPS-READY` | runbook/rollout/alerts/token rotation | `WP-GATEWAY`, `WP-BILLING`, `WP-ENTITLEMENT` |

## 2. Пакеты реализации
### 2.1 `WP-PLATFORM-CORE`
- Scope:
  - проекты, участники, аккаунты (бизнес read-модель)
  - базовые external endpoint-ы Core
- Done:
  - external контракт реализован без breaking-расхождений
  - `OAG-VAL-OPENAPI31`, `OAG-LINT-STYLE` зелёные

### 2.2 `WP-IAM`
- Scope:
  - membership и проектные роли
  - permission checks для Core/Gateway
  - membership cache invalidation
- Done:
  - RBAC соответствует `docs/standards/access-control-matrix.md`
  - подтверждён `QG-SLO-IAM-CACHE-INVALIDATE-P99`

### 2.3 `WP-ROUTE-REGISTRY`
- Scope:
  - schema и API резолва/upsert/switch route
  - версионирование route записей
- Done:
  - route операции проходят contract-tests
  - проверяются `QG-SLO-LAT-ROUTE-UPSERT-P95`, `QG-SLO-LAT-ROUTE-RESOLVE-P95`

### 2.4 `WP-ACCOUNTS-MANAGER`
- Scope:
  - lifecycle create/update/delete/migrate с idempotency
  - orchestration worker provisioning
- Done:
  - e2e lifecycle сценарии зелёные
  - удаление аккаунта гарантированно убирает route

### 2.5 `WP-GATEWAY`
- Scope:
  - resolve route + auth + entitlement checks
  - прокси запросов к worker API
  - CORS policy для external browser calls
- Done:
  - `QG-SLO-LAT-GW-CHECK-P95`, `QG-SLO-LAT-GW-CHECK-P99` подтверждены
  - `OAG-TEST-CORS-EXTERNAL` зелёный

### 2.6 `WP-WORKER-CONTRACT`
- Scope:
  - базовый worker runtime по `openapi-worker.yaml`
  - capabilities + `ext.*` extension policy
  - тестовый воркер (симулятор площадок) как канонический контур разработки/контрактных тестов
- Done:
  - `docs/testing/worker-contract-checklist.md` выполнен
  - `docs/testing/test-worker-checklist.md` выполнен
  - политика `ext.test.*` реализована только для non-production профиля
  - `OAG-TEST-CONTRACT-WORKER`, `OAG-TEST-CAPABILITY-ACTION` зелёные

### 2.7 `WP-BILLING`
- Scope:
  - payment intents, webhook processing, refunds, manual activate
  - dedup и reconciliation
- Done:
  - `QG-SLO-WEBHOOK-P95`, `QG-SLO-WEBHOOK-DEDUP-TTL` подтверждены
  - webhook duplicate не вызывает повторный side effect

### 2.8 `WP-ENTITLEMENT`
- Scope:
  - recalculation pipeline по subscription status + plan + add-ons
  - support `trial/grace` и partial blocking
- Done:
  - `QG-SLO-ENT-RECALC-P95` подтверждён
  - downgrade/unpaid сценарии проходят e2e

### 2.9 `WP-RBAC-UI`
- Scope:
  - frontend stack: `React + Next.js (App Router) + TypeScript`
  - API/data слой: `TanStack Query v5 + Orval` (клиенты из OpenAPI)
  - role-aware UI guards
  - скрытие финансовых/чувствительных операций для moderator
- Done:
  - регрессионные RBAC-сценарии из test strategy зелёные
  - reveal/update proxy credentials доступны только owner/admin

### 2.10 `WP-OPS-READY`
- Scope:
  - alerting dashboards + incident playbooks
  - rollout/rollback rehearsal
  - service-auth token rotation readiness
- Done:
  - runbook/rollout подтверждены dry-run
  - auth/CORS incident thresholds и recovery criteria мониторятся по `QG-*`

## 3. Cross-cutting Definition of Done
- контрактные проверки выполняются по `docs/testing/contract-gates-execution.md`;
- нет нерегламентированных breaking-изменений OpenAPI;
- backend/frontend/tooling соответствуют канонике `docs/standards/technology-stack.md`;
- runtime env-настройки соответствуют `docs/standards/runtime-configuration.md`;
- инцидентные и recovery пороги проверяемы по `docs/standards/quality-gates.md`.

## 4. Минимальный порядок запуска реализации
1. `WP-PLATFORM-CORE`, `WP-IAM`, `WP-ROUTE-REGISTRY`
2. `WP-ACCOUNTS-MANAGER`, `WP-GATEWAY`
3. `WP-WORKER-CONTRACT`
4. `WP-BILLING`, `WP-ENTITLEMENT`
5. `WP-RBAC-UI`
6. `WP-OPS-READY`

## 5. Связанные документы
- `docs/roadmap/full-product-roadmap.md`
- `docs/spec/technical-specification.md`
- `docs/testing/contract-gates-execution.md`
- `docs/testing/test-strategy.md`
- `docs/standards/source-of-truth-map.md`
