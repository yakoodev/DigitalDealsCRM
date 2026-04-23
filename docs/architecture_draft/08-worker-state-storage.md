# Worker State Storage

## Назначение
Внешнее хранилище рабочего состояния account worker-ов.

## Что хранит
- session state
- cookies / токены площадки
- proxy credentials / proxy state
- временные platform-specific данные
- runtime cache
- прочее служебное состояние account worker-а

## Требования хранения
- секреты хранятся только в зашифрованном виде
- Core DB не хранит proxy credentials
