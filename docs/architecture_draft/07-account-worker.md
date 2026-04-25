# Account Worker

## Назначение
Отдельный mini-backend одного аккаунта площадки.

## Основная модель
- один worker = один аккаунт
- у всех worker-ов одинаковая сигнатура API
- worker отвечает за всю работу с площадкой

## Что покрывает API worker-а
Общий контракт покрывает всю работу с площадкой:
- `account.info` (метаданные аккаунта площадки)
- `conversations.list` и `conversations.messages.*` (переписки и сообщения)
- `products.*` и `products.schemas.list` (товары и schema-driven валидация полей)
- extension-операции `ext.*` для platform-specific действий
- любые прочие platform operations только через extension-модель с capability-проверкой

Практика контракта:
- типовые операции (`account/conversations/products`) идут через ресурсные endpoint-ы
- платформенно-специфичные операции идут через extension endpoint `actions/{action}`

## Capability model
У worker-а должны быть capability-флаги, чтобы отражать возможности конкретной площадки.
Capability-модель обязательна для валидации extension-операций.

## Runtime-состояние
Worker хранит своё runtime-состояние во **внешнем хранилище**.

## Прокси
- для каждого аккаунта площадки прокси обязателен
- worker использует proxy-конфиг из runtime-состояния

## С кем общается
- **Account API Gateway**
- **Accounts Manager**
- **Worker State Storage**
- **Внешняя площадка**

## Placement и control-plane
- Accounts Manager ведёт registry worker server-ов (`status/health/capacity/currentLoad/heartbeat`)
- placement worker-а выполняется в Accounts Manager (least-loaded healthy `active` server)
- при миграциях и rebalance именно Accounts Manager переключает route binding и учитывает загрузку server-ов

## Что не делает
- не знает про проекты как доменную сущность
- не знает про бизнес-логику CRM
- не управляет своим lifecycle сам
