# DDCRM — API Contracts

## 1. Канонический формат
- Канонический формат API-контрактов проекта: **OpenAPI 3.1**.
- Источники истины по контурам:
  - `docs/api-contracts/openapi-common.yaml`
  - `docs/api-contracts/openapi-external.yaml`
  - `docs/api-contracts/openapi-internal.yaml`
  - `docs/api-contracts/openapi-worker.yaml`
- Любые изменения endpoint-ов, схем, кодов ошибок и заголовков вносятся сначала в OpenAPI-спеку.

## 2. Зачем это нужно
- генерация typed-клиентов для frontend и внутренних сервисов;
- контрактное тестирование сервисов и worker-ов;
- единая схема валидации request/response;
- контроль обратной совместимости API.
- стек генерации frontend-клиентов фиксируется в `docs/standards/technology-stack.md`.

## 3. Scope текущих спецификаций
- `openapi-common.yaml` содержит общие компоненты (`security`, `parameters`, `requestId/error/ack/generic schemas`).
- `openapi-external.yaml` покрывает публичный Core API и публичный Gateway proxy endpoint; в `x-cors` хранится ссылка на env-источник CORS.
- `openapi-internal.yaml` покрывает service-to-service endpoint-ы (`/internal/...`), включая control-plane операции Accounts Manager (`worker-servers`, `heartbeat`, `account-types`, `account-types/{accountTypeId}`, `lifecycle/rebalance`); все endpoint-ы требуют `service-auth` через `X-Service-Token` (security scheme `internalServiceToken`).
- `openapi-worker.yaml` фиксирует единый контракт, который обязаны поддерживать все worker-ы:
  - типовые доменные операции (`account/conversations/products`) задаются ресурсными endpoint-ами;
  - платформенно-специфичные операции выполняются через `actions/{action}`;
  - все endpoint-ы worker API требуют `service-auth` через `X-Service-Token` (security scheme `workerServiceToken`);
  - обязательна публикация `capabilities` для проверки поддерживаемых операций.
- тестовый воркер (симулятор) использует тот же `openapi-worker` контракт и не добавляет новые production endpoint-ы.
- service-auth токены internal и worker контуров должны быть изолированы (без переиспользования значений).
- каноника action-key вынесена в `docs/standards/worker-action-conventions.md`.
- политика non-production действий `ext.test.*` определяется в `docs/standards/test-worker-governance.md`.

### 3.1 Runtime env-конфигурация
- канонические env-шаблоны и правила формата определяются в `docs/standards/runtime-configuration.md`;
- environment-специфичные значения не хардкодятся в OpenAPI-спеках.

## 4. Базовые правила контракта
- детальные обязательные правила OpenAPI-контрактов и quality gates определяются в `docs/standards/openapi-governance.md`;
- чувствительные proxy credentials не должны возвращаться в стандартных публичных read-response;
- детальная политика proxy credentials (включая `masked by default`, `reveal/update`, требования к активной сессии и аудиту) определяется в `docs/standards/access-control-matrix.md`.

## 5. Готовность контракта к реализации
Контракт считается готовым, когда:
- endpoint содержит request/response schema;
- определены коды ответов и ошибки;
- для критических flow добавлены примеры (`examples`);
- контракт проходит валидацию OpenAPI 3.1;
- контрактные тесты сервиса запускаются от этой спеки.

### 5.1 Совместимость по контурам
- `external`: изменения по умолчанию только backward-compatible.
- `internal`: допускаются более быстрые изменения, но только через обновление контрактных тестов зависимых сервисов.
- `worker`: обратная совместимость обязательна в рамках минорных релизов платформы.
- `worker` extension-операции через `actions/{action}` не должны дублировать типовые ресурсные endpoint-ы.
- релизный прогон worker-контракта обязателен и на симуляторе, и минимум на одной реальной интеграции.

## 6. Связанные документы
- `docs/spec/technical-specification.md`
- `docs/testing/test-strategy.md`
- `docs/testing/contract-gates-execution.md`
- `docs/testing/internal-contract-checklist.md`
- `docs/testing/worker-contract-checklist.md`
- `docs/testing/test-worker-checklist.md`
- `docs/standards/openapi-governance.md`
- `docs/standards/test-worker-governance.md`
- `docs/standards/technology-stack.md`
- `docs/state-machines/subscription-state-machine.md`
- `docs/standards/runtime-configuration.md`
