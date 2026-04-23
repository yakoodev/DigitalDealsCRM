# Core DB

## Назначение
Основное хранилище бизнес-данных DDCRM.

## Общий принцип
Core DB хранит:
- бизнес-данные
- проектные роли
- подписки и коммерческие сущности
- entitlement snapshot
- route/binding-данные для текущей архитектуры

## Основные группы данных

### Пользователи и проекты
- users
- projects
- project members
- project roles

### Аккаунты площадок
- accountId
- platform
- projectId
- display name
- business status
- технический статус snapshot

### Route / Binding metadata
- routeKey
- связь routeKey ↔ accountId
- связь accountId ↔ worker binding

### Billing / Subscription data
- plans
- add-ons
- subscriptions
- payments
- payment history
- refunds
- manual billing actions

### Entitlement data
- allowed platforms
- max platform/account counts
- current usage
- project blocked flag
- grace period info
- trial info
- history of entitlement changes

## Логическое деление
База логически разделяется на:
- бизнес-данные
- технические данные маршрутизации
- коммерческие данные
- entitlement-данные
