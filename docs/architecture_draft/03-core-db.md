# Core DB

## Назначение
Основное хранилище бизнес-данных DDCRM.

## Общий принцип
Core DB хранит:
- бизнес-данные
- проектные роли
- read-модели подписок и коммерческих сущностей
- entitlement snapshot для UI/отчётности

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

### Billing / Subscription data
- plans
- add-ons
- subscriptions (projection)
- payments (projection)
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
- коммерческие данные
- entitlement-данные

## Важная оговорка
- Core DB **не** хранит канонические route/binding-данные.  
- Каноническая маршрутизация хранится в отдельной Route Registry DB.
