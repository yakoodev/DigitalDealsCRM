# Основные потоки данных

## 1. Создание аккаунта

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant CORE as Core
    participant ENT as Entitlement Service
    participant AM as Accounts Manager
    participant GATE as Account API Gateway
    participant W as Account Worker

    FE->>CORE: create account
    CORE->>ENT: check platform type + platform count limits
    ENT-->>CORE: allowed / denied
    CORE->>AM: lifecycle create
    AM->>W: create runtime
    W-->>AM: runtime ready
    AM->>GATE: register route
    GATE-->>AM: route ready
    AM-->>CORE: account created
    CORE-->>FE: account created result
```

## 2. Изменение аккаунта

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant CORE as Core
    participant AM as Accounts Manager
    participant W as Account Worker
    participant GATE as Account API Gateway

    FE->>CORE: update account
    CORE->>AM: lifecycle update
    AM->>W: apply new config
    W-->>AM: updated
    AM->>GATE: update route if needed
    GATE-->>AM: route updated
    AM-->>CORE: update result
    CORE-->>FE: updated
```

## 3. Удаление аккаунта

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant CORE as Core
    participant AM as Accounts Manager
    participant GATE as Account API Gateway
    participant W as Account Worker

    FE->>CORE: delete account
    CORE->>AM: lifecycle delete
    AM->>W: shutdown/delete
    W-->>AM: deleted
    AM->>GATE: remove route
    GATE-->>AM: route removed
    AM-->>CORE: deleted
    CORE-->>FE: delete result
```

## 4. Получение списка аккаунтов и лимитов

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant CORE as Core
    participant DB as Core DB

    FE->>CORE: get workspace
    CORE->>DB: read users/projects/accounts/routes/subscription/limits
    DB-->>CORE: workspace data
    CORE-->>FE: projects + accounts + routeKey + tariff + entitlements + usage
```

## 5. Вызов account API

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant GATE as Account API Gateway
    participant DB as Core DB
    participant ENT as Entitlement Service
    participant W as Account Worker

    FE->>GATE: routeKey + token + action + payload
    GATE->>DB: resolve route and access metadata
    DB-->>GATE: worker binding + project metadata
    GATE->>ENT: check subscription/entitlement/block status
    ENT-->>GATE: allowed / blocked
    GATE->>W: proxied request
    W-->>GATE: api response
    GATE-->>FE: result
```

## 6. Перенос worker-а

```mermaid
sequenceDiagram
    participant CORE as Core
    participant AM as Accounts Manager
    participant W1 as Old Worker
    participant W2 as New Worker
    participant GATE as Account API Gateway

    CORE->>AM: migrate account worker
    AM->>W2: create runtime on new server
    W2-->>AM: new runtime ready
    AM->>GATE: switch route
    GATE-->>AM: route switched
    AM->>W1: shutdown old runtime
    W1-->>AM: old runtime stopped
    AM-->>CORE: migration completed
```

## 7. Оплата тарифа / add-on

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant CORE as Core
    participant BILL as Billing
    participant PAY as Acquiring
    participant ENT as Entitlement Service

    FE->>CORE: buy plan / addon
    CORE->>BILL: create payment
    BILL->>PAY: create acquiring payment
    PAY-->>BILL: payment session
    BILL-->>CORE: payment info
    CORE-->>FE: payment link/session

    PAY-->>BILL: payment success
    BILL->>ENT: activate purchased entitlements
    ENT-->>BILL: provisioning done
```

## 8. Блокировка из-за неоплаты

```mermaid
sequenceDiagram
    participant BILL as Billing
    participant ENT as Entitlement Service
    participant GATE as Account API Gateway
    participant FE as Frontend

    BILL->>ENT: subscription expired / unpaid
    ENT-->>GATE: project blocked
    FE->>GATE: routeKey + token + action
    GATE-->>FE: blocked by subscription / entitlement
```

## 9. Базовые error cases
- worker недоступен
- route устарел
- proxy / auth проблема на стороне worker
- entitlement не позволяет подключить платформу
- превышен hard limit
- проект заблокирован из-за неоплаты
