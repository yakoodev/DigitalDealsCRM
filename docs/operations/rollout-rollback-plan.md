# DDCRM — Rollout and Rollback Plan (черновик)

Канонический источник порогов:
- `docs/standards/quality-gates.md`

## 1. Стратегия релиза
- релиз по фазам: dev -> staging -> production
- для критических сервисов (Gateway, Billing, Entitlement) использовать canary rollout
- включение новых проверок доступа через feature flags

## 2. Pre-release checklist
- миграции БД проверены на staging
- contract-tests сервисов зелёные
- E2E сценарии платежей и lifecycle зелёные
- runbook обновлён под релизные изменения
- runtime-переменные из `docs/standards/runtime-configuration.md` заданы в целевой среде
- проверены CORS preflight и allowlist origins для external API
- проверено, что internal API отклоняет запросы без валидного `X-Service-Token`
- проверено, что worker API отклоняет запросы без валидного `X-Service-Token`
- подтверждена изоляция service-auth токенов между internal и worker контурами
- для релизных изменений auth-конфигурации подготовлен `docs/operations/service-auth-rotation-playbook.md`
- есть нагрузочный прогон staging с проверкой `QG-SLO-LAT-GW-CHECK-P95`, `QG-SLO-LAT-CORE-P95`, `QG-SLO-WEBHOOK-P95`, `QG-SLO-ENT-RECALC-P95`

## 3. Rollout steps
- выкатить изменения Billing и Entitlement
- выкатить Gateway и Route Registry DB миграции
- выкатить Core и Accounts Manager
- включить флаги поэтапно
- мониторить ошибки и бизнес-метрики после каждого шага

## 4. Триггеры rollback
- `QG-INC-SEV1-5XX-RATE`
- потеря консистентности подписки/entitlement
- `QG-INC-SEV1-ROUTE-NOT-FOUND-RATE`
- `QG-INC-SEV2-GW-CHECK-P95`
- `QG-INC-SEV2-CORS-PREFLIGHT-FAIL-RATE`
- `QG-INC-SEV2-INTERNAL-AUTH-FAIL-RATE`
- `QG-INC-SEV2-WORKER-AUTH-FAIL-RATE`
- `QG-INC-SEV1-WEBHOOK-OLDEST-AGE`
- нарушение `QG-SLO-ENT-RECALC-P99`
- нарушение финансовых ограничений ролей

## 5. Rollback steps
- отключить новые feature flags
- откатить сервисы в обратном порядке
- восстановить route-состояние из последнего валидного snapshot
- запустить reconciliation платежей и entitlement
- подтвердить восстановление ключевых пользовательских сценариев
- подтвердить recovery-критерии `QG-REC-GW-5XX-RATE`, `QG-REC-ROUTE-NOT-FOUND-RATE`, `QG-REC-WEBHOOK-P95`, `QG-REC-ENT-RECALC-P95`
- подтвердить recovery-критерии `QG-REC-CORS-PREFLIGHT-FAIL-RATE`, `QG-REC-INTERNAL-AUTH-FAIL-RATE`, `QG-REC-WORKER-AUTH-FAIL-RATE`

## 6. Post-rollback
- зафиксировать incident report
- обновить runbook
- добавить регрессионные тесты на причину отката
- обновить SLO baseline в release-отчёте (до/после rollback)
