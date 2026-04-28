# DDCRM — Test Strategy (черновик)

## 1. Цели
- предотвратить регресс в платежах, entitlement и account lifecycle
- гарантировать корректность доступа по ролям и подписке
- подтвердить устойчивость route и worker lifecycle-процессов
- обеспечить воспроизводимую контрактную проверку worker API через тестовый воркер (симулятор)

## 2. Уровни тестирования
### Unit
- бизнес-правила ролей и прав
- расчет entitlement
- state-machine переходы
- дедуп webhook

### Integration
- Core <-> IAM
- Core <-> Billing
- Billing <-> Entitlement
- Core <-> Accounts Manager
- Gateway <-> Route Registry DB
- Gateway <-> Entitlement
- проверка совместимости реализаций с OpenAPI 3.1 контрактами (`external/internal/worker`)

### E2E
- create/update/delete/migrate аккаунта
- payment success -> entitlement activation
- unpaid -> grace -> blocked
- downgrade и частичная блокировка действий
- override с истечением срока

## 3. Обязательные регрессионные сценарии
- модератор не видит финансовые данные
- модератор не может выполнять `project.accounts.lifecycle.manage` (create/update/delete/migrate)
- модератор может выполнять только `project.workers.operate` для существующих аккаунтов
- proxy credentials в стандартных `accounts` read-response маскированы по умолчанию
- только роли с `project.accounts.proxyCredentials.reveal` могут выполнить явный reveal credentials
- reveal proxy credentials выполняется в рамках активной сессии и без step-up-челленджа (по политике из `docs/standards/access-control-matrix.md`)
- только роли с `project.accounts.proxyCredentials.update` могут изменить credentials
- модератор не может выполнять reveal/update proxy credentials
- Gateway блокирует только запрещённые entitlement-операции
- обязательный proxy-конфиг при создании аккаунта
- route корректно переключается при миграции worker
- повтор webhook не меняет состояние повторно
- worker публикует capability-набор и не принимает неподдерживаемые действия
- типовые worker-операции покрыты ресурсными endpoint-тестами, extension-сценарии покрыты отдельно
- extension endpoint worker-а принимает только `ext.*` action-key
- `ext.test.*` доступен только в non-production профиле тестового воркера
- сценарии `TW-SCN-AUTH-FAIL`, `TW-SCN-TIMEOUT`, `TW-SCN-CONTRACT-DRIFT` проходят с ожидаемой диагностикой
- internal API недоступен без валидного `X-Service-Token`
- worker API недоступен без валидного `X-Service-Token`
- service-auth токены internal и worker контуров не переиспользуются между собой
- `/v1/admin/account-manager/*` недоступен без JWT system-claim `system.accountManager.manage`
- write-only токен registry (`/v1/admin/account-manager/worker-servers`) не возвращается в ответах/ошибках и корректно очищается через explicit clear-flag
- Docker autospawn при отсутствии локального образа выполняет `GHCR pull-if-missing`, а при невалидных credentials возвращает детерминированную configuration error
- action-key в Gateway валидируется по `docs/standards/worker-action-conventions.md`
- external API корректно обрабатывает CORS preflight и применяет allowlist origins
- CORS runtime-настройки external API читаются по `docs/standards/runtime-configuration.md`
- service-auth runtime-настройки internal API читаются по `docs/standards/runtime-configuration.md`
- service-auth runtime-настройки worker API читаются по `docs/standards/runtime-configuration.md`

## 4. Технические требования к тестам
- все mutating API покрываются тестами идемпотентности
- тесты на конфликтные события webhook/внутренних обновлений
- contract-tests между Core/Gateway/AM/Billing/Entitlement
- contract-tests строятся от `docs/api-contracts/openapi-external.yaml`, `docs/api-contracts/openapi-internal.yaml`, `docs/api-contracts/openapi-worker.yaml`
- contract quality gates выполняются по ID из `docs/standards/openapi-governance.md` (`OAG-*`)
- порядок запуска contract quality gates в CI определяется в `docs/testing/contract-gates-execution.md`
- сценарии и capability-профили тестового воркера определяются в `docs/standards/test-worker-governance.md`
- операционный порядок прогона тестового воркера определяется в `docs/testing/test-worker-checklist.md`
- RBAC-тесты строятся от `docs/standards/access-control-matrix.md`
- детальный набор internal contract-checks поддерживается в `docs/testing/internal-contract-checklist.md`
- для worker API обязательны: schema validation запросов/ответов и backward compatibility проверки
- детальный набор worker contract-checks поддерживается в `docs/testing/worker-contract-checklist.md`
- performance/regression тесты обязаны проверять SLO-пороги по ID из `docs/standards/quality-gates.md`
- минимум один DR-тест на релизный цикл обязан подтверждать `QG-DR-RTO-CRITICAL` и `QG-DR-RPO-STATE`

## 5. Входной критерий релиза
- нет блокирующих дефектов в платежных и lifecycle-потоках
- E2E сценарии коммерческого контура зелёные
- регресс роли/доступа зелёный
- обязательные прогоны worker-контракта выполнены и на тестовом воркере, и минимум на одной реальной интеграции
- на staging подтверждены обязательные quality gates для релизного этапа

## 6. Выходная аналитика по тестам
- отчёт по покрытиям критических сценариев
- список известных ограничений и исключений
- список residual risks перед релизом
