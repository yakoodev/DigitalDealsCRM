# DDCRM — Runtime Configuration (канонический)

## Назначение
Единый источник правил runtime-конфигурации через env-шаблоны.

Правило:
- env-шаблоны и правила их изменения определяются только здесь;
- остальные документы ссылаются на этот стандарт без копирования списков переменных.

## 1. Канонические env-шаблоны
- `.env.external-api.example` — runtime-конфигурация CORS для external API;
- `.env.internal-api.example` — runtime-конфигурация service-auth для internal API и внутренних client-настроек;
- `.env.worker-api.example` — runtime-конфигурация service-auth для worker API;
- `.env.test-worker.example` — runtime-профиль тестового воркера (симулятора) для non-production контуров.

## 2. Конвенции env-переменных
- `EXTERNAL_API_CORS_*` — переменные CORS-контура external API;
- `EXTERNAL_API_JWT_*` — переменные валидации bearer JWT для external API;
- `INTERNAL_API_SERVICE_AUTH_*` — переменные service-auth контура internal API;
- `WORKER_API_SERVICE_AUTH_*` — переменные service-auth контура worker API;
- `WORKER_CONTROL_CLIENT_*` — переменные internal worker-control client-а (service-to-service вызовы internal -> worker);
- `TEST_WORKER_*` — переменные управления тестовым воркером по `docs/standards/test-worker-governance.md`;
- булевы значения задаются как `true/false` (lowercase);
- списки значений задаются comma-separated строкой без пробелов.

## 2.1 Изоляция service-auth токенов
- токены `internal` и `worker` контуров должны быть раздельными;
- пересечение значений между `INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS` и `WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS` запрещено;
- клиентские токены `INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN` и `WORKER_API_SERVICE_AUTH_CLIENT_TOKEN` не должны совпадать.

## 2.2 Контурные ограничения тестового воркера
- тестовый воркер включается только в `local/ci/staging`;
- в production включение тестового воркера должно быть заблокировано (`TEST_WORKER_BLOCK_IN_PRODUCTION=true`);
- тестовый воркер по умолчанию скрыт и активируется только явной env-настройкой;
- `ext.test.*` действия разрешены только при включённом non-production профиле симулятора.

## 3. Политика секретов
- значения токенов и секретов в production не хранятся в git;
- `.env.*.example` содержат только шаблонные значения;
- реальные значения должны поставляться через secret manager/CI variables.

## 4. Процесс изменения
- добавить/изменить переменную сначала в соответствующем `.env.*.example`;
- синхронизировать этот стандарт при изменении префиксов/правил формата;
- при изменении переменных тестового воркера синхронизировать `docs/standards/test-worker-governance.md`;
- проверить связанные OpenAPI-контракты и contract-tests.

## 5. Связанные документы
- `docs/standards/source-of-truth-map.md`
- `docs/standards/openapi-governance.md`
- `docs/standards/test-worker-governance.md`
- `docs/standards/documentation-governance.md`
- `docs/operations/rollout-rollback-plan.md`
- `docs/operations/runbook.md`
- `docs/operations/service-auth-rotation-playbook.md`
- `.env.external-api.example`
- `.env.internal-api.example`
- `.env.worker-api.example`
- `.env.test-worker.example`
