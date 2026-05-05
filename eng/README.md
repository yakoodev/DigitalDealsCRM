# Contract Entrypoints

Используйте один из entrypoint-команд через обёртку:

- `./eng/contracts.ps1 contracts:validate`
- `./eng/contracts.ps1 contracts:lint`
- `./eng/contracts.ps1 contracts:diff:external`
- `./eng/contracts.ps1 contracts:diff:worker`
- `./eng/contracts.ps1 contracts:test:services`
- `./eng/contracts.ps1 contracts:test:worker`
- `./eng/contracts.ps1 contracts:test:security`
- `./eng/contracts.ps1 contracts:test:cors`

Linux/macOS эквивалент:

- `./eng/contracts.sh <entrypoint>`

## Ops-ready dry-run entrypoint

Полный dry-run `WP-OPS-READY` (docker health + smoke + contract gates):

- `./eng/ops-ready.ps1`
- `./eng/ops-ready.sh`

Быстрый режим (без повторного прогона contract gates):

- `./eng/ops-ready.ps1 -SkipContracts`
- `./eng/ops-ready.sh --skip-contracts`

## Локальная сборка образов (без push)

Сборка образов воркеров:

- `./eng/build-local-images.ps1 workers`
- `./eng/build-local-images.sh workers`

Сборка integration-сервисов:

- `./eng/build-local-images.ps1 integrations`
- `./eng/build-local-images.sh integrations`

Полная сборка:

- `./eng/build-local-images.ps1 all`
- `./eng/build-local-images.sh all`
