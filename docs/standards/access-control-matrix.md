# DDCRM — Access Control Matrix (канонический)

## Назначение
Единый источник истины по ролям и правам доступа.

Правило:
- состав ролей и матрица прав определяются только здесь;
- остальные документы ссылаются на этот стандарт без дублирования списков прав.

## 1. Проектные роли
Фиксированные роли проекта:
- `owner`
- `admin`
- `moderator`

## 2. Инварианты модели
- в проекте ровно один `owner`;
- пользователь может иметь разные проектные роли в разных проектах;
- `moderator` не имеет доступа к финансовым данным и операциям тарифа.
- `moderator` не имеет доступа к lifecycle-изменениям аккаунтов/worker-ов (`create/update/delete/migrate`).
- `moderator` работает только с уже существующими аккаунтами/worker-ами в рамках операционных действий.
- `moderator` не имеет доступа к чувствительным proxy credentials аккаунта.

## 3. Матрица прав проектных ролей
| Permission Key | Owner | Admin | Moderator |
|---|---|---|---|
| `project.members.invite` | ✅ | ✅ | ❌ |
| `project.members.remove` | ✅ | ✅ | ❌ |
| `project.roles.change` | ✅ | ✅ | ❌ |
| `project.accounts.lifecycle.manage` | ✅ | ✅ | ❌ |
| `project.accounts.view` | ✅ | ✅ | ✅ |
| `project.workers.operate` | ✅ | ✅ | ✅ |
| `project.accounts.proxyCredentials.reveal` | ✅ | ✅ | ❌ |
| `project.accounts.proxyCredentials.update` | ✅ | ✅ | ❌ |
| `project.billing.view` | ✅ | ✅ | ❌ |
| `project.billing.changePlan` | ✅ | ✅ | ❌ |
| `project.modules.operate` | ✅ | ✅ | ✅ |
| `project.integrations.use` | ✅ | ✅ | ❌ |

## 4. Системные админы DDCRM
Это отдельная сущность, не совпадающая с проектными ролями.

Правило:
- доступ к `/v1/admin/*` определяется только системными правами;
- проектные роли (`owner/admin/moderator`) сами по себе не дают доступ к системным admin endpoint-ам.

Права системного админа:
- `system.accountManager.manage`
- `system.integrations.manage`
- `system.plans.manage`
- `system.limits.manage`
- `system.entitlements.override`
- `system.subscriptions.manual`
- `system.payments.view`
- `system.projects.block`

## 5. Политика override
- обязательные поля: `reason`, `actor`, `createdAt`, `expiresAt`;
- все override-операции подлежат обязательному аудиту;
- допускается механизм второго подтверждения для длительных override.

## 6. Чувствительные данные аккаунта
- proxy credentials относятся к чувствительным данным аккаунта;
- `reveal` — отдельная явная операция получения полного proxy-конфига для конкретного аккаунта;
- в стандартных `accounts` read-response credentials скрыты/маскированы по умолчанию;
- полный просмотр credentials выполняется только через явную операцию reveal;
- reveal выполняется при валидной активной сессии пользователя; отдельный step-up (повторный пароль/2FA) не требуется;
- отдельный функциональный троттлинг специально для операции reveal не вводится;
- изменение credentials выполняется только через явную операцию update;
- reveal и update обязаны журналироваться в аудит с `actor`, `reason`, `requestId`, `timestamp`;
- право полного просмотра определяется `project.accounts.proxyCredentials.reveal`;
- право изменения определяется `project.accounts.proxyCredentials.update`;
- для `moderator` reveal/update proxy credentials запрещены.

## 7. RBAC для account-api action
- для `account-api` action по умолчанию требуется `project.workers.operate`;
- для `ext.account.lifecycle.*` требуется `project.accounts.lifecycle.manage`;
- для `ext.account.proxy-credentials.reveal` требуется `project.accounts.proxyCredentials.reveal`;
- для `ext.account.proxy-credentials.update` требуется `project.accounts.proxyCredentials.update`;
- для `ext.account.proxy-credentials.apply` требуется `project.accounts.proxyCredentials.update`;
- для `ext.account.marketplace-auth.apply` требуется `project.accounts.lifecycle.manage`;
- для `ext.integration.*.read` требуется `project.integrations.use`;
- для `ext.integration.*.jobs` требуется `project.integrations.use`;
- Gateway обязан применять это сопоставление до проксирования запроса в worker.
