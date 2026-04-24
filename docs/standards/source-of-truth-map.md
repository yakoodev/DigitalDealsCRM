# DDCRM — Source of Truth Map (канонический)

## Назначение
Единая карта: какая тема меняется в каком документе.

## Канонические источники по темам
| Тема | Канонический документ |
|---|---|
| OpenAPI правила, quality gates, совместимость | `docs/standards/openapi-governance.md` |
| Runtime env-конфигурация (`external/internal/worker`) | `docs/standards/runtime-configuration.md` |
| Роли и права доступа (RBAC) | `docs/standards/access-control-matrix.md` |
| Action key формат и extension-политика | `docs/standards/worker-action-conventions.md` |
| NFR/SLO и пороги инцидентов | `docs/standards/quality-gates.md` |
| Структура реализации (work packages) | `docs/implementation/delivery-work-packages.md` |
| Общие правила сопровождения документации | `docs/standards/documentation-governance.md` |

## Операционные источники
| Тема | Канонический документ |
|---|---|
| Инцидентные процедуры | `docs/operations/runbook.md` |
| Релиз/rollback порядок | `docs/operations/rollout-rollback-plan.md` |
| Ротация service-auth токенов | `docs/operations/service-auth-rotation-playbook.md` |

## Тестовые источники
| Тема | Канонический документ |
|---|---|
| Общая тест-стратегия | `docs/testing/test-strategy.md` |
| Профиль запуска контрактных gate в CI | `docs/testing/contract-gates-execution.md` |
| Детальные проверки internal API контракта | `docs/testing/internal-contract-checklist.md` |
| Детальные проверки worker API контракта | `docs/testing/worker-contract-checklist.md` |

## Правило изменения
- перед изменением темы определить её канонический источник в этой карте;
- сначала менять канонический документ;
- в зависимых документах обновлять только ссылки/ID без копирования подробностей.
