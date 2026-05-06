# Полный Reset БД (Ручной Wipe)

Автоочистки и отдельного cleanup-скрипта в проекте нет.  
Для уже существующих данных используйте только ручной полный сброс томов.

## Важно
- Команда `docker compose down -v` удаляет volumes и все данные контуров.
- Используйте ее только для тестовых/стендовых сред.

## 1. DigitalDealsCRM
```powershell
cd F:\ddcrm\DigitalDealsCRM
docker compose down -v
docker compose up --build -d
```

## 2. DigitalDealsStats
```powershell
cd F:\ddcrm\DigitalDealsStats
docker compose down -v
docker compose up --build -d
```

## 3. DDCRM-Steam
```powershell
cd F:\ddcrm\DDCRM-Steam
docker compose down -v
docker compose up --build -d
```

## 4. Когда применять
- Перед прогоном product-readiness тестов на "чистом" окружении.
- После изменения seed/инициализации данных (например, после удаления предсозданных шаблонов).
