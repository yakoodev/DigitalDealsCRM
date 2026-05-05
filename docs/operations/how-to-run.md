# Как Запустить DDCRM Локально

## 1. Подготовка
- Установите Docker + Docker Compose.
- Проверьте, что рядом с репозиторием есть соседние директории:
  - `F:\ddcrm\DDCRM-FunPay`
  - `F:\ddcrm\DDCRM-Playerok`
  - `F:\ddcrm\DigitalDealsStats`
  - `F:\ddcrm\SteamFleetControl`

## 2. Локальная сборка образов
Из `F:\ddcrm\DigitalDealsCRM`:

```powershell
./eng/build-local-images.ps1 workers
./eng/build-local-images.ps1 integrations
```

Linux/macOS:

```bash
./eng/build-local-images.sh workers
./eng/build-local-images.sh integrations
```

`workers` собирает:
- `ddcrm/worker-api:local`
- `ddcrm/funpay-worker:local`
- `ddcrm/playerok-worker:local`

`integrations` собирает:
- `ddcrm/marketstat:local`
- `ddcrm/steamfleet-web:local`
- `ddcrm/steamfleet-worker:local`

## 3. Запуск основного контура DDCRM
Из `F:\ddcrm\DigitalDealsCRM`:

```powershell
docker compose up --build -d
```

## 4. Health-check
Проверьте, что сервисы отвечают:

```powershell
curl http://localhost:5073/health
curl http://localhost:5137/health -H "X-Service-Token: internal-token-a"
curl http://localhost:5072/health -H "X-Service-Token: worker-token-a"
```

UI доступен на `http://localhost:3800`.

## 5. Что изменилось по умолчанию
- Demo/manual JWT-вход удален: в UI используется только login/register по `email + password`.
- Супер-админ bootstrap-ится из env:
  - `EXTERNAL_API_SUPER_ADMIN_EMAIL`
  - `EXTERNAL_API_SUPER_ADMIN_PASSWORD`
  - `EXTERNAL_API_SUPER_ADMIN_DISPLAY_NAME` (optional)
- При первом входе супер-админ обязан сменить пароль (до этого бизнес endpoint-ы заблокированы).
- На чистом старте каталог platform templates пустой.
  - `/v1/admin/account-manager/account-types` и внутренний `/internal/v1/account-types` вернут пустой список, пока админ явно не создаст template.

## 6. Полный ручной reset БД
- Автоочистки и cleanup-скриптов нет.
- Для полного сброса используйте отдельный гайд: [db-reset.md](./db-reset.md).
