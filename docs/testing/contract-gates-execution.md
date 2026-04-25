# DDCRM — Contract Gates Execution Profile

## Назначение
Единый исполняемый профиль запуска контрактных проверок OpenAPI и связанных quality gates.

Правило:
- `OAG-*` gate считается выполненным только при наличии автоматизированного прогона в CI;
- локальный запуск и CI должны использовать один и тот же набор проверок (разница только в режиме/fail-policy).

## 1. Scope
- `docs/api-contracts/openapi-external.yaml`
- `docs/api-contracts/openapi-internal.yaml`
- `docs/api-contracts/openapi-worker.yaml`
- `docs/api-contracts/openapi-common.yaml`

## 2. Матрица gate -> проверка
| Gate ID | Что проверяем | Минимальная автоматизация |
|---|---|---|
| `OAG-VAL-OPENAPI31` | валидность OpenAPI 3.1 + отсутствие битых `$ref` | schema validation + ref resolution |
| `OAG-LINT-STYLE` | style/lint правила для контрактов | linter ruleset в CI |
| `OAG-BREAKING-EXTERNAL` | breaking changes в external API | openapi diff against main baseline |
| `OAG-BREAKING-WORKER` | breaking changes в worker API | openapi diff against main baseline |
| `OAG-TEST-CONTRACT-SERVICES` | contract tests для external/internal | integration contract-tests |
| `OAG-TEST-CONTRACT-WORKER` | contract tests worker implementations | worker contract tests + mandatory `TW-SCN-*` набор |
| `OAG-TEST-CAPABILITY-ACTION` | соответствие capability/action policy | negative + positive tests по `ext.*` + policy для `ext.test.*` |
| `OAG-TEST-INTERNAL-SERVICE-AUTH` | internal auth enforcement | 401/403 tests without/invalid token |
| `OAG-TEST-WORKER-SERVICE-AUTH` | worker auth enforcement | 401/403 tests without/invalid token + `TW-SCN-AUTH-FAIL` |
| `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION` | изоляция internal/worker токенов | cross-contour token misuse tests |
| `OAG-TEST-CORS-EXTERNAL` | CORS/preflight policy | browser-like preflight tests |

## 2.1 Обязательный набор симуляторных сценариев
Каноника сценариев и профилей определяется в `docs/standards/test-worker-governance.md`.

Обязательный минимум для CI:
- `TW-SCN-HAPPY-PATH`
- `TW-SCN-AUTH-FAIL`
- `TW-SCN-TIMEOUT`
- `TW-SCN-CAPABILITY-MISMATCH`
- `TW-SCN-IDEMPOTENCY-REPLAY`
- `TW-SCN-TRANSIENT-ERROR`
- `TW-SCN-MALFORMED-PAYLOAD`
- `TW-SCN-CONTRACT-DRIFT`

## 3. Профили запуска
### 3.1 PR (обязательный fail-fast)
- `OAG-VAL-OPENAPI31`
- `OAG-LINT-STYLE`
- `OAG-BREAKING-EXTERNAL`
- `OAG-BREAKING-WORKER`
- smoke subset для `OAG-TEST-CONTRACT-SERVICES`, `OAG-TEST-CONTRACT-WORKER`
- для worker smoke обязательны `TW-SCN-HAPPY-PATH` и `TW-SCN-IDEMPOTENCY-REPLAY`

### 3.2 Merge to main (обязательный полный)
- все gate из PR профиля
- полный набор `OAG-TEST-CONTRACT-SERVICES`
- полный набор `OAG-TEST-CONTRACT-WORKER`
- `OAG-TEST-CAPABILITY-ACTION`
- `OAG-TEST-INTERNAL-SERVICE-AUTH`
- `OAG-TEST-WORKER-SERVICE-AUTH`
- `OAG-TEST-SERVICE-AUTH-TOKEN-ISOLATION`
- `OAG-TEST-CORS-EXTERNAL`
- обязательны сценарии "ошибка площадки" и "контрактный дрейф" (`TW-SCN-TRANSIENT-ERROR`, `TW-SCN-CONTRACT-DRIFT`)

### 3.3 Pre-release (staging)
- повтор полного merge профиля
- проверка связки с runtime env-конфигурацией по `docs/standards/runtime-configuration.md`
- запуск тестового воркера выполняется по checklist из `docs/testing/test-worker-checklist.md`

## 4. Минимальный интерфейс команд (рекомендуемый)
Репозиторий должен предоставить команды уровня entrypoint (имена могут быть адаптированы под стек):
- `contracts:validate`
- `contracts:lint`
- `contracts:diff:external`
- `contracts:diff:worker`
- `contracts:test:services`
- `contracts:test:worker`
- `contracts:test:security`
- `contracts:test:cors`

## 5. Политика падения пайплайна
- любой красный gate из обязательного профиля блокирует merge/release;
- временный bypass допустим только через документированный exception с owner, reason и сроком.
- для `contracts:diff:*` exception-правила фиксируются в `docs/implementation/contract-gate-exceptions.json` с полями `id/scope/owner/reason/expiresOn/removedOperations`.

## 6. Связанные документы
- `docs/standards/openapi-governance.md`
- `docs/testing/test-strategy.md`
- `docs/testing/internal-contract-checklist.md`
- `docs/testing/worker-contract-checklist.md`
- `docs/testing/test-worker-checklist.md`
- `docs/standards/runtime-configuration.md`
- `docs/standards/test-worker-governance.md`
