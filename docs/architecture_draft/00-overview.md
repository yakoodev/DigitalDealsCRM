# DDCRM — общий черновик архитектуры v3

## Полное название
**DDCRM (Digital Deals CRM)**

## Формат продукта
SaaS-система для управления продажами цифровых товаров на нескольких площадках.

## Для кого система
- одиночный продавец
- команда продавца
- несколько участников в рамках одного проекта

## Базовая модель
В системе есть пользователи, проекты и подключённые аккаунты площадок.  
Один пользователь может состоять в нескольких проектах.  
В одном проекте может быть несколько аккаунтов одной и той же площадки.  
Каждый проект имеет **собственные тарифы, add-on, ограничения и доступы**.

## Ключевой коммерческий принцип
Настройки и доступы задаются **на уровне проекта**:
- какие площадки разрешены
- сколько аккаунтов каждой площадки можно подключать
- какие модули доступны
- активен ли тестовый период
- действует ли блокировка из-за неоплаты

## Главный архитектурный принцип
Система разделена на:
- **Core** — бизнес-ядро CRM
- **Identity & Access** — роли и доступы пользователей
- **Billing** — источник истины по платежам и подпискам
- **Entitlement Service** — источник истины по итоговым ограничениям и доступам проекта
- **Accounts Manager** — lifecycle аккаунт-воркеров
- **Account API Gateway** — единая точка доступа к API аккаунтов
- **Account Worker** — отдельный mini-backend одного аккаунта площадки
- **Core DB** — бизнес-данные и read-модели
- **Accounts Manager DB** — данные по серверам, подам и lifecycle
- **Route Registry DB** — отдельная БД маршрутизации `routeKey -> worker binding`
- **Worker State Storage** — внешнее хранилище runtime-состояния worker-ов

## Выбранная схема

```mermaid
flowchart LR
    FE[Frontend] --> CORE[Core]
    FE --> GATE[Account API Gateway]

    CORE --> CDB[(Core DB)]
    CORE --> IAM[Identity & Access]
    CORE --> BILL[Billing]
    CORE --> ENT[Entitlement Service]
    CORE --> AM[Accounts Manager]

    BILL --> PAY[Acquiring / Payment Provider]
    BILL --> CDB

    ENT --> CDB
    IAM --> CDB
    IAM --> GATE

    AM --> ADB[(Accounts Manager DB)]
    AM --> GATE
    AM --> W1[FunPay Account Worker]
    AM --> W2[Steam Integration Worker]
    AM --> W3[FunPay Account Worker 2]

    GATE --> RDB[(Route Registry DB)]
    GATE --> ENT
    GATE --> W1
    GATE --> W2
    GATE --> W3

    W1 --> WSTORE[(Worker State Storage)]
    W2 --> WSTORE
    W3 --> WSTORE
```

## Слои ответственности

### Frontend
UI системы. Работает с Core и через Gateway вызывает API аккаунтов.

### Core
Хранит и обслуживает основные бизнес-сущности CRM:
- пользователи
- проекты
- аккаунты площадок
- общие статусы
- бизнес-представление тарифов и ограничений
- подготовку данных для UI

### Identity & Access
Отвечает за:
- проектные роли
- системных админов DDCRM
- membership пользователя в проекте
- проверку прав пользователя
- выдачу данных для проверки доступа в Gateway (через claims и membership cache)

### Billing
Отвечает за:
- тарифы
- add-on
- платежи
- подписки
- продления
- ручные и автоматические платёжные сценарии
- возвраты
- дедупликацию webhook и reconciliation

### Entitlement Service
Отвечает за:
- итоговые разрешения проекта
- доступные платформы
- лимиты по количеству площадок/аккаунтов
- блокировки и частичные ограничения при неоплате
- `trial/grace` по политике тарифа
- историю изменений ограничений

### Accounts Manager
Управляет жизненным циклом аккаунтов:
- создать worker
- изменить worker
- удалить worker
- перенести worker
- обновить конфиг
- балансировать размещение
- выполнять health-check
- инициировать upsert/remove маршрута через Gateway

### Account API Gateway
Отдельный микросервис для:
- проксирования запросов
- проверки токена и role/membership (claims + cache)
- проверки entitlement
- маршрутизации по `routeKey`
- ограничения доступа к account API

### Route Registry DB
Отдельная БД маршрутизации:
- `routeKey`
- `accountId`
- `projectId`
- `worker binding`
- версия маршрута

### Account Worker
Mini-backend одного аккаунта площадки.  
Все worker-ы обязаны иметь одинаковый API-контракт.

## Общая идея потока

```mermaid
sequenceDiagram
    participant FE as Frontend
    participant CORE as Core
    participant GATE as Account API Gateway
    participant AM as Accounts Manager
    participant W as Account Worker
    participant BILL as Billing
    participant ENT as Entitlement Service

    FE->>CORE: получить пользователя, проекты, аккаунты, тариф, лимиты
    CORE-->>FE: user info + project list + account list + subscription summary

    FE->>GATE: запрос по routeKey к API аккаунта
    GATE->>ENT: проверить доступность проекта и entitlement
    ENT-->>GATE: allowed / blocked / partially_allowed
    GATE->>W: проксирование запроса
    W-->>GATE: ответ worker-а
    GATE-->>FE: ответ account API

    FE->>CORE: создать / изменить / удалить аккаунт
    CORE->>ENT: проверить лимиты и доступные платформы
    ENT-->>CORE: allowed / denied
    CORE->>AM: lifecycle-команда
    AM->>W: создать / изменить / удалить / перенести worker
    AM-->>CORE: lifecycle result

    FE->>CORE: купить тариф / add-on
    CORE->>BILL: создать платёж
    BILL->>ENT: пересчитать итоговые доступы после изменения статуса подписки
```

## Ключевые ограничения и коммерческие правила
- тариф привязан к проекту
- у каждого проекта отдельный набор тарифов и add-on
- `trial/grace` задаются политикой конкретного тарифа
- тариф + отдельные add-on
- площадки могут входить в тариф и могут продаваться отдельно
- лимиты жёсткие (**hard limits**)
- при снижении тарифа или неоплате неразрешённые площадки блокируются, но не удаляются сразу
- Gateway перестаёт пускать в worker только для запрещённых entitlement-операций
- есть ручные платёжные сценарии
- есть ручной override со стороны системного админа DDCRM (с аудитом и сроком действия)
- для каждой площадки прокси обязателен

## Что специально не детализируется в этом черновике
- конкретные API-методы
- DTO
- таблицы БД
- детали оркестрации контейнеров
- детали токенов и сетевой реализации
- детали брокеров событий
