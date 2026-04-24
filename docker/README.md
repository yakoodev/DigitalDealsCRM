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
- Gateway API: `http://localhost:5068`
- Worker API: `http://localhost:5072`

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
