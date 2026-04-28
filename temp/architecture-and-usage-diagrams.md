# DDCRM — Диаграммы использования и общей архитектуры

## 1) Общая архитектура
```mermaid
flowchart LR
    FE["Frontend"]
    CORE["Core API"]
    IAM["Identity and Access"]
    BILL["Billing"]
    ENT["Entitlement Service"]
    AM["Accounts Manager"]
    GW["Account API Gateway"]

    CDB[("Core DB")]
    AMDB[("Accounts Manager DB")]
    RRDB[("Route Registry DB")]
    WSTATE[("Worker State Storage")]

    W1["Worker A"]
    W2["Worker B"]
    PAY["Acquiring Provider"]

    FE --> CORE
    FE --> GW

    CORE --> IAM
    CORE --> BILL
    CORE --> ENT
    CORE --> AM
    CORE --> CDB

    IAM --> CDB
    BILL --> CDB
    BILL --> PAY
    BILL --> ENT

    AM --> AMDB
    AM --> W1
    AM --> W2
    AM --> GW

    GW --> RRDB
    GW --> ENT
    GW --> W1
    GW --> W2

    W1 --> WSTATE
    W2 --> WSTATE
```

## 2) Диаграмма использования (роли -> действия)
```mermaid
flowchart TB
    OWNER["Owner"]
    ADMIN["Admin"]
    MOD["Moderator"]

    UC1["Управлять участниками и ролями"]
    UC2["Lifecycle аккаунта (create update delete migrate)"]
    UC3["Оперировать существующими worker"]
    UC4["Смотреть masked proxy credentials"]
    UC5["Reveal и update proxy credentials"]
    UC6["Смотреть финансы и менять тариф"]

    OWNER --> UC1
    OWNER --> UC2
    OWNER --> UC3
    OWNER --> UC4
    OWNER --> UC5
    OWNER --> UC6

    ADMIN --> UC1
    ADMIN --> UC2
    ADMIN --> UC3
    ADMIN --> UC4
    ADMIN --> UC5
    ADMIN --> UC6

    MOD --> UC3
    MOD --> UC4

    MOD -. "Запрещено" .-> UC1
    MOD -. "Запрещено" .-> UC2
    MOD -. "Запрещено" .-> UC5
    MOD -. "Запрещено" .-> UC6
```

## 3) Сценарий: Операционный вызов к worker через Gateway
```mermaid
sequenceDiagram
    participant U as "User (Owner Admin Moderator)"
    participant FE as "Frontend"
    participant GW as "Gateway"
    participant ENT as "Entitlement"
    participant RR as "Route Registry"
    participant W as "Worker"

    U->>FE: "Запустить операцию"
    FE->>GW: "POST /v1/account-api/{routeKey}/{action}"
    GW->>ENT: "Проверить доступ role + entitlement"
    ENT-->>GW: "allowed or denied"

    alt "allowed"
        GW->>RR: "resolve routeKey"
        RR-->>GW: "worker binding"
        GW->>W: "proxy request"
        W-->>GW: "result"
        GW-->>FE: "success response"
    else "denied"
        GW-->>FE: "forbidden"
    end
```

## 4) Сценарий: Создание аккаунта (owner admin)
```mermaid
sequenceDiagram
    participant U as "Owner or Admin"
    participant CORE as "Core"
    participant ENT as "Entitlement"
    participant AM as "Accounts Manager"
    participant W as "Worker"
    participant RR as "Route Registry"

    U->>CORE: "Create account with proxy config"
    CORE->>ENT: "Проверить лимиты и доступ платформы"
    ENT-->>CORE: "allowed"
    CORE->>AM: "Lifecycle create with idempotency key"
    AM->>W: "Provision worker"
    W-->>AM: "ready"
    AM->>RR: "route upsert"
    RR-->>AM: "saved"
    AM-->>CORE: "created"
    CORE-->>U: "account created"
```

## 5) Сценарий: Оплата -> активация доступа
```mermaid
sequenceDiagram
    participant U as "User"
    participant CORE as "Core"
    participant BILL as "Billing"
    participant PAY as "Acquiring"
    participant ENT as "Entitlement"

    U->>CORE: "Оплатить тариф или add-on"
    CORE->>BILL: "create payment"
    BILL->>PAY: "charge"
    PAY-->>BILL: "webhook success"
    BILL->>BILL: "dedup webhook"
    BILL->>ENT: "recalculate entitlement"
    ENT-->>BILL: "new access snapshot"
    BILL-->>CORE: "subscription updated"
    CORE-->>U: "доступ активирован"
```

## 6) Сценарий: Reveal proxy credentials
```mermaid
sequenceDiagram
    participant U as "Owner or Admin"
    participant FE as "Frontend"
    participant CORE as "Core API"
    participant AUD as "Audit"

    U->>FE: "Запросить reveal credentials"
    FE->>CORE: "POST /proxy-credentials/reveal with reason"
    CORE->>CORE: "Проверить active session + RBAC"
    CORE->>AUD: "log actor reason requestId timestamp"
    AUD-->>CORE: "logged"
    CORE-->>FE: "full proxy config"
```

## 7) Сценарий: Auth токены internal и worker (изоляция)
```mermaid
flowchart LR
    subgraph Internal["Internal API Auth"]
        IACC["INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS"]
        ICLI["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"]
    end

    subgraph Worker["Worker API Auth"]
        WACC["WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS"]
        WCLI["WORKER_API_SERVICE_AUTH_CLIENT_TOKEN"]
    end

    ICLI --> IACC
    WCLI --> WACC

    IACC -. "no overlap" .- WACC
    ICLI -. "must differ" .- WCLI
```
