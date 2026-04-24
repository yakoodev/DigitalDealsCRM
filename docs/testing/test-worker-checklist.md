# DDCRM — Test Worker Checklist

## Назначение
Операционный checklist использования тестового воркера в `local/CI/staging` для проверки worker-контракта и диагностики ошибок интеграции.

## Источник истины
- `docs/standards/test-worker-governance.md`

## 1. Включение симулятора
- подтверждено, что контур запуска не `production`;
- загружен env-профиль `.env.test-worker.example` с валидными значениями;
- `TEST_WORKER_ENABLED=true` и `TEST_WORKER_SCENARIO` установлен в требуемый `TW-SCN-*`;
- `TEST_WORKER_BLOCK_IN_PRODUCTION=true`.

## 2. Проверка профиля возможностей
- активный capability-профиль (`TW-CAP-*`) опубликован в `/internal/v1/worker/capabilities`;
- выбранный сценарий совместим с активным capability-профилем;
- тесты Gateway не отправляют action вне объявленного capability-набора.

## 3. Обязательные сценарии прогона
- `TW-SCN-HAPPY-PATH`;
- `TW-SCN-AUTH-FAIL`;
- `TW-SCN-TIMEOUT`;
- `TW-SCN-CAPABILITY-MISMATCH`;
- `TW-SCN-IDEMPOTENCY-REPLAY`;
- `TW-SCN-TRANSIENT-ERROR`;
- `TW-SCN-MALFORMED-PAYLOAD`;
- `TW-SCN-CONTRACT-DRIFT`.

## 4. Диагностика: ошибка симуляции vs ошибка платформы
- если воспроизводится только на одном `fixture revision`, классифицировать как проблему симуляции/фикстуры;
- если воспроизводится на нескольких `fixture revision` и на реальной интеграции, классифицировать как проблему платформенного кода;
- если ошибка возникает только при `ext.test.*`, проверить capability и ограничение non-production профиля;
- результат диагностики фиксируется в отчёте прогона (`scenario ID`, `capability profile`, `fixture revision`, `trace/request ID`).

## 5. Ограничения использования
- тестовый воркер не используется как production integration;
- `ext.test.*` действия недоступны для production-профиля;
- любые изменения сценариев/фикстур сопровождаются обновлением contract-tests.
