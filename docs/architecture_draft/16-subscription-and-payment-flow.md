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
    BILL->>ENT: activate purchased permissions
    ENT->>DB: update entitlement / subscription snapshot
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

## Ручной сценарий
Архитектура должна поддерживать ручное подтверждение платежа и ручную активацию доступа вне эквайринга.
