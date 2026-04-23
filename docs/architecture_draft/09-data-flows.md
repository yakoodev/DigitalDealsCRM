# Основные потоки данных

## 1. Создание аккаунта

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant CORE as Core
    participant ENT as Entitlement Service
    participant AM as Accounts Manager
    participant GATE as Account API Gateway
    participant RDB as Route Registry DB
    participant W as Account Worker

    FE->>CORE: create account + proxy config
    CORE->>ENT: check platform type + hard limits + project state
    ENT-->>CORE: allowed / denied
    CORE->>AM: lifecycle create (idempotency key)
    AM->>W: create runtime with proxy
    W-->>AM: runtime ready
    AM->>GATE: upsert route(accountId, projectId, workerBinding)
    GATE->>RDB: save route
    RDB-->>GATE: route saved
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
    participant RDB as Route Registry DB

    FE->>CORE: update account + proxy config
    CORE->>AM: lifecycle update (idempotency key)
    AM->>W: apply new config
    W-->>AM: updated
    AM->>GATE: update route if needed
    GATE->>RDB: update route
    RDB-->>GATE: route updated
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
    participant RDB as Route Registry DB
    participant W as Account Worker

    FE->>CORE: delete account
    CORE->>AM: lifecycle delete (idempotency key)
    AM->>W: shutdown/delete
    W-->>AM: deleted
    AM->>GATE: remove route
    GATE->>RDB: delete route
    RDB-->>GATE: route removed
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
    participant GATE as Account API Gateway
    participant RDB as Route Registry DB

    FE->>CORE: get workspace
    CORE->>DB: read users/projects/accounts/subscription/limits
    DB-->>CORE: workspace business data
    CORE->>GATE: get route keys for account list
    GATE->>RDB: resolve routes by accountId
    RDB-->>GATE: route list
    GATE-->>CORE: route keys
    CORE-->>FE: projects + accounts + routeKey + tariff + entitlements + usage
```

## 5. Вызов account API

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant GATE as Account API Gateway
    participant RDB as Route Registry DB
    participant ENT as Entitlement Service
    participant W as Account Worker

    FE->>GATE: routeKey + token + action + payload
    GATE->>RDB: resolve route and project/account metadata
    RDB-->>GATE: worker binding + metadata
    GATE->>ENT: check entitlement state for action
    ENT-->>GATE: allowed / blocked / partially_allowed
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
    participant RDB as Route Registry DB

    CORE->>AM: migrate account worker (idempotency key)
    AM->>W2: create runtime on new server
    W2-->>AM: new runtime ready
    AM->>GATE: switch route
    GATE->>RDB: replace worker binding
    RDB-->>GATE: route switched
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

    FE->>CORE: buy plan / add-on
    CORE->>BILL: create payment (idempotency key)
    BILL->>PAY: create acquiring payment
    PAY-->>BILL: payment session
    BILL-->>CORE: payment info
    CORE-->>FE: payment link/session

    PAY-->>BILL: payment webhook callback
    BILL->>BILL: deduplicate + validate event
    BILL->>ENT: send updated subscription state
    ENT-->>BILL: entitlement recalculated
```

## 8. Неоплата и ограничение доступа

```mermaid
sequenceDiagram
    participant BILL as Billing
    participant ENT as Entitlement Service
    participant GATE as Account API Gateway
    participant FE as Frontend

    BILL->>ENT: subscription unpaid/expired
    ENT-->>GATE: updated project state (grace/blocked/partially_allowed)
    FE->>GATE: routeKey + token + action
    GATE-->>FE: allowed or blocked based on action entitlement
```

## 9. Базовые error cases
- worker недоступен
- route устарел или отсутствует
- stale membership cache в Gateway
- proxy/auth проблема на стороне worker
- entitlement не позволяет подключить платформу
- превышен hard limit
- webhook дублируется

## 10. Технические гарантии
- mutating-операции выполняются с `idempotency key`
- webhook обрабатываются с дедупликацией
- route-операции версионируются
