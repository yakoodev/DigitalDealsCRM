# DDCRM — Инструкции Для Coding-Агента

## 1. Язык и стиль
- всегда отвечай на русском языке;
- не предлагай архитектурные изменения без явной необходимости и обоснования;
- при противоречиях между документами сначала проверь `docs/standards/source-of-truth-map.md`.

## 2. Канонические документы (читать перед изменениями)
- `docs/standards/source-of-truth-map.md`
- `docs/standards/technology-stack.md`
- `docs/standards/openapi-governance.md`
- `docs/standards/runtime-configuration.md`
- `docs/standards/worker-action-conventions.md`
- `docs/standards/test-worker-governance.md`
- `docs/standards/access-control-matrix.md`
- `docs/testing/contract-gates-execution.md`
- `docs/implementation/delivery-work-packages.md`

## 3. Жёсткие ограничения по стеку
- платформенные сервисы (`Core`, `IAM`, `Accounts Manager`, `Gateway`, `Billing`, `Entitlement`, internal/admin API) пишутся на `C#/.NET` (`ASP.NET Core`);
- frontend пишется на `React + Next.js (App Router) + TypeScript`;
- frontend server-state и API-интеграции: `TanStack Query v5 + Orval` (генерация клиентов из OpenAPI);
- тестовый воркер (симулятор) пишется на `C#/.NET`;
- любые отклонения от стека допускаются только после обновления `docs/standards/technology-stack.md`.

## 4. Архитектурные инварианты
- production API-контракты: источник истины только OpenAPI 3.1 (`docs/api-contracts/*.yaml`);
- для worker-контрактов запрещено вводить несогласованные breaking-изменения;
- `ext.test.*` разрешён только в non-production профиле тестового воркера;
- service-auth токены internal и worker контуров не переиспользуются;
- модератор работает только с уже существующими воркерами и не меняет lifecycle.

## 5. Порядок работы
- перед изменением темы найди канонический документ в `source-of-truth-map`;
- сначала обнови канонический документ, потом зависимые ссылки;
- не дублируй одинаковые правила в нескольких местах;
- если меняешь env-конфигурацию, синхронизируй соответствующий `.env.*.example`;
- если меняешь контракты/worker-поведение, синхронизируй тестовые checklist/gates.

## 6. Минимальный checklist перед завершением задачи
- изменения соответствуют канонике и стеку;
- нет дублирования требований в документации;
- ссылки между документами не сломаны;
- scope изменений ограничен задачей;
- в финальном отчёте явно указано, что изменено и какие ограничения сохранены.

## 7. Тестовые данные для UI/ручной QA
- для тестов разрешено использовать тестовые аккаунты и данные из `F:\ddcrm\тестовые данные.txt`;
- эти данные считаются непроизводственными и допускаются для локальной отладки интерфейса;
- при проверках с FunPay не запускать бесконтрольные циклы запросов и не инициировать массовую/автоматическую отправку сообщений.
