# DDCRM — Quality Gates Catalog (канонический)

## Назначение
Единый источник истины для всех числовых NFR/SLO, порогов инцидентов и критериев восстановления.

Правило:
- числовые значения хранятся только в этом файле;
- остальные документы ссылаются на идентификаторы (ID) из этого каталога;
- изменение порога выполняется в одном месте: здесь.

## 1. SLO по доступности (месячное окно)
| ID | Метрика | Цель |
|---|---|---|
| `QG-SLO-AVAIL-GATEWAY` | Account API Gateway availability | `>= 99.9%` |
| `QG-SLO-AVAIL-CORE` | Core API availability | `>= 99.5%` |
| `QG-SLO-AVAIL-BILLING` | Billing API availability | `>= 99.5%` |
| `QG-SLO-AVAIL-ENTITLEMENT` | Entitlement API availability | `>= 99.5%` |

## 2. Производительность API
| ID | Метрика | Цель |
|---|---|---|
| `QG-SLO-LAT-CORE-P95` | Core API latency p95 | `<= 300ms` |
| `QG-SLO-LAT-CORE-P99` | Core API latency p99 | `<= 700ms` |
| `QG-SLO-LAT-GW-CHECK-P95` | Gateway (resolve + auth + entitlement) p95 | `<= 120ms` |
| `QG-SLO-LAT-GW-CHECK-P99` | Gateway (resolve + auth + entitlement) p99 | `<= 250ms` |
| `QG-SLO-LAT-GW-PROXY-P95` | Gateway proxy end-to-end p95 (без времени площадки) | `<= 800ms` |
| `QG-SLO-LAT-ROUTE-UPSERT-P95` | Route upsert/switch p95 | `<= 300ms` |
| `QG-SLO-LAT-ROUTE-RESOLVE-P95` | Route resolve p95 | `<= 120ms` |

## 3. Коммерческие и доступовые потоки
| ID | Метрика | Цель |
|---|---|---|
| `QG-SLO-WEBHOOK-P95` | Billing webhook processing p95 | `<= 60s` |
| `QG-SLO-WEBHOOK-P99` | Billing webhook processing p99 | `<= 300s` |
| `QG-SLO-WEBHOOK-DEDUP-TTL` | Dedup key retention window | `>= 72h` |
| `QG-SLO-ENT-RECALC-P95` | Entitlement recalculation p95 | `<= 30s` |
| `QG-SLO-ENT-RECALC-P99` | Entitlement recalculation p99 | `<= 120s` |
| `QG-SLO-IAM-CACHE-INVALIDATE-P99` | Membership cache invalidation lag p99 | `<= 30s` |

## 4. Disaster Recovery
| ID | Метрика | Цель |
|---|---|---|
| `QG-DR-RTO-CRITICAL` | RTO для Gateway/Billing/Entitlement | `<= 30m` |
| `QG-DR-RPO-STATE` | RPO для Billing/Entitlement/Route Registry | `<= 5m` |

## 5. Observability и аудит
| ID | Метрика | Цель |
|---|---|---|
| `QG-OBS-REQUESTID-COVERAGE` | Mutating API с `requestId` в логах | `= 100%` |
| `QG-OBS-AUDIT-LAG` | Billing/override audit lag | `<= 60s` |
| `QG-OBS-SEV1-ALERT-DELIVERY` | Доставка Sev-1 alert до on-call | `<= 1m` |

## 6. Пороги классификации инцидентов
| ID | Класс | Порог |
|---|---|---|
| `QG-INC-SEV1-5XX-RATE` | Sev-1 | `5xx > 2%` за `5m` на Gateway/Billing/Entitlement |
| `QG-INC-SEV1-ROUTE-NOT-FOUND-RATE` | Sev-1 | `ROUTE_NOT_FOUND > 1%` за `5m` |
| `QG-INC-SEV1-WEBHOOK-OLDEST-AGE` | Sev-1 | возраст старейшего webhook `> 300s` |
| `QG-INC-SEV2-GW-CHECK-P95` | Sev-2 | Gateway check p95 `> 250ms` за `10m` |
| `QG-INC-SEV2-IAM-CACHE-P99` | Sev-2 | IAM cache invalidation p99 `> 30s` за `10m` |

## 7. Критерии восстановления после инцидента/rollback
| ID | Критерий |
|---|---|
| `QG-REC-GW-5XX-RATE` | Gateway `5xx < 1%` за `15m` |
| `QG-REC-GW-CHECK-P95` | Gateway check p95 `<= 120ms` за `15m` |
| `QG-REC-ROUTE-NOT-FOUND-RATE` | `ROUTE_NOT_FOUND < 0.1%` за `15m` |
| `QG-REC-ROUTE-RESOLVE-P95` | Route resolve p95 `<= 120ms` за `15m` |
| `QG-REC-WEBHOOK-P95` | webhook p95 `<= 60s` |
| `QG-REC-WEBHOOK-OLDEST-AGE` | возраст старейшего webhook `<= 60s` |
| `QG-REC-ENT-RECALC-P95` | entitlement recalculation p95 `<= 30s` |
| `QG-REC-FALSE-BLOCKED-RATE` | ошибочно заблокированные разрешённые операции `< 0.1%` за `30m` |

## 8. Операционные SLA реакции
| ID | Цель |
|---|---|
| `QG-OPS-MTTA-SEV1` | MTTA для Sev-1 `<= 10m` |
| `QG-OPS-MITIGATION-START-SEV1` | начало mitigation для Sev-1 `<= 15m` |
| `QG-OPS-ESCALATE-L2-SEV1` | эскалация на L2 `<= 10m` от детекта |
| `QG-OPS-ESCALATE-L3-SEV1` | эскалация на L3 `<= 20m`, если mitigation неэффективен |
