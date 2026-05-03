# DDCRM — Runtime Configuration (канонический)

## Назначение
Единый источник правил runtime-конфигурации через env-шаблоны.

Правило:
- env-шаблоны и правила их изменения определяются только здесь;
- остальные документы ссылаются на этот стандарт без копирования списков переменных.

## 1. Канонические env-шаблоны
- `.env.external-api.example` — runtime-конфигурация CORS для external API;
- `.env.internal-api.example` — runtime-конфигурация service-auth для internal API и внутренних client-настроек;
- `.env.worker-api.example` — runtime-конфигурация service-auth для worker API;
- `.env.test-worker.example` — runtime-профиль тестового воркера (симулятора) для non-production контуров.

## 2. Конвенции env-переменных
- `EXTERNAL_API_CORS_*` — переменные CORS-контура external API;
- `EXTERNAL_API_JWT_*` — переменные валидации bearer JWT для external API;
- `EXTERNAL_API_SYSTEM_PERMISSION_*` — переменные проверки system-claim для `external` admin endpoint-ов (`/v1/admin/*`);
- `EXTERNAL_API_SYSTEM_INTEGRATIONS_PERMISSION_CLAIM_VALUE` — system permission для admin управления integration grant-ами и Telegram proxy-профилями;
- `FEATURE_OFFERS_ENABLED`, `FEATURE_WORKFLOWS_ENABLED`, `FEATURE_CUSTOM_HTTP_INTEGRATIONS_ENABLED` — feature-flag rollout для Offer/Workflow/Custom HTTP контура в Core external API;
- `WORKFLOW_PURCHASE_WEBHOOK_SECRET` — секрет входящего purchase webhook (`/v1/integrations/workflow/purchase`) для асинхронного запуска workflow;
- `WorkflowMessagePolling:*` + `RouteRegistryClient__BaseUrl` — runtime-настройки message polling bridge (Core -> Route Registry -> Worker API) для запуска `MessageStart` workflow по входящим сообщениям из worker conversations (`OnlyUnreadConversations` для poll-scope, `MaxMessagesPerConversationPerPoll` для анти-бёрст ограничения, `SkipFirstMessagePerConversation` для fail-safe bootstrap без запуска на историческом хвосте, `DispatchWorkerReplies` для безопасного dry-run без реальной отправки в marketplace);
- `INTERNAL_API_SERVICE_AUTH_*` — переменные service-auth контура internal API;
- `WORKER_API_SERVICE_AUTH_*` — переменные service-auth контура worker API;
- `CORE_SECRETS_ENCRYPTION_KEY` — ключ шифрования секретов интеграций в Core DB (project service tokens, custom HTTP bearer tokens, Telegram proxy credentials);
- `FUNPAYSTAT_INTEGRATION_SERVICE_TOKEN` — service-auth токен DDCRM -> FunPayStat integration API;
- `FUNPAYSTAT_PROJECT_TOKEN_SIGNING_SALT` — salt для валидации project token fingerprint в FunPayStat;
- `TELEGRAM_NOTIFICATION_BOT_TOKEN` — глобальный bot token Notification Bus (v1 Telegram);
- `TELEGRAM_LINK_WEBHOOK_SECRET` — секрет подтверждения Telegram link-code callback;
- `IntegrationWorkerRuntime:*` — runtime-настройки worker integration bus в Core API (platform alias + fallback proxy defaults для provision Steam runtime);
- `WORKER_PROXY_CREDENTIALS_ENCRYPTION_KEY` — ключ шифрования proxy credentials в worker state storage;
- `WORKER_MARKETPLACE_AUTH_ENCRYPTION_KEY` — ключ шифрования marketplace auth credentials в worker state storage;
- `DDCRM_WORKER_ACCOUNT_ID` — runtime account binding для single-tenant worker instance (инжектится Accounts Manager autospawn на каждый spawned worker);
- `FUNPAY_WORKER_ACCOUNT_ID` — platform-alias runtime account binding для FunPay worker (значение синхронизируется с `DDCRM_WORKER_ACCOUNT_ID`);
- `PLAYEROK_WORKER_ACCOUNT_ID` — platform-alias runtime account binding для Playerok worker (значение синхронизируется с `DDCRM_WORKER_ACCOUNT_ID`);
- `WORKER_CONTROL_CLIENT_*` — переменные internal worker-control client-а (service-to-service вызовы internal -> worker);
- `FunPayStatClient:*`, `EntitlementCheckClient:*`, `TelegramNotifications:*` — секции appsettings/ENV для integration bus runtime-клиентов и Telegram sender/polling (`Enabled`, `ApiBaseUrl`, `FallbackToDirectOnProxyFailure`, `RequestTimeoutSeconds`, `PollingEnabled`, `PollingIntervalSeconds`, `PollingTimeoutSeconds`, `PollingBatchSize`);
- `ACCOUNT_MANAGER_AUTOSPAWN_*` — переменные Docker-autospawn orchestration в Accounts Manager;
- `ACCOUNTS_MANAGER_ENABLE_PLAYEROK_TEMPLATE` — включает/отключает дефолтный test-template `test-worker.playerok` в Accounts Manager (для локального Steam/FunPay-only профиля можно ставить `false`);
- `STEAM_INTEGRATION_RUNTIME_*` — дефолтные env для spawned Steam integration runtime (`POSTGRES/REDIS` соединения и bootstrap admin secrets);
- `ACCOUNT_MANAGER_AUTOSPAWN_REGISTRY_*` — переменные secret-handling для registry credentials в Accounts Manager (`worker-servers` GHCR auth, write-only storage);
- runtime env-шаблоны account-type поддерживают placeholder-ы `{accountId}`, `{accountIdN}`, `{projectId}` (expansion при autospawn), чтобы изолировать per-project/per-runtime storage namespace;
- порт worker runtime выбирается Accounts Manager автоматически: приоритет `account-types.runtime.containerPort`, fallback `ACCOUNT_MANAGER_AUTOSPAWN_WORKER_INTERNAL_PORT` (ручная настройка порта на уровне `worker-servers` не используется);
- `TEST_WORKER_*` — переменные управления тестовым воркером по `docs/standards/test-worker-governance.md`;
- `TEST_WORKER_PROVIDER` — выбор provider-профиля симулятора (`funpay/playerok/ggsell/platimarket`);
- `TEST_WORKER_INSTANCE_ID` — метка инстанса симулятора для диагностики и агрегации данных multi-worker в UI;
- булевы значения задаются как `true/false` (lowercase);
- списки значений задаются comma-separated строкой без пробелов.

## 2.1 Изоляция service-auth токенов
- токены `internal` и `worker` контуров должны быть раздельными;
- пересечение значений между `INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS` и `WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS` запрещено;
- клиентские токены `INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN` и `WORKER_API_SERVICE_AUTH_CLIENT_TOKEN` не должны совпадать.

## 2.2 Контурные ограничения тестового воркера
- тестовый воркер включается только в `local/ci/staging`;
- в production включение тестового воркера должно быть заблокировано (`TEST_WORKER_BLOCK_IN_PRODUCTION=true`);
- тестовый воркер по умолчанию скрыт и активируется только явной env-настройкой;
- `ext.test.*` действия разрешены только при включённом non-production профиле симулятора.

## 3. Политика секретов
- значения токенов и секретов в production не хранятся в git;
- `.env.*.example` содержат только шаблонные значения;
- реальные значения должны поставляться через secret manager/CI variables.
- при `ACCOUNT_MANAGER_AUTOSPAWN_ENABLED=true` Docker endpoint/socket (`ACCOUNT_MANAGER_AUTOSPAWN_DOCKER_ENDPOINT`) должен передаваться в runtime безопасным способом (socket mount/secret-managed host endpoint).
- ключ `ACCOUNT_MANAGER_AUTOSPAWN_REGISTRY_SECRET_ENCRYPTION_KEY` обязателен для шифрования per-server registry токенов (не короче 32 символов, только secret storage);
- registry токены из `worker-servers` сохраняются только в зашифрованном виде и никогда не возвращаются API-ответами (`write-only` поведение).

## 4. Процесс изменения
- добавить/изменить переменную сначала в соответствующем `.env.*.example`;
- синхронизировать этот стандарт при изменении префиксов/правил формата;
- при изменении переменных тестового воркера синхронизировать `docs/standards/test-worker-governance.md`;
- проверить связанные OpenAPI-контракты и contract-tests.

## 5. Связанные документы
- `docs/standards/source-of-truth-map.md`
- `docs/standards/openapi-governance.md`
- `docs/standards/test-worker-governance.md`
- `docs/standards/documentation-governance.md`
- `docs/operations/rollout-rollback-plan.md`
- `docs/operations/runbook.md`
- `docs/operations/service-auth-rotation-playbook.md`
- `.env.external-api.example`
- `.env.internal-api.example`
- `.env.worker-api.example`
- `.env.test-worker.example`
