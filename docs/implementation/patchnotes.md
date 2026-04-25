# DDCRM — Patchnotes реализации

## Шаблон записи
- `Task ID`:
- `WP`:
- `Status` (`started` | `completed`):
- `Operations`:
- `Gates`:
- `Exception`:
- `Changed files`:
- `Date`:

## Wave 1 (Фаза 1, backend-only, contract-first)

### W1-T01
- `Task ID`: `W1-T01`
- `WP`: `WP-PLATFORM-CORE`, `WP-IAM`, `WP-ROUTE-REGISTRY`
- `Status`: `started`
- `Operations`: bootstrap solution, services, shared-layer
- `Gates`: n/a
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/*`, `tests/*`, `tools/*`, `eng/*`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T01`
- `WP`: `WP-PLATFORM-CORE`, `WP-IAM`, `WP-ROUTE-REGISTRY`
- `Status`: `completed`
- `Operations`: core/iam/route service bootstrap + shared middleware/auth/idempotency
- `Gates`: `OAG-TEST-CONTRACT-SERVICES` (подготовка)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/*`, `src/DDCRM.Iam.Api/*`, `src/DDCRM.RouteRegistry.Api/*`, `src/DDCRM.Shared/*`
- `Date`: `2026-04-24`

### W1-T02
- `Task ID`: `W1-T02`
- `WP`: `WP-PLATFORM-CORE`, `WP-ROUTE-REGISTRY`
- `Status`: `started`
- `Operations`: persistence model + db contexts
- `Gates`: n/a
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Persistence/*`, `src/DDCRM.RouteRegistry.Persistence/*`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T02`
- `WP`: `WP-PLATFORM-CORE`, `WP-ROUTE-REGISTRY`
- `Status`: `completed`
- `Operations`: separate db contexts (`ddcrm_core`, `ddcrm_route_registry`) + idempotency tables
- `Gates`: `OAG-TEST-CONTRACT-SERVICES` (подготовка)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Persistence/*`, `src/DDCRM.RouteRegistry.Persistence/*`
- `Date`: `2026-04-24`

### W1-T03
- `Task ID`: `W1-T03`
- `WP`: `WP-PLATFORM-CORE`, `WP-IAM`, `WP-ROUTE-REGISTRY`
- `Status`: `started`
- `Operations`: cross-cutting middleware/auth/cors/jwt/service-auth
- `Gates`: `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`, `OAG-TEST-CORS-EXTERNAL`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Shared/*`, `src/DDCRM.Core.Api/Program.cs`, `src/DDCRM.Iam.Api/Program.cs`, `src/DDCRM.RouteRegistry.Api/Program.cs`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T03`
- `WP`: `WP-PLATFORM-CORE`, `WP-IAM`, `WP-ROUTE-REGISTRY`
- `Status`: `completed`
- `Operations`: requestId, error envelope, idempotency store, internal auth, token isolation, external cors/jwt
- `Gates`: `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`, `OAG-TEST-CORS-EXTERNAL` (подготовка)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Shared/*`, `src/DDCRM.Core.Api/Program.cs`, `src/DDCRM.Iam.Api/Program.cs`, `src/DDCRM.RouteRegistry.Api/Program.cs`, `.env.external-api.example`, `docs/standards/runtime-configuration.md`
- `Date`: `2026-04-24`

### W1-T04
- `Task ID`: `W1-T04`
- `WP`: `WP-PLATFORM-CORE`
- `Status`: `started`
- `Operations`: external operations projects/members/accounts(list)
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/Program.cs`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T04`
- `WP`: `WP-PLATFORM-CORE`
- `Status`: `completed`
- `Operations`: list/create/get/update projects, add/change/remove member, list accounts, out-of-scope endpoints => `501 FEATURE_NOT_READY`
- `Gates`: `OAG-TEST-CONTRACT-SERVICES` (подготовка)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/Program.cs`
- `Date`: `2026-04-24`

### W1-T05
- `Task ID`: `W1-T05`
- `WP`: `WP-IAM`
- `Status`: `started`
- `Operations`: IAM endpoints + permission matrix integration + cache invalidate audit
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Iam.Api/Program.cs`, `src/DDCRM.Shared/Authorization/*`, `src/DDCRM.Core.Persistence/Entities/MembershipCacheInvalidationAuditEntity.cs`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T05`
- `WP`: `WP-IAM`
- `Status`: `completed`
- `Operations`: `iamCheckPermission`, `iamProjectMembership`, `iamMembershipCacheInvalidate`
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH` (подготовка)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Iam.Api/Program.cs`, `src/DDCRM.Shared/Authorization/*`, `src/DDCRM.Core.Persistence/*`
- `Date`: `2026-04-24`

### W1-T06
- `Task ID`: `W1-T06`
- `WP`: `WP-ROUTE-REGISTRY`
- `Status`: `started`
- `Operations`: route upsert/delete/switch/resolve-bulk
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.RouteRegistry.Api/Program.cs`, `src/DDCRM.RouteRegistry.Persistence/*`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T06`
- `WP`: `WP-ROUTE-REGISTRY`
- `Status`: `completed`
- `Operations`: route API + version check + `409` conflict + idempotency
- `Gates`: `OAG-TEST-CONTRACT-SERVICES` (подготовка)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.RouteRegistry.Api/Program.cs`, `src/DDCRM.RouteRegistry.Persistence/*`
- `Date`: `2026-04-24`

### W1-T07
- `Task ID`: `W1-T07`
- `WP`: `cross-cutting`
- `Status`: `started`
- `Operations`: CI contract entrypoints
- `Gates`: `OAG-VAL-OPENAPI31`, `OAG-LINT-STYLE`, `OAG-BREAKING-EXTERNAL`, `OAG-BREAKING-WORKER`, `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`, `OAG-TEST-CORS-EXTERNAL`
- `Exception`: n/a
- `Changed files`: `eng/contracts.ps1`, `eng/contracts.sh`, `tools/DDCRM.Contracts.Cli/*`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T07`
- `WP`: `cross-cutting`
- `Status`: `completed`
- `Operations`: entrypoints `contracts:validate/lint/diff:external/diff:worker/test:services/test:security/test:cors`
- `Gates`: подготовлены для CI
- `Exception`: n/a
- `Changed files`: `eng/contracts.ps1`, `eng/contracts.sh`, `eng/README.md`, `tools/DDCRM.Contracts.Cli/Program.cs`
- `Date`: `2026-04-24`

### W1-T08
- `Task ID`: `W1-T08`
- `WP`: `WP-WORKER-CONTRACT (deferred)`
- `Status`: `started`
- `Operations`: оформить временный exception на worker-specific gates
- `Gates`: `OAG-TEST-CONTRACT-WORKER`, `OAG-TEST-CAPABILITY-ACTION`, `OAG-TEST-WORKER-SERVICE-AUTH`
- `Exception`: pending
- `Changed files`: `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T08`
- `WP`: `WP-WORKER-CONTRACT (deferred)`
- `Status`: `completed`
- `Operations`: оформлен временный exception
- `Gates`: `OAG-TEST-CONTRACT-WORKER`, `OAG-TEST-CAPABILITY-ACTION`, `OAG-TEST-WORKER-SERVICE-AUTH`
- `Exception`: `owner=platform-team`, `reason=wave-1 scope excludes worker runtime`, `expiry=2026-06-30`, `mitigation=закрыть в WP-WORKER-CONTRACT`
- `Changed files`: `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W1-T09
- `Task ID`: `W1-T09`
- `WP`: `process`
- `Status`: `started`
- `Operations`: task decomposition and patchnote workflow
- `Gates`: n/a
- `Exception`: n/a
- `Changed files`: `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

## Wave 2 (Фаза 1, Accounts Manager)

### W2-T01
- `Task ID`: `W2-T01`
- `WP`: `WP-ACCOUNTS-MANAGER`
- `Status`: `started`
- `Operations`: bootstrap Accounts Manager API/Persistence/Test projects + lifecycle orchestration implementation
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.AccountsManager.Api/*`, `src/DDCRM.AccountsManager.Persistence/*`, `tests/DDCRM.AccountsManager.Api.Tests/*`, `eng/contracts.ps1`, `eng/contracts.sh`
- `Date`: `2026-04-24`

- `Task ID`: `W2-T01`
- `WP`: `WP-ACCOUNTS-MANAGER`
- `Status`: `completed`
- `Operations`: lifecycle `create/update/delete/migrate` (`202 Ack`), persisted idempotency, route cleanup on delete, route-switch/upsert orchestration, DB migration, integration/security tests, CI entrypoint wiring
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` (подготовка)
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.AccountsManager.Api/*`, `src/DDCRM.AccountsManager.Persistence/*`, `tests/DDCRM.AccountsManager.Api.Tests/*`, `eng/contracts.ps1`, `eng/contracts.sh`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W2-T02
- `Task ID`: `W2-T02`
- `WP`: `WP-GATEWAY`
- `Status`: `started`
- `Operations`: bootstrap Gateway API/Test projects + proxy endpoint implementation (`resolve route`, `jwt auth`, `iam permission`, `entitlement gate`, `worker proxy`)
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-CORS-EXTERNAL`
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.Gateway.Api/*`, `tests/DDCRM.Gateway.Api.Tests/*`, `eng/contracts.ps1`, `eng/contracts.sh`
- `Date`: `2026-04-24`

- `Task ID`: `W2-T02`
- `WP`: `WP-GATEWAY`
- `Status`: `completed`
- `Operations`: `proxyAccountApiAction` runtime + action validation + capability-check for `ext.*` + CORS/JWT + integration/security/cors tests + CI entrypoint wiring
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-CORS-EXTERNAL` (подготовка)
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.Gateway.Api/*`, `tests/DDCRM.Gateway.Api.Tests/*`, `eng/contracts.ps1`, `eng/contracts.sh`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W2-T03
- `Task ID`: `W2-T03`
- `WP`: `WP-WORKER-CONTRACT`
- `Status`: `started`
- `Operations`: bootstrap Worker API/Persistence/Test projects + runtime implementation по `openapi-worker.yaml` (resource endpoints, idempotency-store, service-auth, scenario/capability policy)
- `Gates`: `OAG-TEST-CONTRACT-WORKER`, `OAG-TEST-CAPABILITY-ACTION`, `OAG-TEST-WORKER-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.Worker.Api/*`, `src/DDCRM.Worker.Persistence/*`, `tests/DDCRM.Worker.Api.Tests/*`, `src/DDCRM.Shared/Auth/*`, `eng/contracts.ps1`, `eng/contracts.sh`, `eng/README.md`
- `Date`: `2026-04-24`

- `Task ID`: `W2-T03`
- `WP`: `WP-WORKER-CONTRACT`
- `Status`: `completed`
- `Operations`: реализованы `workerHealth`, `workerCapabilities`, `workerAccountInfo`, `workerListingsSearch`, `workerListingUpdate`, `workerMessageSend`, `workerOrdersSearch`, `workerOrderAction`, `workerExtensionAction`; добавлены `TW-SCN-*`/`TW-CAP-*` runtime-профили, политика `ext.test.*` для non-production, EF migration, integration/security tests, entrypoint `contracts:test:worker`
- `Gates`: `OAG-TEST-CONTRACT-WORKER`, `OAG-TEST-CAPABILITY-ACTION`, `OAG-TEST-WORKER-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.Worker.Api/*`, `src/DDCRM.Worker.Persistence/*`, `tests/DDCRM.Worker.Api.Tests/*`, `src/DDCRM.Shared/Auth/*`, `eng/contracts.ps1`, `eng/contracts.sh`, `eng/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W2-T04
- `Task ID`: `W2-T04`
- `WP`: `WP-WORKER-CONTRACT`
- `Status`: `started`
- `Operations`: закрыть временный worker-gates exception из `W1-T08` после реализации `WP-WORKER-CONTRACT`
- `Gates`: `OAG-TEST-CONTRACT-WORKER`, `OAG-TEST-CAPABILITY-ACTION`, `OAG-TEST-WORKER-SERVICE-AUTH`
- `Exception`: `W1-T08` pending resolution
- `Changed files`: `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W2-T04`
- `WP`: `WP-WORKER-CONTRACT`
- `Status`: `completed`
- `Operations`: временный exception из `W1-T08` закрыт после успешной реализации worker runtime и прохождения worker/security/capability gate-проверок
- `Gates`: `OAG-TEST-CONTRACT-WORKER`, `OAG-TEST-CAPABILITY-ACTION`, `OAG-TEST-WORKER-SERVICE-AUTH` (зелёные локальные прогоны)
- `Exception`: `W1-T08 resolved`, `owner=platform-team`, `reason=worker runtime delivered in W2-T03`, `resolvedDate=2026-04-24`
- `Changed files`: `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

## Wave 3 (Фаза 2, Billing baseline)

### W3-T01
- `Task ID`: `W3-T01`
- `WP`: `WP-BILLING`
- `Status`: `started`
- `Operations`: bootstrap Billing API/Persistence/Test projects + internal billing operations implementation (`payments create/webhook/refund`, `subscriptions manual-activate/reconcile`), webhook dedup, idempotency
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.Billing.Api/*`, `src/DDCRM.Billing.Persistence/*`, `tests/DDCRM.Billing.Api.Tests/*`, `eng/contracts.ps1`, `eng/contracts.sh`
- `Date`: `2026-04-24`

- `Task ID`: `W3-T01`
- `WP`: `WP-BILLING`
- `Status`: `completed`
- `Operations`: реализованы `internalPaymentsCreate`, `internalPaymentsWebhook`, `internalPaymentsRefund`, `internalSubscriptionsManualActivate`, `internalSubscriptionsReconcile`; добавлены persistence-модели billing + EF migration; webhook dedup и idempotency mutating-операций; integration/security tests; billing добавлен в `contracts:test:services`
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.Billing.Api/*`, `src/DDCRM.Billing.Persistence/*`, `tests/DDCRM.Billing.Api.Tests/*`, `eng/contracts.ps1`, `eng/contracts.sh`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W3-T02
- `Task ID`: `W3-T02`
- `WP`: `WP-PLATFORM-CORE`, `WP-BILLING`
- `Status`: `started`
- `Operations`: подключить external billing операции `createPayment`, `purchaseAddon`, `changePlan` в Core API через internal Billing API client + идемпотентность + RBAC
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/Program.cs`, `src/DDCRM.Core.Api/Billing/*`, `tests/DDCRM.Core.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W3-T02`
- `WP`: `WP-PLATFORM-CORE`, `WP-BILLING`
- `Status`: `completed`
- `Operations`: реализованы external billing handlers в `Core API` с вызовом `BillingHttpClient`; добавлены `GenericObjectResponse` ответы для `createPayment`/`purchaseAddon`, `AckResponse` для `changePlan`; покрытие интеграционными тестами по success/idempotency/RBAC
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/Program.cs`, `src/DDCRM.Core.Api/Billing/*`, `tests/DDCRM.Core.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W3-T03
- `Task ID`: `W3-T03`
- `WP`: `WP-ENTITLEMENT`
- `Status`: `started`
- `Operations`: bootstrap Entitlement API/Persistence/Test projects + internal entitlement operations implementation (`check`, `recalculate`, `override`, `get`), persisted idempotency, service-auth/token-isolation
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.Entitlement.Api/*`, `src/DDCRM.Entitlement.Persistence/*`, `tests/DDCRM.Entitlement.Api.Tests/*`, `eng/contracts.ps1`, `eng/contracts.sh`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W3-T03`
- `WP`: `WP-ENTITLEMENT`
- `Status`: `completed`
- `Operations`: реализованы `entitlementCheck`, `entitlementRecalculate`, `entitlementOverride`, `entitlementGet`; добавлены entitlement persistence-модели + EF migration; idempotency для mutating операций; integration/security tests; entitlement добавлен в `contracts:test:services`
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `DigitalDealsCRM.slnx`, `src/DDCRM.Entitlement.Api/*`, `src/DDCRM.Entitlement.Persistence/*`, `tests/DDCRM.Entitlement.Api.Tests/*`, `eng/contracts.ps1`, `eng/contracts.sh`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W3-T04
- `Task ID`: `W3-T04`
- `WP`: `WP-BILLING`, `WP-ENTITLEMENT`
- `Status`: `started`
- `Operations`: оркестрация billing -> entitlement recalculate на событиях подписки (`webhook`, `manual-activate`, `reconcile`) через internal Entitlement client
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Billing.Api/*`, `tests/DDCRM.Billing.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W3-T04`
- `WP`: `WP-BILLING`, `WP-ENTITLEMENT`
- `Status`: `completed`
- `Operations`: добавлен `EntitlementHttpClient` в Billing API; на `payment.succeeded/payment.failed`, `subscriptions/manual-activate`, `subscriptions/reconcile` выполняется вызов `entitlementRecalculate` с service-auth и idempotency key; расширены integration-тесты orchestration/dedup
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Billing.Api/*`, `tests/DDCRM.Billing.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W3-T05
- `Task ID`: `W3-T05`
- `WP`: `WP-PLATFORM-CORE`, `WP-ACCOUNTS-MANAGER`
- `Status`: `started`
- `Operations`: реализовать external account lifecycle в Core API (`createAccount`, `updateAccount`, `deleteAccount`, `getAccountProxyCredentialsMasked`, `updateAccountProxyCredentials`) через internal Accounts Manager client, idempotency, RBAC и аудит update proxy credentials
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/*`, `src/DDCRM.Core.Persistence/*`, `tests/DDCRM.Core.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W3-T05`
- `WP`: `WP-PLATFORM-CORE`, `WP-ACCOUNTS-MANAGER`
- `Status`: `completed`
- `Operations`: добавлен `AccountsManagerHttpClient` в Core API; реализованы external handlers `createAccount/updateAccount/deleteAccount/getAccountProxyCredentialsMasked/updateAccountProxyCredentials`; добавлен persisted аудит `proxy_credentials_audits`; расширены integration/security тесты Core на account lifecycle и proxy credentials update
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/*`, `src/DDCRM.Core.Persistence/*`, `tests/DDCRM.Core.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W3-T06
- `Task ID`: `W3-T06`
- `WP`: `WP-PLATFORM-CORE`, `WP-GATEWAY`
- `Status`: `started`
- `Operations`: реализовать external `proxyAccountApiAction` в Core API через Gateway client (`IGatewayProxyClient`) с persisted idempotency и проксированием bearer authorization
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/*`, `tests/DDCRM.Core.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W3-T06`
- `WP`: `WP-PLATFORM-CORE`, `WP-GATEWAY`
- `Status`: `completed`
- `Operations`: добавлен `GatewayProxyHttpClient` и endpoint-handler `POST /v1/account-api/{routeKey}/{action}` в Core API; включены idempotency-ключи для mutating прокси-вызовов; добавлены integration-тесты на success/idempotency/payload-forwarding
- `Gates`: `OAG-TEST-CONTRACT-SERVICES` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/*`, `tests/DDCRM.Core.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W3-T07
- `Task ID`: `W3-T07`
- `WP`: `WP-GATEWAY`
- `Status`: `started`
- `Operations`: security-hardening `account-api` proxy: ввести action-based RBAC mapping (`ext.account.lifecycle.*`, `ext.account.proxy-credentials.reveal/update`) вместо единого `project.workers.operate`
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH`
- `Exception`: n/a
- `Changed files`: `docs/standards/access-control-matrix.md`, `docs/standards/worker-action-conventions.md`, `src/DDCRM.Gateway.Api/*`, `tests/DDCRM.Gateway.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W3-T07`
- `WP`: `WP-GATEWAY`
- `Status`: `completed`
- `Operations`: Gateway применяет `ResolvePermissionForAction` перед `iamCheckPermission`; чувствительные `ext.account.*` action-key требуют отдельные permission keys из access matrix; добавлены integration/security тесты на permission mapping и запрет вызова worker при denied-доступе
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-INTERNAL-SERVICE-AUTH` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `docs/standards/access-control-matrix.md`, `docs/standards/worker-action-conventions.md`, `src/DDCRM.Gateway.Api/*`, `tests/DDCRM.Gateway.Api.Tests/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W3-T08
- `Task ID`: `W3-T08`
- `WP`: `WP-PLATFORM-CORE`, `WP-ACCOUNTS-MANAGER`, `WP-WORKER-CONTRACT`, `WP-GATEWAY`
- `Status`: `started`
- `Operations`: реализовать `revealAccountProxyCredentials` end-to-end: синхронизация proxy credentials в worker state storage (`ext.account.proxy-credentials.apply`), reveal action (`ext.account.proxy-credentials.reveal`), Core reveal endpoint + аудит + idempotency side-effect dedup, запрет чувствительных `ext.account.*` через generic Core account-api endpoint
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-WORKER-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Worker.Api/*`, `src/DDCRM.Worker.Persistence/*`, `src/DDCRM.AccountsManager.Api/*`, `src/DDCRM.Core.Api/*`, `src/DDCRM.Gateway.Api/*`, `tests/DDCRM.Worker.Api.Tests/*`, `tests/DDCRM.AccountsManager.Api.Tests/*`, `tests/DDCRM.Core.Api.Tests/*`, `tests/DDCRM.Gateway.Api.Tests/*`, `docs/standards/access-control-matrix.md`, `docs/standards/worker-action-conventions.md`, `docs/standards/runtime-configuration.md`, `.env.internal-api.example`, `.env.worker-api.example`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W3-T08`
- `WP`: `WP-PLATFORM-CORE`, `WP-ACCOUNTS-MANAGER`, `WP-WORKER-CONTRACT`, `WP-GATEWAY`
- `Status`: `completed`
- `Operations`: в Worker добавлены persisted proxy credentials + extension-actions apply/reveal; password хранится в worker state storage в зашифрованном виде (AES-GCM, ключ `WORKER_PROXY_CREDENTIALS_ENCRYPTION_KEY`); Accounts Manager синхронизирует proxy-config в worker на lifecycle create/update; Core реализует `POST /projects/{projectId}/accounts/{accountId}/proxy-credentials/reveal` через Gateway с RBAC, каноническим envelope и аудитом; чувствительные `ext.account.*` блокируются на generic Core account-api endpoint; Gateway RBAC-mapping учитывает `ext.account.proxy-credentials.apply` как update-permission; worker-control client вынесен в env-конвенцию `WORKER_CONTROL_CLIENT_*`
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-WORKER-SERVICE-AUTH`, `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Worker.Api/*`, `src/DDCRM.Worker.Persistence/*`, `src/DDCRM.AccountsManager.Api/*`, `src/DDCRM.Core.Api/*`, `src/DDCRM.Gateway.Api/*`, `tests/DDCRM.Worker.Api.Tests/*`, `tests/DDCRM.AccountsManager.Api.Tests/*`, `tests/DDCRM.Core.Api.Tests/*`, `tests/DDCRM.Gateway.Api.Tests/*`, `docs/standards/access-control-matrix.md`, `docs/standards/worker-action-conventions.md`, `docs/standards/runtime-configuration.md`, `.env.internal-api.example`, `.env.worker-api.example`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W1-T09`
- `WP`: `process`
- `Status`: `completed`
- `Operations`: зафиксирован шаблон patchnote и обязательные статусы `started/completed`
- `Gates`: n/a
- `Exception`: n/a
- `Changed files`: `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

## Wave 4 (Фаза 3, Docker Runtime + RBAC UI baseline)

### W4-T01
- `Task ID`: `W4-T01`
- `WP`: `cross-cutting`, `WP-OPS-READY`
- `Status`: `started`
- `Operations`: docker runtime baseline для всех API сервисов + PostgreSQL + UI контейнер, единый `docker-compose.yml`, инициализация отдельных БД
- `Gates`: n/a
- `Exception`: n/a
- `Changed files`: `docker-compose.yml`, `docker/*`, `.dockerignore`
- `Date`: `2026-04-24`

- `Task ID`: `W4-T01`
- `WP`: `cross-cutting`, `WP-OPS-READY`
- `Status`: `completed`
- `Operations`: добавлены `docker/api.Dockerfile`, `docker/ui.Dockerfile`, compose-оркестрация сервисов (`core/iam/route/accounts-manager/gateway/billing/entitlement/worker/ui`) и postgres init script с отдельными БД; добавлен runtime README
- `Gates`: `docker compose config`, `docker compose build core-api ui` (локальные зелёные прогоны)
- `Exception`: n/a
- `Changed files`: `docker-compose.yml`, `docker/api.Dockerfile`, `docker/ui.Dockerfile`, `docker/postgres/init/01-create-ddcrm-databases.sql`, `docker/README.md`, `.dockerignore`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W4-T02
- `Task ID`: `W4-T02`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: bootstrap базового UI на `Next.js App Router + TypeScript + TanStack Query + Orval`; реализовать role-aware guards для проектов/аккаунтов/proxy credentials/billing/gateway action
- `Gates`: `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/*`
- `Date`: `2026-04-24`

- `Task ID`: `W4-T02`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: создан frontend-проект `src/ddcrm-rbac-ui`; Orval-генерация клиента из `openapi-external.yaml`; TanStack Query data-layer; UI-секции проектов/аккаунтов/proxy credentials/billing/gateway action; role-aware guard по `access-control-matrix` (скрытие финансовых и чувствительных операций для moderator)
- `Gates`: `npm run generate:api`, `npm run lint`, `npm run build` (локальные зелёные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W4-T03
- `Task ID`: `W4-T03`
- `WP`: `cross-cutting`, `WP-OPS-READY`
- `Status`: `started`
- `Operations`: унифицировать runtime-health для docker стека: `/health` во всех API, compose healthcheck с service-auth токенами, hardening runtime image системными зависимостями для Npgsql
- `Gates`: `docker compose up -d --build`, `docker compose ps`, smoke health probes
- `Exception`: n/a
- `Changed files`: `docker-compose.yml`, `docker/api.Dockerfile`, `docker/README.md`, `src/DDCRM.Core.Api/Program.cs`, `src/DDCRM.Iam.Api/Program.cs`, `src/DDCRM.RouteRegistry.Api/Program.cs`, `src/DDCRM.AccountsManager.Api/Program.cs`, `src/DDCRM.Billing.Api/Program.cs`, `src/DDCRM.Entitlement.Api/Program.cs`, `src/DDCRM.Gateway.Api/Program.cs`, `src/DDCRM.Worker.Api/Program.cs`
- `Date`: `2026-04-24`

- `Task ID`: `W4-T03`
- `WP`: `cross-cutting`, `WP-OPS-READY`
- `Status`: `completed`
- `Operations`: добавлены endpoint-ы `/health` для всех API-контуров; docker runtime-образ теперь содержит `libgssapi-krb5-2` и `curl`; `docker-compose` healthchecks используют `X-Service-Token` (internal/worker token isolation сохранен); подтверждён полный green-статус `healthy` по всем контейнерам и smoke HTTP `200` по UI/API health
- `Gates`: `docker compose up -d --build`, `docker compose ps` (all healthy), `contracts:validate`, `contracts:lint`, `contracts:diff:external`, `contracts:diff:worker`, `contracts:test:services`, `contracts:test:security`, `contracts:test:cors`, `contracts:test:worker` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `docker-compose.yml`, `docker/api.Dockerfile`, `docker/README.md`, `src/DDCRM.Core.Api/Program.cs`, `src/DDCRM.Iam.Api/Program.cs`, `src/DDCRM.RouteRegistry.Api/Program.cs`, `src/DDCRM.AccountsManager.Api/Program.cs`, `src/DDCRM.Billing.Api/Program.cs`, `src/DDCRM.Entitlement.Api/Program.cs`, `src/DDCRM.Gateway.Api/Program.cs`, `src/DDCRM.Worker.Api/Program.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

## Wave 5 (Фаза 6, WP-OPS-READY baseline)

### W5-T01
- `Task ID`: `W5-T01`
- `WP`: `WP-OPS-READY`
- `Status`: `started`
- `Operations`: автоматизировать dry-run операционной готовности (`docker health`, `smoke probes`, `contract gates`) и синхронизировать ops-документы под воспроизводимый rollout/rotation rehearsal
- `Gates`: `QG-OPS-MTTA-SEV1`, `QG-OPS-MITIGATION-START-SEV1`, `QG-INC-SEV2-INTERNAL-AUTH-FAIL-RATE`, `QG-INC-SEV2-WORKER-AUTH-FAIL-RATE`
- `Exception`: n/a
- `Changed files`: `eng/ops-ready.ps1`, `eng/ops-ready.sh`, `eng/README.md`, `docs/operations/runbook.md`, `docs/operations/rollout-rollback-plan.md`, `docs/operations/service-auth-rotation-playbook.md`
- `Date`: `2026-04-24`

- `Task ID`: `W5-T01`
- `WP`: `WP-OPS-READY`
- `Status`: `completed`
- `Operations`: добавлены entrypoint-скрипты `eng/ops-ready.ps1/.sh` с проверкой `docker compose` health-статусов, HTTP smoke (`/health` + service-auth headers) и полным прогоном contract gates; `runbook`, `rollout/rollback` и `service-auth rotation playbook` синхронизированы с обязательным dry-run/rehearsal процессом
- `Gates`: `./eng/ops-ready.ps1` (зелёный локальный прогон), `contracts:validate`, `contracts:lint`, `contracts:diff:external`, `contracts:diff:worker`, `contracts:test:services`, `contracts:test:security`, `contracts:test:cors`, `contracts:test:worker` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `eng/ops-ready.ps1`, `eng/ops-ready.sh`, `eng/README.md`, `docs/operations/runbook.md`, `docs/operations/rollout-rollback-plan.md`, `docs/operations/service-auth-rotation-playbook.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

## Wave 6 (Фаза 3, WP-RBAC-UI hardening)

### W6-T01
- `Task ID`: `W6-T01`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: добавить автоматизированные регрессионные тесты матрицы ролей/прав (owner/admin/moderator) в UI и встроить запуск в npm-scripts
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/package.json`, `src/ddcrm-rbac-ui/package-lock.json`, `src/ddcrm-rbac-ui/src/lib/rbac.test.ts`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T01`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: подключён `vitest`; добавлены тесты `rbac.test.ts` на полный набор permission-check для owner/admin и ограниченный набор для moderator; добавлены npm entrypoint-команды `test`/`test:run`
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/package.json`, `src/ddcrm-rbac-ui/package-lock.json`, `src/ddcrm-rbac-ui/src/lib/rbac.test.ts`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T02
- `Task ID`: `W6-T02`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: доработать UI до platform-shell формата с базовой авторизацией, личным кабинетом и unified-навигацией
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/app/page.tsx`, `src/ddcrm-rbac-ui/src/app/layout.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/src/components/auth-screen.tsx`, `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/lib/auth.ts`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T02`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: реализованы экраны входа (`Demo`/`Manual JWT`), persisted session, platform sidebar/navigation, личный кабинет (профиль, JWT-meta, переключение UI-роли, permission-list), журнал активности и переработанный адаптивный дизайн
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/app/page.tsx`, `src/ddcrm-rbac-ui/src/app/layout.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/src/components/auth-screen.tsx`, `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/lib/auth.ts`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T03
- `Task ID`: `W6-T03`
- `WP`: `WP-GATEWAY`, `WP-WORKER-CONTRACT`, `WP-PLATFORM-CORE`
- `Status`: `started`
- `Operations`: исправить e2e reveal proxy credentials через gateway proxy: привести формат `ext.*` запроса к worker `ExtensionActionRequest` (`payload` envelope)
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-WORKER-SERVICE-AUTH`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Gateway.Api/Clients/WorkerProxyHttpClient.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T03`
- `WP`: `WP-GATEWAY`, `WP-WORKER-CONTRACT`, `WP-PLATFORM-CORE`
- `Status`: `completed`
- `Operations`: `Gateway` теперь оборачивает `ext.*` payload в `{"payload": ...}`; устранён runtime-сбой `Для reveal требуется payload`; подтверждён e2e сценарий: `createAccount` (Core -> Accounts Manager -> Worker apply) + `revealAccountProxyCredentials` (Core -> Gateway -> Worker reveal)
- `Gates`: `dotnet test tests/DDCRM.Gateway.Api.Tests`, `dotnet test tests/DDCRM.Core.Api.Tests` (зелёные локальные прогоны), e2e smoke `createAccount + reveal` (успешно)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Gateway.Api/Clients/WorkerProxyHttpClient.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T04
- `Task ID`: `W6-T04`
- `WP`: `WP-PLATFORM-CORE`
- `Status`: `started`
- `Operations`: устранить runtime-ошибку `listProjects` (EF translation failure при `OrderBy` после проекции в `ProjectDto`)
- `Gates`: `OAG-TEST-CONTRACT-SERVICES`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/Program.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T07
- `Task ID`: `W6-T07`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: улучшить UX модульных вкладок проекта (`Товары`, `Сообщения`, `Заказы`): добавить табличный вывод результатов и персистентность параметров запросов по `projectId`
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T09
- `Task ID`: `W6-T09`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: расширить вкладку `Участники` пакетными (bulk) операциями `add/change role/remove` по списку GUID и добавить структурированный отчёт последней пакетной операции
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T09`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: добавлен bulk-парсинг userId с поддержкой разделителей (newline/space/comma/semicolon), обработка невалидных GUID/дубликатов, последовательное выполнение mutating member-операций с частичным успехом и сбором ошибок; в UI добавлены формы `Bulk add`, `Bulk change role`, `Bulk remove`, общий lock на параллельные member-операции и отчёт последней пакетной операции
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T07`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: в `Товары/Сообщения/Заказы` добавлен табличный рендер ответов (dynamic columns + форматирование значений) с сохранением raw JSON; payload-параметры сохраняются в `localStorage` отдельно для каждого проекта и автоматически восстанавливаются при открытии проекта
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны), `docker compose up -d --build ui` (контейнер обновлён)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T06
- `Task ID`: `W6-T06`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: добавить управление участниками проекта в UI (`addMember`, `changeMemberRole`, `removeMember`) и быстрый onboarding demo-пользователей в проект
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/lib/api-client.ts`, `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T06`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: добавлена вкладка `Участники` в проекте; реализованы формы и action-кнопки для invite/change-role/remove с RBAC-guard; добавлен быстрый выбор demo userId; добавлен локальный журнал операций участников; API-layer расширен wrapper-ами member-операций
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/lib/api-client.ts`, `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T05
- `Task ID`: `W6-T05`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: перестроить UX в проектно-центричный сценарий: «список проектов + создать проект» -> «открыть проект» -> «вкладки проекта (аккаунты/товары/сообщения/заказы/proxy/billing/gateway)»
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T05`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: реализован явный список проектов с empty-state и кнопкой «Создать проект»; добавлено открытие конкретного проекта; внутри проекта добавлены отдельные вкладки `Аккаунты/Товары/Сообщения/Заказы/Proxy/Billing/Gateway`; операции модулей привязаны к выбранному аккаунту и выполняются через существующие API/gateway action-ы
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны), `docker compose up -d --build ui` (контейнер обновлён)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T04`
- `WP`: `WP-PLATFORM-CORE`
- `Status`: `completed`
- `Operations`: переписана выборка `listProjects`: сортировка выполняется по анонимной проекции до маппинга в `ProjectDto`; подтверждён рабочий runtime-ответ `GET /v1/projects` (`200`)
- `Gates`: `dotnet test tests/DDCRM.Core.Api.Tests` (зелёный локальный прогон), docker smoke `GET /v1/projects` (`200`)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Core.Api/Program.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T08
- `Task ID`: `W6-T08`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: улучшить discoverability проектной зоны: добавить фильтрацию `Проекты/Аккаунты` и автоматическое восстановление последнего открытого проекта между сессиями
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T08`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: добавлен поиск и фильтрация списков проектов/аккаунтов с empty-state подсказками и быстрым reset фильтра; открытый проект теперь автоподхватывается из localStorage после обновления страницы (без ручного повторного открытия); обновлены пользовательские заметки в README
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T10
- `Task ID`: `W6-T10`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: добавить вкладку `Обзор` внутри проекта с проектной сводкой (status/accounts/role/module readiness) и быстрыми переходами по ключевым разделам workspace
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

- `Task ID`: `W6-T10`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: добавлена вкладка `Обзор` с метриками проекта и быстрыми action-кнопками для перехода в `Аккаунты/Участники/Товары/Сообщения/Заказы/Proxy/Billing/Gateway` с учётом RBAC-disable; при открытии проекта workspace теперь стабильно стартует со вкладки `Аккаунты`, как в базовом пользовательском сценарии project-first
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W6-T11
- `Task ID`: `W6-T11`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: усилить вкладку `Обзор` до операционного формата: добавить «пульс» интеграций (Core/Accounts/Proxy/Gateway) и проектный журнал последних действий с персистентностью по `projectId`
- `Gates`: `frontend:test`, `frontend:lint`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W7-T01
- `Task ID`: `W7-T01`
- `WP`: `WP-WORKER-CONTRACT`
- `Status`: `started`
- `Operations`: начать additive-расширение worker-контракта до `v2`: добавить ресурсные endpoint-ы `account/capabilities/conversations/products/schemas` без удаления `v1`, подготовить runtime-реализацию и тесты
- `Gates`: `contracts:validate`, `contracts:lint`, `contracts:test:worker`
- `Exception`: n/a
- `Changed files`: `docs/standards/openapi-governance.md`, `docs/testing/worker-contract-checklist.md`, `docs/api-contracts/openapi-worker.yaml`, `src/DDCRM.Worker.Api/Program.cs`, `tests/DDCRM.Worker.Api.Tests/WorkerApiIntegrationTests.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

### W7-T02
- `Task ID`: `W7-T02`
- `WP`: `WP-GATEWAY`, `WP-WORKER-CONTRACT`
- `Status`: `started`
- `Operations`: синхронизировать action-routing в Gateway с новым `v2` worker namespace (`account/conversations/products`) и сохранить legacy `v1` actions в dual-support режиме
- `Gates`: `dotnet test tests/DDCRM.Gateway.Api.Tests`, `contracts:diff:worker`
- `Exception`: n/a
- `Changed files`: `docs/standards/worker-action-conventions.md`, `src/DDCRM.Gateway.Api/Clients/WorkerProxyHttpClient.cs`, `tests/DDCRM.Gateway.Api.Tests/WorkerProxyHttpClientTests.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W7-T02`
- `WP`: `WP-GATEWAY`, `WP-WORKER-CONTRACT`
- `Status`: `completed`
- `Operations`: обновлен `WorkerProxyHttpClient`: добавлен mapping action-ключей `account.info`, `conversations.*`, `products.*` в `/internal/v2/worker/*`; добавлена сборка query-параметров для list/read action-ов; mutating payload для `products.update/products.delete` и `conversations.messages.send` санитизируется от path-id полей перед проксированием; добавлен fallback dual-support для legacy `messages/listings/orders`; в канонике action-conventions зафиксированы `v2` namespace и dual-support политика
- `Gates`: `dotnet test tests/DDCRM.Gateway.Api.Tests` (зелёный локальный прогон), `dotnet test tests/DDCRM.Worker.Api.Tests` (зелёный локальный прогон), `./eng/contracts.ps1 contracts:validate`, `./eng/contracts.ps1 contracts:lint`, `./eng/contracts.ps1 contracts:diff:worker`, `./eng/contracts.ps1 contracts:test:worker`, `./eng/contracts.ps1 contracts:test:services` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `docs/standards/worker-action-conventions.md`, `src/DDCRM.Gateway.Api/Clients/WorkerProxyHttpClient.cs`, `tests/DDCRM.Gateway.Api.Tests/WorkerProxyHttpClientTests.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W7-T01`
- `WP`: `WP-WORKER-CONTRACT`
- `Status`: `completed`
- `Operations`: добавлен `internal/v2/worker` namespace поверх текущего `v1` (dual-support): реализованы `workerV2AccountInfo`, `workerV2Capabilities`, `workerV2ConversationsList`, `workerV2ConversationMessages`, `workerV2ConversationMessageSend`, `workerV2ProductsList`, `workerV2ProductCreate`, `workerV2ProductUpdate`, `workerV2ProductDelete`, `workerV2ProductSchemas`; добавлен runtime-store для переписок и расширяемых product-полей (`schemaId/media/attributes`) с version-check и `409` при stale-version; расширены интеграционные тесты worker API для `v2` endpoint-ов и idempotency/version-conflict сценариев
- `Gates`: `dotnet test tests/DDCRM.Worker.Api.Tests` (зелёный локальный прогон), `./eng/contracts.ps1 contracts:validate`, `./eng/contracts.ps1 contracts:lint`, `./eng/contracts.ps1 contracts:test:worker` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `docs/standards/openapi-governance.md`, `docs/testing/worker-contract-checklist.md`, `docs/api-contracts/openapi-worker.yaml`, `src/DDCRM.Worker.Api/Program.cs`, `tests/DDCRM.Worker.Api.Tests/WorkerApiIntegrationTests.cs`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W6-T11`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: добавлен проектный activity-store (`localStorage`) и автологирование ключевых project-операций (members/accounts/proxy/modules/billing/gateway/open project); в `Обзор` добавлен «Операционный пульс» по состояниям `Core external`, `Accounts sync`, `Proxy channel`, `Gateway channel`; добавлен блок «Последние действия проекта» с очисткой истории
- `Gates`: `npm run test:run`, `npm run lint`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/README.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`

### W7-T03
- `Task ID`: `W7-T03`
- `WP`: `WP-WORKER-CONTRACT`, `WP-GATEWAY`
- `Status`: `started`
- `Operations`: завершить вывод legacy `v1` worker namespace из runtime/infra, зафиксировать только `v2` path-prefix в сервисных клиентах и docker-конфигурации, оформить machine-readable exception для `contracts:diff:worker`
- `Gates`: `dotnet test tests/DDCRM.Worker.Api.Tests`, `dotnet test tests/DDCRM.Gateway.Api.Tests`, `contracts:validate`, `contracts:lint`, `contracts:diff:worker`, `contracts:test:worker`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Gateway.Api/Clients/WorkerProxyHttpClient.cs`, `src/DDCRM.Worker.Api/Program.cs`, `docker-compose.yml`, `docs/api-contracts/openapi-worker.yaml`, `tools/DDCRM.Contracts.Cli/Program.cs`, `docs/implementation/contract-gate-exceptions.json`, `docs/testing/contract-gates-execution.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W7-T03`
- `WP`: `WP-WORKER-CONTRACT`, `WP-GATEWAY`
- `Status`: `completed`
- `Operations`: удалён runtime dual-support префиксов `v1/v2` в `Gateway` (`WorkerProxyHttpClient` использует только `PathPrefix`); `docker-compose` и service-clients переведены на `/internal/v2/worker`; worker OpenAPI обновлён до `version: 2.0.0`; удалены неиспользуемые legacy-хелперы в worker runtime после отказа от `v1`; в `DDCRM.Contracts.Cli` добавлена поддержка exception-правил для `contracts:diff:*`, зарегистрирован активный exception `CGE-WORKER-V1-REMOVAL-2026-04` (owner/reason/expiry) для контролируемого breaking при выводе `v1`
- `Gates`: `dotnet test tests/DDCRM.Worker.Api.Tests`, `dotnet test tests/DDCRM.Gateway.Api.Tests`, `dotnet test tests/DDCRM.Core.Api.Tests`, `./eng/contracts.ps1 contracts:validate`, `./eng/contracts.ps1 contracts:lint`, `./eng/contracts.ps1 contracts:diff:worker`, `./eng/contracts.ps1 contracts:test:worker` (зелёные локальные прогоны)
- `Exception`: `CGE-WORKER-V1-REMOVAL-2026-04` (`owner=platform-team`, `expiry=2026-07-31`)
- `Changed files`: `src/DDCRM.Gateway.Api/Clients/WorkerProxyHttpClient.cs`, `src/DDCRM.Worker.Api/Program.cs`, `docker-compose.yml`, `docs/api-contracts/openapi-worker.yaml`, `tools/DDCRM.Contracts.Cli/Program.cs`, `docs/implementation/contract-gate-exceptions.json`, `docs/testing/contract-gates-execution.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

### W7-T04
- `Task ID`: `W7-T04`
- `WP`: `WP-RBAC-UI`, `WP-WORKER-CONTRACT`
- `Status`: `started`
- `Operations`: синхронизировать пользовательский UI/документацию с `v2` worker доменами (`conversations/products/schemas`) и убрать legacy-терминологию `listings/messages/orders`
- `Gates`: `frontend:lint`, `frontend:test`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/README.md`, `docs/standards/worker-action-conventions.md`, `docs/api-contracts/api-contracts.md`, `docs/spec/technical-specification.md`, `docs/architecture_draft/07-account-worker.md`, `docs/testing/worker-contract-checklist.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W7-T04`
- `WP`: `WP-RBAC-UI`, `WP-WORKER-CONTRACT`
- `Status`: `completed`
- `Operations`: UI-модули проекта работают в `v2` action namespace (`conversations.*`, `products.*`, `products.schemas.*`); пользовательские материалы обновлены под вкладку `Схемы`; канонические и зависимые документы синхронизированы на `account/conversations/products` как единый worker baseline без `v1`-терминологии
- `Gates`: `npm run lint`, `npm run test:run`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/README.md`, `docs/standards/worker-action-conventions.md`, `docs/api-contracts/api-contracts.md`, `docs/spec/technical-specification.md`, `docs/architecture_draft/07-account-worker.md`, `docs/testing/worker-contract-checklist.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

### W7-T05
- `Task ID`: `W7-T05`
- `WP`: `WP-WORKER-CONTRACT`
- `Status`: `started`
- `Operations`: добавить provider-профили тестового воркера (`funpay/playerok/ggsell/platimarket`) с env-переключением `TEST_WORKER_PROVIDER`, provider-специфичными account/capabilities/schemas ответами и контрактной поддержкой query-параметра `provider` для `schemas/products`
- `Gates`: `dotnet test tests/DDCRM.Worker.Api.Tests`, `contracts:validate`, `contracts:lint`, `contracts:diff:worker`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Worker.Api/Simulation/TestWorkerOptions.cs`, `src/DDCRM.Worker.Api/Program.cs`, `tests/DDCRM.Worker.Api.Tests/Infrastructure/WorkerApiFactory.cs`, `tests/DDCRM.Worker.Api.Tests/WorkerApiIntegrationTests.cs`, `docs/api-contracts/openapi-worker.yaml`, `.env.test-worker.example`, `docker-compose.yml`, `docs/standards/test-worker-governance.md`, `docs/testing/test-worker-checklist.md`, `docs/testing/worker-contract-checklist.md`, `docs/standards/runtime-configuration.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W7-T05`
- `WP`: `WP-WORKER-CONTRACT`
- `Status`: `completed`
- `Operations`: runtime тестового воркера поддерживает выбор площадки через `TEST_WORKER_PROVIDER`; `provider` теперь консистентен в `/internal/v2/worker/account`, `/internal/v2/worker/capabilities` и `/internal/v2/worker/schemas/products`; добавлены provider-специфичные product schema (`funpay.item.v1`, `playerok.item.v1`, `ggsell.item.v1`, `platimarket.item.v1`) вместе с базовой `digital_goods.v1`; `products.create` валидирует неизвестный `schemaId` и обязательные provider-специфичные поля по schema (`400` при несоответствии); добавлена валидация query-параметра `provider`; расширены интеграционные тесты под provider-профили и schema-validation сценарии
- `Gates`: `dotnet test tests/DDCRM.Worker.Api.Tests` (зелёный локальный прогон), `./eng/contracts.ps1 contracts:validate`, `./eng/contracts.ps1 contracts:lint`, `./eng/contracts.ps1 contracts:diff:worker` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.Worker.Api/Simulation/TestWorkerOptions.cs`, `src/DDCRM.Worker.Api/Program.cs`, `tests/DDCRM.Worker.Api.Tests/Infrastructure/WorkerApiFactory.cs`, `tests/DDCRM.Worker.Api.Tests/WorkerApiIntegrationTests.cs`, `docs/api-contracts/openapi-worker.yaml`, `.env.test-worker.example`, `docker-compose.yml`, `docs/standards/test-worker-governance.md`, `docs/testing/test-worker-checklist.md`, `docs/testing/worker-contract-checklist.md`, `docs/standards/runtime-configuration.md`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

### W7-T06
- `Task ID`: `W7-T06`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: улучшить UX/testability frontend-консоли: добавить стабильные `data-testid` для ключевых сценариев (`auth`, `projects`, `accounts`, `tabs`, `status`), уточнить project-first сценарий в интерфейсе и подготовить компонентные UI-тесты под регрессионный прогон
- `Gates`: `frontend:lint`, `frontend:test`, `frontend:build`, `docker:compose-up`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/auth-screen.tsx`, `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/page.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/vitest.config.ts`, `src/ddcrm-rbac-ui/src/test/setup.ts`, `src/ddcrm-rbac-ui/src/components/auth-screen.test.tsx`, `src/ddcrm-rbac-ui/src/components/platform-console.test.tsx`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W7-T06`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: в UI добавлены сценарные подсказки и тестовые якоря для потока «вход -> проекты -> открыть проект -> аккаунты/вкладки», обновлены дефолтные marketplace-примеры под целевые платформы (`funpay/...`), настроен `vitest + jsdom` для component-тестов; добавлены тесты `AuthScreen` и `PlatformConsole` (логин, открытие проекта, создание проекта, добавление аккаунта); подтверждён локальный build frontend и контейнерный запуск полного стенда
- `Gates`: `npm run lint`, `npm run test:run`, `npm run build` (зелёные локальные прогоны), `docker compose up -d --build`, `docker compose ps`, smoke: `curl http://localhost:3000 -> 200`, `curl http://localhost:5073/health -> 200`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/components/auth-screen.tsx`, `src/ddcrm-rbac-ui/src/components/platform-console.tsx`, `src/ddcrm-rbac-ui/src/app/page.tsx`, `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/vitest.config.ts`, `src/ddcrm-rbac-ui/src/test/setup.ts`, `src/ddcrm-rbac-ui/src/components/auth-screen.test.tsx`, `src/ddcrm-rbac-ui/src/components/platform-console.test.tsx`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

## Wave 8 (Route-driven UI + Worker control-plane v1)

### W8-T01
- `Task ID`: `W8-T01`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: перевести UI на route-driven сценарий (`/login`, `/projects`, `/projects/[projectId]/accounts|products|messages|schemas`) и убрать legacy-секции из основного UX
- `Gates`: `frontend:lint`, `frontend:test`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/app/*`, `src/ddcrm-rbac-ui/src/components/*`, `src/ddcrm-rbac-ui/src/lib/*`, `src/ddcrm-rbac-ui/src/hooks/*`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W8-T01`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: реализован project-route UX: отдельный `/projects` список, project-shell с табами `accounts/products/messages/schemas`, session guard с возвратом на целевой route, персист выбранного аккаунта по `projectId`; удалены из основного сценария `profile/activity/overview/proxy/billing/gateway/members`
- `Gates`: `npm run lint`, `npm run test:run`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/app/page.tsx`, `src/ddcrm-rbac-ui/src/app/login/page.tsx`, `src/ddcrm-rbac-ui/src/app/projects/page.tsx`, `src/ddcrm-rbac-ui/src/app/projects/[projectId]/*`, `src/ddcrm-rbac-ui/src/components/project-shell.tsx`, `src/ddcrm-rbac-ui/src/components/login-page-client.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/*`, `src/ddcrm-rbac-ui/src/components/account-selector.tsx`, `src/ddcrm-rbac-ui/src/lib/use-session-guard.ts`, `src/ddcrm-rbac-ui/src/lib/project-account-selection.ts`, `src/ddcrm-rbac-ui/src/hooks/use-project-accounts.ts`, `src/ddcrm-rbac-ui/src/lib/worker-result.ts`, `src/ddcrm-rbac-ui/src/app/globals.css`
- `Date`: `2026-04-25`

### W8-T02
- `Task ID`: `W8-T02`
- `WP`: `WP-ACCOUNTS-MANAGER`
- `Status`: `started`
- `Operations`: расширить Accounts Manager до worker control-plane v1 (`worker-servers registry`, `heartbeat`, `placement`, `rebalance/migrate`) без автоспавна контейнеров
- `Gates`: `contracts:test:services`, `contracts:test:security`
- `Exception`: n/a
- `Changed files`: `src/DDCRM.AccountsManager.Api/*`, `src/DDCRM.AccountsManager.Persistence/*`, `tests/DDCRM.AccountsManager.Api.Tests/*`, `docs/api-contracts/openapi-internal.yaml`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W8-T02`
- `WP`: `WP-ACCOUNTS-MANAGER`
- `Status`: `completed`
- `Operations`: добавлены internal endpoint-ы `workerServersList/workerServerUpsert/workerServerHeartbeat/lifecycleRebalance`; реализованы placement правила (least-loaded healthy active), авто-выбор target для `migrate` без target, fallback `srv-default` при пустом registry, учёт `load/capacity/health/heartbeat`; `WorkerControlHttpClient` поддерживает `baseUrlTemplate` выбранного server-а с env-fallback; добавлена EF migration `WorkerServerControlPlaneV1`
- `Gates`: `dotnet build DigitalDealsCRM.slnx`, `dotnet test tests/DDCRM.AccountsManager.Api.Tests`, `./eng/contracts.ps1 contracts:test:services`, `./eng/contracts.ps1 contracts:test:security` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/DDCRM.AccountsManager.Api/Program.cs`, `src/DDCRM.AccountsManager.Api/Worker/*`, `src/DDCRM.AccountsManager.Persistence/AccountsManagerDbContext.cs`, `src/DDCRM.AccountsManager.Persistence/Entities/WorkerServerEntity.cs`, `src/DDCRM.AccountsManager.Persistence/Migrations/20260425143829_WorkerServerControlPlaneV1*`, `src/DDCRM.AccountsManager.Persistence/Migrations/AccountsManagerDbContextModelSnapshot.cs`, `tests/DDCRM.AccountsManager.Api.Tests/AccountsManagerApiIntegrationTests.cs`, `tests/DDCRM.AccountsManager.Api.Tests/Infrastructure/*`, `docs/api-contracts/openapi-internal.yaml`
- `Date`: `2026-04-25`

### W8-T03
- `Task ID`: `W8-T03`
- `WP`: `process`, `WP-RBAC-UI`, `WP-ACCOUNTS-MANAGER`
- `Status`: `started`
- `Operations`: синхронизировать документацию/канонику под worker control-plane операции и route-driven UI, добавить frontend тесты автозагрузки/deeplink/login-guard
- `Gates`: `contracts:validate`, `contracts:lint`, `contracts:diff:external`, `contracts:diff:worker`, `frontend:test`
- `Exception`: n/a
- `Changed files`: `docs/*`, `src/ddcrm-rbac-ui/src/**/*.test.tsx`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W8-T03`
- `WP`: `process`, `WP-RBAC-UI`, `WP-ACCOUNTS-MANAGER`
- `Status`: `completed`
- `Operations`: обновлены зависимые документы (`api-contracts`, `technical-specification`, `delivery-work-packages`, `internal-contract-checklist`, `architecture_draft/07-account-worker`) под новый worker control-plane и placement policy; добавлены frontend тесты для deeplink/guard/autoload/messages-history lazy-load; подтверждены OpenAPI-gates и diff-check без нерегламентированных breaking-изменений
- `Gates`: `./eng/contracts.ps1 contracts:validate`, `./eng/contracts.ps1 contracts:lint`, `./eng/contracts.ps1 contracts:diff:external`, `./eng/contracts.ps1 contracts:diff:worker`, `npm run test:run` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `docs/api-contracts/api-contracts.md`, `docs/spec/technical-specification.md`, `docs/implementation/delivery-work-packages.md`, `docs/testing/internal-contract-checklist.md`, `docs/architecture_draft/07-account-worker.md`, `src/ddcrm-rbac-ui/src/lib/use-session-guard.test.tsx`, `src/ddcrm-rbac-ui/src/app/projects/project-routes.test.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/products-panel.test.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/messages-panel.test.tsx`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

### W8-T04
- `Task ID`: `W8-T04`
- `WP`: `WP-RBAC-UI`
- `Status`: `started`
- `Operations`: улучшить UX route-driven UI под операционный CRM/seller workflow: добавить обзорные метрики, фильтрацию/поиск, более читаемую структуру списков и рабочую форму редактирования товаров/сообщений/схем
- `Gates`: `frontend:lint`, `frontend:test`, `frontend:build`
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/src/components/project-shell.tsx`, `src/ddcrm-rbac-ui/src/app/projects/page.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/accounts-panel.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/products-panel.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/messages-panel.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/schemas-panel.tsx`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`

- `Task ID`: `W8-T04`
- `WP`: `WP-RBAC-UI`
- `Status`: `completed`
- `Operations`: UI приведён к формату «проектный control workspace»: `/projects` получил портфельные KPI, фильтры и операционный focus-блок; `project-shell` усилен поиском по проектам, метриками и табами с контекстом; во вкладках добавлены summary-карточки, улучшенные списки, поисковые фильтры, `products.update` редактор, inbox-паттерн для сообщений (conversation list + lazy thread + quick templates), schema-catalog с выбором и raw payload preview; стили переработаны под единый CRM-вид и адаптив
- `Gates`: `npm run lint`, `npm run test:run`, `npm run build` (зелёные локальные прогоны)
- `Exception`: n/a
- `Changed files`: `src/ddcrm-rbac-ui/src/app/globals.css`, `src/ddcrm-rbac-ui/src/components/project-shell.tsx`, `src/ddcrm-rbac-ui/src/app/projects/page.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/accounts-panel.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/products-panel.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/messages-panel.tsx`, `src/ddcrm-rbac-ui/src/components/project-pages/schemas-panel.tsx`, `docs/implementation/patchnotes.md`
- `Date`: `2026-04-25`
