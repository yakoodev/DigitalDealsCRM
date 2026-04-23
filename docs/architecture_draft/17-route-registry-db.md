# Route Registry DB

## Назначение
Отдельная БД маршрутизации account API.

## Что хранит
- `routeKey`
- `accountId`
- `projectId`
- текущий `worker binding`
- версия маршрута
- технические флаги актуальности маршрута
- timestamp последнего обновления

## Кто пишет
- Account API Gateway (по lifecycle-командам от Accounts Manager)

## Кто читает
- Account API Gateway (при каждом resolve `routeKey`)

## Что не хранит
- бизнес-данные пользователей и проектов
- платежи и entitlement
- proxy-секреты и сессионные данные worker-ов
