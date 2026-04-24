# DDCRM Docker Runtime

## Что поднимается

- `postgres` (единый кластер, отдельные БД под каждый контур)
- API-сервисы: `core`, `iam`, `route-registry`, `accounts-manager`, `gateway`, `billing`, `entitlement`, `worker`
- базовый UI: `ui` (`Next.js`)

## Запуск

```bash
docker compose up -d --build
```

## Полезные URL

- UI: `http://localhost:3000`
- Core API: `http://localhost:5073`
- Health Core API: `http://localhost:5073/health`
- Gateway API: `http://localhost:5068`
- Health Gateway API: `http://localhost:5068/health`
- Worker API: `http://localhost:5072`
- Health Worker API: `http://localhost:5072/health`

Остальные API health:
- IAM: `http://localhost:5120/health`
- Route Registry: `http://localhost:5110/health`
- Accounts Manager: `http://localhost:5137/health`
- Billing: `http://localhost:5122/health`
- Entitlement: `http://localhost:5221/health`

Важно: internal/worker health endpoint-ы защищены service-auth.
- Для `IAM/Route Registry/Accounts Manager/Billing/Entitlement` передавайте `X-Service-Token: internal-token-a`.
- Для `Worker` передавайте `X-Service-Token: worker-token-a`.

## Остановка

```bash
docker compose down
```

С полным удалением тома Postgres:

```bash
docker compose down -v
```

## Важно по секретам

- В `docker-compose.yml` оставлены только шаблонные локальные токены/ключи.
- Реальные секреты, JWT ключи и тестовые proxy credentials нужно передавать локально через override-файлы или env-переменные.
