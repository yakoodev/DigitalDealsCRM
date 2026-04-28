# Поток подписки и оплаты

## Основной поток

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant CORE as Core
    participant BILL as Billing
    participant PAY as Acquiring
    participant ENT as Entitlement Service
    participant DB as Core DB

    FE->>CORE: купить тариф / add-on
    CORE->>BILL: create payment request
    BILL->>PAY: create payment
    PAY-->>BILL: payment session / url
    BILL-->>CORE: payment info
    CORE-->>FE: payment session / url

    PAY-->>BILL: payment success callback
    BILL->>BILL: validate + deduplicate webhook
    BILL->>ENT: publish subscription state update
    ENT->>DB: update entitlement snapshot
    DB-->>ENT: ok
    ENT-->>BILL: provisioning completed
```

## Что должно включаться после оплаты
- тариф проекта
- add-on
- доступные платформы
- лимиты количества площадок / аккаунтов
- модули проекта
- trial / paid state
- блокировки и снятие блокировок

## State machine доступа
- состояние доступа ведёт Entitlement Service
- переходы `trial -> active -> grace -> blocked`
- длительности `trial/grace` задаются политикой тарифа

## Ручной сценарий
Архитектура должна поддерживать ручное подтверждение платежа и ручную активацию доступа вне эквайринга.

## Источник истины
- факт оплаты и состояние подписки: Billing
- итоговые разрешения проекта: Entitlement Service
