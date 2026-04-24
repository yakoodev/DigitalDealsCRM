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
