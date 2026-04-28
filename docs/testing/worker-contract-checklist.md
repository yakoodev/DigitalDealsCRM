# DDCRM — Worker Contract Checklist

## Назначение
Операционный checklist для разработки и тестирования worker-реализаций по контракту OpenAPI 3.1.

## Источники истины
- `docs/api-contracts/openapi-worker.yaml`
- `docs/standards/openapi-governance.md`
- `docs/standards/worker-action-conventions.md`
- `docs/standards/runtime-configuration.md`
- `docs/standards/test-worker-governance.md`

## 1. Базовая контрактная проверка
- OpenAPI-спека `openapi-worker.yaml` валидна и не содержит битых `$ref`;
- реализация worker-а покрывает обязательные resource endpoint-ы `v2` (`account/conversations/products`) и extension endpoint `actions/{action}`;
- `products.create` валидирует `schemaId` и обязательные provider-специфичные поля по `schemas/products`;
- все mutating endpoint-ы поддерживают `Idempotency-Key`.

## 2. Service-auth проверка
- worker API требует `X-Service-Token` для всех endpoint-ов;
- запросы без токена и с невалидным токеном отклоняются;
- runtime-настройки service-auth заданы по `docs/standards/runtime-configuration.md`.
- worker service-auth токены не пересекаются с internal service-auth токенами.

## 3. Capability и extension проверки
- `/internal/v2/worker/capabilities` публикует актуальный capability-набор;
- provider-профиль (`funpay/playerok/ggsell/platimarket`) консистентен между `/capabilities`, `/account` и `/schemas/products`;
- resource endpoint-ы `account/conversations/products` отклоняют операции с `features=false` через `409/WORKER_RUNTIME_CONFLICT`;
- extension endpoint принимает только `ext.*` action-key;
- Gateway не вызывает extension action без соответствующего capability.

## 4. Совместимость и регресс
- backward compatibility проверена для `openapi-worker.yaml`;
- новые поля и операции добавляются как backward-compatible изменения;
- breaking-изменения допускаются только с миграционным планом, версионированием и обновлением contract gates.

## 5. Минимальный набор негативных тестов
- `401/403` при отсутствии/некорректном `X-Service-Token`;
- `401/403` при попытке вызвать worker API токеном internal-контура;
- `WORKER_INVALID_ACTION` для неподдерживаемого `action`;
- `409` конфликт состояния runtime для конфликтных mutating-операций;
- отклонение extension action, отсутствующего в capability-наборе.

## 6. Тестовый воркер (симулятор)
- для прогона через симулятор использовать `docs/testing/test-worker-checklist.md`;
- каталог `TW-SCN-*` и capability-профили `TW-CAP-*` поддерживаются канонически в `docs/standards/test-worker-governance.md`.
