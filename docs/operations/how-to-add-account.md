# Как Добавить Аккаунт

## 1. Preconditions
- Вы вошли в UI по `email + password` (или через auth provider после его включения).
- Пользователь имеет право управлять аккаунтами проекта.
- Для платформы выдан активный integration grant `platform.<name>` (например `platform.funpay`).
- Для выбранной платформы существует активный worker template (если используете `accountTypeId`).

## 2. API путь

### 2.1 Создание аккаунта
- `POST /v1/projects/{projectId}/accounts`
- Заголовки:
  - `Authorization: Bearer <jwt>`
  - `Idempotency-Key: <uuid>`

Пример:

```bash
curl -X POST "http://localhost:5073/v1/projects/<projectId>/accounts" \
  -H "Authorization: Bearer <jwt>" \
  -H "Idempotency-Key: 6f3ad2f1-2bb8-4f89-b2b9-b95f2b9ae3f9" \
  -H "Content-Type: application/json" \
  -d '{
    "platform": "funpay",
    "accountTypeId": "funpay.main",
    "displayName": "FunPay Store #1",
    "proxyConfig": {
      "host": "45.88.208.237",
      "port": 1508,
      "login": "proxy-user",
      "password": "proxy-pass"
    },
    "marketplaceAuth": {
      "scheme": "golden_key",
      "credentials": {
        "golden_key": "<secret>",
        "user_agent": "Mozilla/5.0"
      }
    }
  }'
```

## 3. UI путь
- Откройте `/projects/{projectId}/accounts`.
- В форме создания укажите:
  - платформу;
  - отображаемое имя;
  - proxy данные;
  - при необходимости `accountTypeId`.
- Подтвердите создание.

## 4. Проверки и правила
- `accountTypeId` опционален.
- Если `accountTypeId` передан:
  - он должен существовать и быть `enabled=true`;
  - его `platform` должна совпадать с полем `platform` запроса.
- Без активного integration grant `platform.<name>` создание блокируется.
- Повтор запроса с тем же `Idempotency-Key` возвращает идемпотентный результат (без дубля аккаунта).
