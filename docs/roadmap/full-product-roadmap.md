# DDCRM — Roadmap полной реализации

## Цель roadmap
Довести продукт от архитектурного фундамента до полной продуктовой реализации: Core CRM + коммерческий контур + модульная работа с площадками + операционная зрелость.

## Формат этапов
Roadmap разделён на фазы с gate-критериями.  
Переход в следующую фазу допускается только после закрытия критериев предыдущей.

Исполняемое разбиение работ для команд разработки:
- `docs/implementation/delivery-work-packages.md`

## Фаза 0. Документация и архитектурная консолидация
### Результат
- единая архитектурная модель без противоречий
- зафиксированное ТЗ
- зафиксированные API-контракты в формате OpenAPI 3.1 (`external/internal/worker`), ERD, state machine, test strategy, runbook, rollout/rollback
- зафиксированные числовые NFR/SLO и пороги алертов

### Gate
- все документы из `docs/README.md` существуют и синхронизированы
- OpenAPI-контракты валидны и используются как источник для контрактных тестов
- правила OpenAPI-совместимости и contract quality gates зафиксированы в `docs/standards/openapi-governance.md`
- профиль запуска contract quality gates в CI зафиксирован в `docs/testing/contract-gates-execution.md`
- runtime-источники CORS/service-auth (`external/internal/worker`) зафиксированы в `docs/standards/runtime-configuration.md`
- `docs/standards/quality-gates.md` принят как единый источник числовых порогов
- ТЗ, runbook, rollout/rollback и test strategy ссылаются на ID quality gates без дублирования чисел

## Фаза 1. Платформенный фундамент
### Результат
- Core + Core DB
- IAM (проектные роли + membership)
- Accounts Manager + Accounts Manager DB
- Route Registry DB
- Gateway как единая точка входа
- базовый lifecycle account worker

### Gate
- create/update/delete/migrate аккаунта работают end-to-end
- route upsert/remove/switch работает через Route Registry DB
- обязательный proxy-конфиг проходит валидацию
- на staging подтверждены `QG-SLO-LAT-GW-CHECK-P95`, `QG-SLO-LAT-GW-CHECK-P99`
- на staging подтверждено `QG-SLO-IAM-CACHE-INVALIDATE-P99`

## Фаза 2. Коммерческий контур
### Результат
- Billing как источник истины по оплате/подписке
- интеграция с эквайрингом (payment + webhook)
- Entitlement как источник истины по доступам
- trial/grace по политике тарифа
- сценарии downgrade/unpaid с выборочной блокировкой операций

### Gate
- оплата и активация entitlement проходят end-to-end
- дедуп webhook работает
- ручная активация и возврат поддерживаются
- подтверждены `QG-SLO-WEBHOOK-P95`, `QG-SLO-ENT-RECALC-P95`
- подтверждён `QG-SLO-WEBHOOK-DEDUP-TTL`

## Фаза 3. Админка и контроль доступа
### Результат
- проектные роли в UI
- финансовые экраны только для owner/admin
- системная админка DDCRM
- override с reason/actor/expiresAt и аудитом

### Gate
- модератор не видит финансовые разделы
- аудит override доступен в UI и в логах

## Фаза 4. Продуктовые модули Core (операционный слой)
### Результат
- товары
- единый inbox
- заказы
- базовые рабочие операции по каждому модулю

### Gate
- модули доступны по role/entitlement-правилам
- есть связка модулей с account API операциями

## Фаза 5. Аналитика и автоматизация
### Результат
- аналитика по продажам и активности
- автоматизация рутинных сценариев
- события и триггеры на ключевые бизнес-состояния

### Gate
- есть основные отчёты по проекту и площадкам
- есть минимум 3-5 production-ready автоматизаций

## Фаза 6. Эксплуатационная зрелость
### Результат
- централизованные логи, трассировка, алерты
- стабильный runbook инцидентов
- формализованный rollout/rollback
- disaster-процедуры

### Gate
- команда может восстановить сервисы по runbook без ad-hoc решений
- релизы проходят повторяемо по rollout-плану
- SLO по доступности (`QG-SLO-AVAIL-GATEWAY`, `QG-SLO-AVAIL-CORE`, `QG-SLO-AVAIL-BILLING`, `QG-SLO-AVAIL-ENTITLEMENT`) соблюдается 2 релизных цикла подряд
- disaster-тест подтверждает `QG-DR-RTO-CRITICAL` и `QG-DR-RPO-STATE`

## Фаза 7. Полная продуктовая готовность
### Результат
- покрыты все обязательные пользовательские сценарии DDCRM
- закрыты основные риски интеграций площадок
- архитектура и документация поддерживаются как living docs

### Gate
- продукт готов к масштабной эксплуатации и расширению платформ

## Сквозные задачи всех фаз
- поддержание консистентности документации
- обратная совместимость API при изменениях
- регресс-тестирование ключевых коммерческих и lifecycle-потоков
