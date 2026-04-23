# DDCRM — Runbook (черновик)

Канонический источник порогов, SLO и recovery-критериев:
- `docs/standards/quality-gates.md`

## 0. Целевые SLO и пороги инцидентов
- инцидентные пороги: `QG-INC-SEV1-5XX-RATE`, `QG-INC-SEV1-ROUTE-NOT-FOUND-RATE`, `QG-INC-SEV1-WEBHOOK-OLDEST-AGE`, `QG-INC-SEV2-GW-CHECK-P95`, `QG-INC-SEV2-IAM-CACHE-P99`
- операционные SLA реакции: `QG-OPS-MTTA-SEV1`, `QG-OPS-MITIGATION-START-SEV1`

## 1. Инцидент: недоступен Gateway
### Симптомы
- массовые ошибки account API
- рост `5xx` на Gateway

### Действия
- проверить health-check Gateway
- проверить доступность Route Registry DB
- проверить доступность Entitlement Service
- при необходимости переключить трафик на последнюю стабильную версию Gateway

### Критерий восстановления
- `QG-REC-GW-5XX-RATE`
- `QG-REC-GW-CHECK-P95`

## 2. Инцидент: route не резолвится
### Симптомы
- `ROUTE_NOT_FOUND` при активных аккаунтах

### Действия
- проверить запись в Route Registry DB
- сравнить route version с последним lifecycle событием
- повторить route upsert через Accounts Manager -> Gateway

### Критерий восстановления
- `QG-REC-ROUTE-NOT-FOUND-RATE`
- `QG-REC-ROUTE-RESOLVE-P95`

## 3. Инцидент: дубль webhook платежа
### Симптомы
- повторные попытки активации entitlement

### Действия
- проверить dedup-ключ события в Billing
- убедиться, что повтор не изменил итоговый статус подписки
- при конфликте запустить reconciliation

### Критерий восстановления
- `QG-REC-WEBHOOK-P95`
- `QG-REC-WEBHOOK-OLDEST-AGE`
- повторные webhook не изменяют состояние подписки повторно

## 4. Инцидент: некорректный override
### Симптомы
- доступы проекта не соответствуют платежному статусу

### Действия
- проверить запись override: reason/actor/createdAt/expiresAt
- проверить, не истёк ли override
- если override некорректен, завершить его и пересчитать entitlement

### Критерий восстановления
- все активные override валидны по полям `reason/actor/createdAt/expiresAt`
- `QG-REC-ENT-RECALC-P95`

## 5. Инцидент: массовая блокировка после даунгрейда
### Симптомы
- пользователи теряют доступ к операциям на разрешённых платформах

### Действия
- проверить правила entitlement для тарифа
- проверить, что Gateway применяет выборочную блокировку
- при ошибке применить временный admin override с коротким TTL и аудитом

### Критерий восстановления
- `QG-REC-FALSE-BLOCKED-RATE`
- нет роста `ENTITLEMENT_BLOCKED` для разрешённых action

## 6. Эскалация
- L1: on-call инженер
- L2: владелец сервиса (Billing/Entitlement/Gateway)
- L3: продукт + техлид при коммерческом риске
- `QG-OPS-ESCALATE-L2-SEV1`
- `QG-OPS-ESCALATE-L3-SEV1`
