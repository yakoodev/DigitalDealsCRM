# Identity & Access

## Назначение
Сервис ролей и доступов.

Канонический источник матрицы ролей и прав:
- `docs/standards/access-control-matrix.md`

## Типы ролей проекта
Фиксированные роли проекта определяются в `docs/standards/access-control-matrix.md`.

## Правила по ролям
Матрица проектных permission и ограничения ролей определяются в `docs/standards/access-control-matrix.md`.

## Ограничения модели
- в проекте только один владелец
- у одного пользователя могут быть разные роли в разных проектах

## Системные админы DDCRM
Это отдельная сущность, не связанная с проектными ролями.  
Права системного админа определяются в `docs/standards/access-control-matrix.md`.

## На будущее
Нужно заложить возможность появления внутренних ролей DDCRM:
- support
- finance manager
- product admin
- superadmin

## Интеграция с Gateway
- Gateway проверяет роль и membership через claims токена и membership cache
- membership cache должен обновляться по событиям IAM
- при изменении роли требуется принудительный cache bust/invalidate
