# DDCRM — Runtime Configuration (канонический)

## Назначение
Единый источник правил runtime-конфигурации через env-шаблоны.

Правило:
- env-шаблоны и правила их изменения определяются только здесь;
- остальные документы ссылаются на этот стандарт без копирования списков переменных.

## 1. Канонические env-шаблоны
- `.env.external-api.example` — runtime-конфигурация CORS для external API;
- `.env.internal-api.example` — runtime-конфигурация service-auth для internal API;
- `.env.worker-api.example` — runtime-конфигурация service-auth для worker API.

## 2. Конвенции env-переменных
- `EXTERNAL_API_CORS_*` — переменные CORS-контура external API;
- `INTERNAL_API_SERVICE_AUTH_*` — переменные service-auth контура internal API;
- `WORKER_API_SERVICE_AUTH_*` — переменные service-auth контура worker API;
- булевы значения задаются как `true/false` (lowercase);
- списки значений задаются comma-separated строкой без пробелов.

## 3. Политика секретов
- значения токенов и секретов в production не хранятся в git;
- `.env.*.example` содержат только шаблонные значения;
- реальные значения должны поставляться через secret manager/CI variables.

## 4. Процесс изменения
- добавить/изменить переменную сначала в соответствующем `.env.*.example`;
- синхронизировать этот стандарт при изменении префиксов/правил формата;
- проверить связанные OpenAPI-контракты и contract-tests.

## 5. Связанные документы
- `docs/standards/openapi-governance.md`
- `docs/standards/documentation-governance.md`
- `docs/operations/rollout-rollback-plan.md`
- `docs/operations/runbook.md`
- `.env.external-api.example`
- `.env.internal-api.example`
- `.env.worker-api.example`
