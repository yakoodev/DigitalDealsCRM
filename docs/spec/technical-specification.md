# DDCRM — Техническое задание (черновик v1)

## 1. Цель документа
Зафиксировать требования к полной реализации DDCRM до начала активной разработки.

## 2. Границы продукта
### Входит
- управление пользователями, проектами и ролями
- подключение и lifecycle аккаунтов площадок
- единый прокси-доступ к account API через Gateway
- биллинг: тарифы, add-on, подписка, платежи, возвраты
- entitlement: лимиты, доступы, блокировки, `trial/grace`
- системная админка DDCRM с ручными override
- будущие продуктовые модули: товары, inbox, заказы, аналитика, автоматизация

### Не входит на текущем этапе ТЗ
- конкретные UI-макеты экранов
- привязка к конкретному облачному провайдеру

## 3. Ключевые архитектурные решения
- источник истины по оплате и подписке: **Billing**
- источник истины по итоговым доступам: **Entitlement Service**
- маршрутизация `routeKey -> worker binding`: отдельная **Route Registry DB**
- проверка доступа в Gateway: токен + claims + membership/role cache
- прокси обязателен для каждого подключаемого аккаунта площадки
- proxy credentials и session secrets хранятся только в Worker State Storage
- модератор не имеет доступа к финансам
- технологический стек backend/frontend/tooling фиксируется канонически в `docs/standards/technology-stack.md`

## 4. Термины
- `Проект` — изолированный коммерческий и доступный контур
- `Аккаунт площадки` — подключение конкретного seller account к проекту
- `Worker` — runtime одного аккаунта площадки
- `RouteKey` — публичный идентификатор маршрута к worker
- `Entitlement` — итоговый набор доступов и ограничений проекта
- `Override` — ручное административное изменение entitlement/подписки

## 5. Функциональные требования
### 5.1 Пользователи, проекты, роли
- пользователь может состоять в нескольких проектах
- в проекте ровно один владелец
- роли проекта и матрица прав определяются в `docs/standards/access-control-matrix.md`
- роли валидируются и в Core, и в Gateway

### 5.2 Подключение аккаунтов площадок
- один worker обслуживает ровно один аккаунт
- создание/изменение аккаунта требует proxy-конфиг
- при create/update/delete/migrate операции должны быть идемпотентны
- удаление аккаунта удаляет route и останавливает worker
- политика доступа к proxy credentials (masked by default, reveal/update, активная сессия, аудит) определяется канонически в `docs/standards/access-control-matrix.md`
- реализация каждого worker обязана соответствовать контракту `docs/api-contracts/openapi-worker.yaml`
- типовые операции worker (account/listings/messages/orders) реализуются через ресурсные endpoint-ы
- платформенно-специфичные операции worker допускаются только через extension endpoint `actions/{action}` с capability-флагами
- правила формата и таксономии `action` определяются в `docs/standards/worker-action-conventions.md`

### 5.3 Маршрутизация
- каноническая запись route хранится только в Route Registry DB
- route-операции версионируются
- Gateway читает route из Route Registry DB при resolve запроса
- Core DB не хранит каноническое binding-состояние

### 5.4 Billing
- Billing создаёт платежи и принимает webhook эквайринга
- webhook проходят валидацию и дедупликацию
- итоговый статус оплаты/подписки фиксируется только в Billing
- поддерживаются сценарии: новая подписка, продление, add-on, отключение add-on, возврат, ручная активация

### 5.5 Entitlement
- Entitlement пересчитывается по статусу подписки, тарифу и add-on
- лимиты hard: превышение не допускается
- при даунгрейде/неоплате блокируются только недоступные по entitlement операции
- существующие аккаунты не удаляются автоматически
- override администратора должен иметь `reason`, `actor`, `createdAt`, `expiresAt`
- все override-изменения журналируются

### 5.6 Trial и Grace
- состояние доступа: `trial -> active -> grace -> blocked`
- значения `trialDays` и `graceDays` задаются политикой тарифа
- без явных `trialDays/graceDays` тариф не может быть активирован

## 6. Нефункциональные требования
Канонический источник всех числовых NFR/SLO, порогов инцидентов и recovery-критериев:
- `docs/standards/quality-gates.md`

Этот документ фиксирует обязательные группы quality gates:
- доступность сервисов
- производительность API
- коммерческие и доступовые потоки
- disaster recovery
- observability и аудит
- пороги классификации инцидентов
- критерии восстановления
- операционные SLA реакции

Обязательное правило изменений:
- изменение числового порога выполняется только в `docs/standards/quality-gates.md`
- в остальных документах используются только ID соответствующих quality gates

## 7. Требования к данным
- бизнес-данные: Core DB
- runtime размещение worker: Accounts Manager DB
- маршрутизация: Route Registry DB
- сессии/секреты/proxy credentials worker: Worker State Storage (шифрование обязательно)
- Core DB хранит только read-снапшоты billing/entitlement

## 8. Безопасность и доступ
- токены пользователя валидируются в Gateway
- role/membership проверяются по claims и membership cache
- cache должен инвалидироваться при изменении ролей
- ограничения UI и API по ролям определяются в `docs/standards/access-control-matrix.md`
- доступ к proxy credentials регулируется `project.accounts.proxyCredentials.reveal` и `project.accounts.proxyCredentials.update`
- CORS для browser-доступа к external API определяется канонически в `docs/standards/openapi-governance.md`, runtime-значения задаются по `docs/standards/runtime-configuration.md`
- service-auth internal API через `X-Service-Token` обязателен, runtime-значения задаются по `docs/standards/runtime-configuration.md`
- service-auth worker API через `X-Service-Token` обязателен, runtime-значения задаются по `docs/standards/runtime-configuration.md`
- service-auth токены internal и worker контуров изолированы и не переиспользуются

## 9. Критерии готовности документации к старту разработки (уровень C)
- архитектура синхронизирована без противоречий
- есть ТЗ
- есть roadmap полной реализации
- есть API-контракты в формате **OpenAPI 3.1** по контурам `external/internal/worker`
- OpenAPI-контракты пригодны для генерации клиентов и контрактных тестов
- правила OpenAPI-совместимости и contract quality gates определены в `docs/standards/openapi-governance.md`
- технологический стек зафиксирован в `docs/standards/technology-stack.md`
- числовые NFR/SLO зафиксированы в `docs/standards/quality-gates.md`
- runbook и rollout/rollback используют ID quality gates без дублирования числовых значений
- правила doc governance зафиксированы в `docs/standards/documentation-governance.md`
- есть ERD
- есть state machine подписки
- есть тест-стратегия
- есть runbook инцидентов
- есть rollout/rollback план

## 10. Текущие артефакты
- архитектурные черновики: `docs/architecture_draft`
- roadmap: `docs/roadmap/full-product-roadmap.md`
- delivery packages: `docs/implementation/delivery-work-packages.md`
- API (описание): `docs/api-contracts/api-contracts.md`
- API common components: `docs/api-contracts/openapi-common.yaml`
- API external: `docs/api-contracts/openapi-external.yaml`
- API internal: `docs/api-contracts/openapi-internal.yaml`
- API worker: `docs/api-contracts/openapi-worker.yaml`
- quality gates: `docs/standards/quality-gates.md`
- access matrix: `docs/standards/access-control-matrix.md`
- doc governance: `docs/standards/documentation-governance.md`
- ERD: `docs/data-model/erd.md`
- state machine: `docs/state-machines/subscription-state-machine.md`
- тесты: `docs/testing/test-strategy.md`
- запуск contract quality gates: `docs/testing/contract-gates-execution.md`
- эксплуатация: `docs/operations/*`
