# DDCRM — Worker Action Conventions (канонический)

## Назначение
Единый источник правил именования и использования `action`-ключей в Gateway proxy и worker extension API.

Правило:
- формат и таксономия `action` определяются только здесь;
- в остальных документах используются ссылки на этот стандарт без копирования правил.

## 1. Формат ключа
- паттерн: `^[a-z0-9._-]+$`
- разделитель namespace: `.`
- регистр: только lowercase

## 2. Канонические namespace
- `listings.*` — операции над объявлениями
- `messages.*` — операции над сообщениями
- `orders.*` — операции над заказами
- `ext.*` — платформенно-специфичные extension операции
- `ext.account.lifecycle.*` — extension-операции lifecycle аккаунта (чувствительные)
- `ext.account.proxy-credentials.*` — extension-операции управления proxy credentials (чувствительные)
- `ext.test.*` — симуляторные extension-операции только для non-production контуров
- `ext.account.proxy-credentials.apply` — технический action синхронизации credentials в worker state storage (service-to-service)

## 3. Политика использования
- типовые операции должны реализовываться ресурсными endpoint-ами worker API;
- `actions/{action}` используется только для extension операций, не покрытых типовыми endpoint-ами;
- extension ключи обязаны иметь префикс `ext.`.
- `ext.test.*` разрешён только для тестового воркера по `docs/standards/test-worker-governance.md`.
- чувствительные `ext.account.*` action-key должны проверяться через RBAC-сопоставление из `docs/standards/access-control-matrix.md` до проксирования.

## 4. Capability-согласование
- любой `ext.*` action должен иметь соответствующий capability-флаг в `/internal/v1/worker/capabilities`;
- Gateway не должен проксировать action, который не объявлен capability-набором worker-а.

## 5. Изменения и совместимость
- добавление нового `action` должно быть backward-compatible для существующих ключей;
- переименование `action` допускается только с миграционным периодом и dual-support;
- удаление `action` возможно только после удаления зависимости в Gateway/clients/tests.
- `ext.test.*` не должен использоваться как часть production-бизнес-флоу.

## 6. Связанные документы
- `docs/standards/openapi-governance.md`
- `docs/standards/test-worker-governance.md`
- `docs/testing/worker-contract-checklist.md`
- `docs/testing/test-worker-checklist.md`
