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

- `Task ID`: `W1-T09`
- `WP`: `process`
- `Status`: `completed`
- `Operations`: зафиксирован шаблон patchnote и обязательные статусы `started/completed`
- `Gates`: n/a
- `Exception`: n/a
- `Changed files`: `docs/implementation/patchnotes.md`
- `Date`: `2026-04-24`
