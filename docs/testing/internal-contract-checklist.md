# DDCRM — Internal Contract Checklist

## Назначение
Операционный checklist для разработки и тестирования internal service-to-service API по контракту OpenAPI 3.1.

## Источники истины
- `docs/api-contracts/openapi-internal.yaml`
- `docs/standards/openapi-governance.md`
- `docs/standards/runtime-configuration.md`

## 1. Базовая контрактная проверка
- OpenAPI-спека `openapi-internal.yaml` валидна и не содержит битых `$ref`;
- реализация покрывает все обязательные internal endpoint-ы;
- mutating endpoint-ы поддерживают `Idempotency-Key` (кроме webhook-callback endpoint-ов).

## 2. Service-auth проверка
- internal API требует `X-Service-Token` для всех endpoint-ов;
- запросы без токена и с невалидным токеном отклоняются;
- runtime-настройки service-auth заданы по `docs/standards/runtime-configuration.md`.
- internal service-auth токены не пересекаются с worker service-auth токенами.

## 3. Интеграционные проверки
- route/lifecycle endpoint-ы согласованы с актуальной логикой Accounts Manager;
- billing/entitlement endpoint-ы согласованы с актуальной логикой коммерческого контура;
- IAM endpoint-ы согласованы с текущей моделью membership/permission.

## 4. Совместимость и регресс
- backward compatibility проверена для `openapi-internal.yaml`;
- новые поля и операции добавляются как backward-compatible изменения;
- breaking-изменения сопровождаются миграционным планом и обновлением зависимых contract-tests.

## 5. Минимальный набор негативных тестов
- `401/403` при отсутствии/некорректном `X-Service-Token`;
- `401/403` при попытке вызвать internal API токеном worker-контура;
- `409` конфликт состояния для конфликтных mutating-операций;
- `400/422` на невалидное тело запроса и несовместимый schema payload;
- идемпотентный повтор mutating-запроса не приводит к повторному побочному эффекту.
