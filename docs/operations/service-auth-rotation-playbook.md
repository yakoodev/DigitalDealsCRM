# DDCRM — Service-Auth Rotation Playbook

## Назначение
Пошаговая процедура безопасной ротации service-auth токенов для `internal` и `worker` API.

## Источники истины
- `docs/standards/runtime-configuration.md`
- `docs/standards/openapi-governance.md`
- `.env.internal-api.example`
- `.env.worker-api.example`

## Инварианты
- токены `internal` и `worker` изолированы и не переиспользуются;
- ротация выполняется с overlap-периодом;
- клиентские токены и списки `ACCEPTED_TOKENS` синхронизируются поэтапно.

## Контуры и переменные
- internal контур:
  - сервер: `INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS`
  - клиент: `INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN`
- worker контур:
  - сервер: `WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS`
  - клиент: `WORKER_API_SERVICE_AUTH_CLIENT_TOKEN`
- запрещено копировать токен между этими контурами.

## 1. Подготовка
- сгенерировать новый токен для целевого контура (`internal` или `worker`);
- убедиться, что новый токен не используется в другом контуре;
- подготовить change window и rollback план.

## 2. Фаза overlap
- добавить новый токен в `*_ACCEPTED_TOKENS` целевого контура;
- задеплоить принимающую сторону;
- переключить `*_CLIENT_TOKEN` на новый токен у вызывающей стороны;
- подтвердить стабильность трафика и отсутствие auth-ошибок.

## 3. Завершение ротации
- удалить старый токен из `*_ACCEPTED_TOKENS`;
- задеплоить принимающую сторону повторно;
- проверить, что старый токен больше не принимается.

## 4. Проверки после ротации
- нет роста `401/403` на соответствующем контуре;
- нет роста `WORKER_AUTH_FAILED` для worker контура;
- contract/e2e проверки по auth-кейсам зелёные.
- выполнен dry-run `./eng/ops-ready.ps1` (или `./eng/ops-ready.sh`) и получен зелёный результат.

## 5. Rollback
- вернуть старый токен в `*_ACCEPTED_TOKENS`;
- временно вернуть `*_CLIENT_TOKEN` на старый токен;
- после стабилизации выполнить повторную ротацию через overlap-процедуру.

## 6. Минимальный rehearsal перед production
1. На staging добавить новый токен в `*_ACCEPTED_TOKENS`.
2. Переключить `*_CLIENT_TOKEN` на новый токен и убедиться, что cross-contour токены не пересекаются.
3. Выполнить `./eng/ops-ready.ps1 -SkipContracts` (или `./eng/ops-ready.sh --skip-contracts`).
4. Проверить, что соответствующий контур принимает новый токен, а старый токен удаляется только после стабильного окна.
