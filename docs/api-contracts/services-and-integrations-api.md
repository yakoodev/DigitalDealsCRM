# DDCRM — API сервисов и интеграций

Актуально на 18.05.2026.

## 1. Контуры API

- `external` (публичный Core API): UI/интеграторы.
- `internal` (service-to-service): доверенные сервисы.
- `worker` (runtime интеграций): исполнение действий внешних платформ.

Канонические OpenAPI-файлы:

- `docs/api-contracts/openapi-external.yaml`
- `docs/api-contracts/openapi-internal.yaml`
- `docs/api-contracts/openapi-worker.yaml`

## 2. Управление интеграциями в проекте (Core external)

Base path: `/v1/projects/{projectId}/integrations/...`

### 2.1 Статус и инстансы

- `GET /v1/projects/{projectId}/integrations/status`
- `GET /v1/projects/{projectId}/integrations/{integrationKey}/instances`
- `POST /v1/projects/{projectId}/integrations/{integrationKey}/instances`
- `DELETE /v1/projects/{projectId}/integrations/{integrationKey}/instances/{instanceId}`

### 2.2 Runtime lifecycle инстанса

- `POST /v1/projects/{projectId}/integrations/{integrationKey}/instances/{instanceId}/runtime/provision`
- `POST /v1/projects/{projectId}/integrations/{integrationKey}/instances/{instanceId}/runtime/deprovision`
- `POST /v1/projects/{projectId}/integrations/{integrationKey}/instances/{instanceId}/runtime/restart`

### 2.3 Вызов действий инстанса

- `POST /v1/projects/{projectId}/integrations/{integrationKey}/instances/{instanceId}/actions/read`
- `POST /v1/projects/{projectId}/integrations/{integrationKey}/instances/{instanceId}/actions/jobs`

Формат тела:

```json
{
  "operation": "string",
  "...": "операционные поля"
}
```

### 2.4 UI-сессия интеграции

- `POST /v1/projects/{projectId}/integrations/{integrationKey}/instances/{instanceId}/ui/session`

Возвращает `token`, `expiresAtUtc`, `iframeUrl` для embedded UI интеграции.

## 3. Workflow webhook для покупок (Core external)

- `POST /v1/integrations/workflow/purchase`

Ключевые поля для аренд/продлений:

- `payload.leaseId` — корреляция продления.
- `payload.marketplaceAccountId`
- `payload.conversationId`
- `payload.platform`
- `sourceOrderId` — уникальный ID оплаты (для идемпотентности на стороне интеграции).

## 4. Steam integration через workflow node `SteamAction`

Node `SteamAction` в рантайме Core вызывает `ext.integration.steam.read` или `ext.integration.steam.jobs` по `operation`.

Новые read-операции, которые считаются read-only в Core:

- `workflow.actions.catalog`
- `workflow.blocks.catalog`
- `rentals.availability.list`
- `rentals.account.select`
- `denuvo.availability.list`
- `denuvo.slot.stats`

При `operation` c префиксом:

- `rentals.*` — в runtime переменные промоутятся `rental.*`
- `denuvo.*` — в runtime переменные промоутятся `denuvo.*`

## 5. Security и идемпотентность

- Все integration action вызовы идут в рамках project grant + active runtime.
- Для mutating операций worker-контур требует `Idempotency-Key`.
- Для продлений и Denuvo-выдач используйте новый `sourceOrderId` на каждую оплату.

## 6. Рекомендуемые sequence-флоу

### 6.1 Аренда Steam

1. `MessageStart` + `SteamAction(rentals.availability.list)`
2. `PurchaseStart` + `SteamAction(rentals.reserve)`
3. lifecycle warning/expire закрывает `DDCRM-Steam`
4. `PurchaseStart` + `SteamAction(rentals.extend)` при доплате

### 6.2 Offline Denuvo

1. `PurchaseStart` + `SteamAction(denuvo.slot.acquire)`
2. При проверках доступности: `SteamAction(denuvo.availability.list|denuvo.slot.stats)`

Лимит: до 5 активаций на аккаунт за rolling-окно 24 часа.
