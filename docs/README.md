# DDCRM Docs Hub

## Архитектурные черновики
- [00-overview.md](./architecture_draft/00-overview.md)
- [01-frontend.md](./architecture_draft/01-frontend.md)
- [02-core.md](./architecture_draft/02-core.md)
- [03-core-db.md](./architecture_draft/03-core-db.md)
- [04-accounts-manager.md](./architecture_draft/04-accounts-manager.md)
- [05-accounts-manager-db.md](./architecture_draft/05-accounts-manager-db.md)
- [06-account-api-gateway.md](./architecture_draft/06-account-api-gateway.md)
- [07-account-worker.md](./architecture_draft/07-account-worker.md)
- [08-worker-state-storage.md](./architecture_draft/08-worker-state-storage.md)
- [09-data-flows.md](./architecture_draft/09-data-flows.md)
- [10-service-responsibility-matrix.md](./architecture_draft/10-service-responsibility-matrix.md)
- [11-identity-and-access.md](./architecture_draft/11-identity-and-access.md)
- [12-billing.md](./architecture_draft/12-billing.md)
- [13-entitlement-service.md](./architecture_draft/13-entitlement-service.md)
- [14-admin-and-roles.md](./architecture_draft/14-admin-and-roles.md)
- [15-restrictions-and-features-log.md](./architecture_draft/15-restrictions-and-features-log.md)
- [16-subscription-and-payment-flow.md](./architecture_draft/16-subscription-and-payment-flow.md)
- [17-route-registry-db.md](./architecture_draft/17-route-registry-db.md)

## Блок ТЗ и планирования
- [technical-specification.md](./spec/technical-specification.md)
- [full-product-roadmap.md](./roadmap/full-product-roadmap.md)

## API-контракты
- [api-contracts.md](./api-contracts/api-contracts.md)
- [openapi-common.yaml](./api-contracts/openapi-common.yaml) — общие переиспользуемые компоненты
- [openapi-external.yaml](./api-contracts/openapi-external.yaml) — публичный API
- [openapi-internal.yaml](./api-contracts/openapi-internal.yaml) — внутренний service-to-service API
- [openapi-worker.yaml](./api-contracts/openapi-worker.yaml) — контракт воркеров

## Runtime Env Templates
- [runtime-configuration.md](./standards/runtime-configuration.md) — каноника runtime env-конфигурации
- [../.env.external-api.example](../.env.external-api.example) — CORS-конфигурация external API
- [../.env.internal-api.example](../.env.internal-api.example) — service-auth конфигурация internal API
- [../.env.worker-api.example](../.env.worker-api.example) — service-auth конфигурация worker API

## Стандарты
- [quality-gates.md](./standards/quality-gates.md) — канонический каталог NFR/SLO/порогов инцидентов
- [worker-action-conventions.md](./standards/worker-action-conventions.md) — каноника action-key для Gateway/worker
- [access-control-matrix.md](./standards/access-control-matrix.md) — каноническая матрица ролей и прав
- [openapi-governance.md](./standards/openapi-governance.md) — каноника правил OpenAPI-совместимости и contract quality gates
- [runtime-configuration.md](./standards/runtime-configuration.md) — каноника runtime env-конфигурации
- [source-of-truth-map.md](./standards/source-of-truth-map.md) — карта канонических источников по темам
- [documentation-governance.md](./standards/documentation-governance.md) — правила сопровождения канонических документов

## Обязательные артефакты уровня C
- [erd.md](./data-model/erd.md)
- [subscription-state-machine.md](./state-machines/subscription-state-machine.md)
- [test-strategy.md](./testing/test-strategy.md)
- [internal-contract-checklist.md](./testing/internal-contract-checklist.md)
- [worker-contract-checklist.md](./testing/worker-contract-checklist.md)
- [runbook.md](./operations/runbook.md)
- [rollout-rollback-plan.md](./operations/rollout-rollback-plan.md)
- [service-auth-rotation-playbook.md](./operations/service-auth-rotation-playbook.md)
