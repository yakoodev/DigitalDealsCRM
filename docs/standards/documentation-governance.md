# DDCRM — Documentation Governance (канонический)

## Назначение
Единый процесс сопровождения документации как `living docs`.

## 1. Статусы документов
- `draft` — рабочий черновик
- `in-review` — на согласовании
- `approved` — принят как текущая каноника
- `deprecated` — устарел, хранится для истории

## 2. Обязательные метаданные для канонических документов
Для документов в `docs/standards/` и ключевых артефактов (`spec`, `api-contracts`, `testing`, `operations`) рекомендуется единый metadata-блок:
- `status`
- `owner`
- `lastReviewedAt`
- `version`
- `sourceOfTruth` (если документ является каноникой)

## 3. Правила изменения каноники
- числовые пороги меняются только в `quality-gates.md`;
- правила `action` меняются только в `worker-action-conventions.md`;
- матрица RBAC меняется только в `access-control-matrix.md`;
- правила OpenAPI-совместимости и contract quality gates меняются только в `openapi-governance.md`;
- runtime env-настройки OpenAPI-контуров меняются только по правилам `docs/standards/runtime-configuration.md`;
- технологический стек backend/frontend/tooling меняется только в `docs/standards/technology-stack.md`;
- каноника тестового воркера и политика `ext.test.*` меняются только в `docs/standards/test-worker-governance.md`;
- правила для coding-агента в репозитории меняются только в `AGENTS.md`;
- изменения в канонике должны сопровождаться ссылочными обновлениями зависимых документов.

## 4. Идентификаторы стандартов
- quality gates: префикс `QG-`
- permission keys: namespace формат `domain.object.action`
- action keys: namespace формат по `worker-action-conventions.md`

## 5. Политика ссылок и дублирования
- в зависимых документах запрещено копировать числовые пороги и полные матрицы прав;
- допустимо использовать только ссылки на канонический документ и/или ID.
- определение канонического документа по теме выполняется через `docs/standards/source-of-truth-map.md`.

## 6. Минимальный cadence ревью
- канонические стандарты: не реже 1 раза в 30 дней
- runbook/rollout: после каждого инцидента Sev-1 или rollback
- API-контракты: при каждом изменении endpoint/schema/кодов ошибок
