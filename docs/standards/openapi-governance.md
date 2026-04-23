# DDCRM — OpenAPI Governance (канонический)

## Назначение
Единый источник правил качества и совместимости OpenAPI-контрактов DDCRM.

Правило:
- обязательные правила проектирования контрактов и contract quality gates определяются только здесь;
- остальные документы ссылаются на этот стандарт без копирования полных правил.

## 1. Контуры и источники истины
- `docs/api-contracts/openapi-common.yaml` — общие компоненты;
- `docs/api-contracts/openapi-external.yaml` — публичный API;
- `docs/api-contracts/openapi-internal.yaml` — внутренний service-to-service API;
- `docs/api-contracts/openapi-worker.yaml` — унифицированный контракт worker API.

## 1.1 Runtime env-источники
- runtime env-источники и их конвенции определяются в `docs/standards/runtime-configuration.md`;
- в OpenAPI-спеках разрешены только ссылки на env-источник, без хардкода environment-специфичных значений.

## 2. Обязательные правила контракта
- формат спецификаций: только OpenAPI 3.1;
- security-схемы должны быть явно описаны для каждого контура API;
- для `internal` контура `service-auth` через `X-Service-Token` обязателен для всех endpoint-ов, runtime-значения задаются по `docs/standards/runtime-configuration.md`;
- для `worker` контура `service-auth` через `X-Service-Token` обязателен для всех endpoint-ов, runtime-значения задаются по `docs/standards/runtime-configuration.md`;
- mutating-операции обязаны принимать `Idempotency-Key`, кроме входящих webhook-callback endpoint-ов;
- ответы об ошибках должны использовать единый error envelope из `openapi-common.yaml`;
- критические flow обязаны иметь `examples` для request/response;
- любое изменение поведения API сначала фиксируется в OpenAPI-спеке, затем в реализации.

## 3. CORS-политика внешнего API
- `external` контур обязан поддерживать CORS для browser-клиентов;
- в `docs/api-contracts/openapi-external.yaml` (корневой `x-cors`) хранится только ссылка на env-источник;
- канонические runtime-значения CORS задаются по `docs/standards/runtime-configuration.md`;
- preflight (`OPTIONS`) должен корректно обрабатываться Gateway;
- CORS должен работать по allowlist origins и не открываться wildcard-правилом в production.

## 4. Правила worker API
- типовые операции worker-а реализуются через ресурсные endpoint-ы (`account/listings/messages/orders`);
- `actions/{action}` используется только для extension-операций;
- extension action-key обязан соответствовать правилам `docs/standards/worker-action-conventions.md`;
- каждый extension action обязан быть согласован с capability-набором worker-а;
- Gateway не должен проксировать action, который не объявлен capability-набором worker-а.

## 5. Политика совместимости
- совместимыми считаются изменения вида: добавление новых optional-полей, новых endpoint-ов, новых enum-значений и новых error-кодов;
- breaking change: удаление/переименование endpoint-а, удаление/переименование поля, перевод optional-поля в required, несовместимое изменение типа или семантики;
- каждый breaking change требует миграционного плана и периода dual-support;
- для `external` и `worker` контуров breaking changes запрещены без отдельного согласования и миграции;
- для `worker` контура обратная совместимость обязательна в рамках минорного релиза платформы.

## 6. Contract Quality Gates
- `OAG-VAL-OPENAPI31`: все OpenAPI-спеки валидны и не содержат битых `$ref`;
- `OAG-LINT-STYLE`: контракты проходят style/lint проверки проекта;
- `OAG-BREAKING-EXTERNAL`: нет нерегламентированных breaking-изменений в `openapi-external.yaml`;
- `OAG-BREAKING-WORKER`: нет нерегламентированных breaking-изменений в `openapi-worker.yaml`;
- `OAG-TEST-CONTRACT-SERVICES`: contract-tests сервисов зелёные для `external/internal`;
- `OAG-TEST-CONTRACT-WORKER`: contract-tests worker-реализаций зелёные;
- `OAG-TEST-CAPABILITY-ACTION`: extension action и capability-набор согласованы;
- `OAG-TEST-INTERNAL-SERVICE-AUTH`: internal API недоступен без валидного `X-Service-Token`;
- `OAG-TEST-WORKER-SERVICE-AUTH`: worker API недоступен без валидного `X-Service-Token`;
- `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`: internal и worker service-auth токены изолированы по `docs/standards/runtime-configuration.md`;
- `OAG-TEST-CORS-EXTERNAL`: CORS/preflight проверки для `external` API зелёные.

## 7. Процесс изменения контракта
- обновить соответствующую OpenAPI-спеку;
- при изменении runtime-настроек обновить соответствующий env-шаблон;
- при изменении правил совместимости/качества обновить этот стандарт;
- обновить и запустить contract-tests;
- подтвердить прохождение quality gates из раздела 6;
- обновить связанные документы ссылками, без дублирования правил.

## 8. Связанные документы
- `docs/api-contracts/api-contracts.md`
- `docs/testing/test-strategy.md`
- `docs/testing/internal-contract-checklist.md`
- `docs/testing/worker-contract-checklist.md`
- `docs/spec/technical-specification.md`
- `docs/standards/documentation-governance.md`
- `docs/standards/runtime-configuration.md`
