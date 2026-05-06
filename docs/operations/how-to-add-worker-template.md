# Как Добавить Worker Template

## 1. Preconditions
- Вы вошли в UI по `email + password` (или другой auth provider после его внедрения).
- У текущего пользователя в сессии есть системное право `system.accountManager.manage`.
- AccountsManager API доступен (`/health` отвечает).

## 2. API путь

### 2.1 Список шаблонов
- `GET /v1/admin/account-manager/account-types`

### 2.2 Upsert шаблона
- `PUT /v1/admin/account-manager/account-types/{accountTypeId}`
- Заголовки:
  - `Authorization: Bearer <jwt>`
  - `Idempotency-Key: <uuid>`

Пример:

```bash
curl -X PUT "http://localhost:5073/v1/admin/account-manager/account-types/funpay.main" \
  -H "Authorization: Bearer <jwt>" \
  -H "Idempotency-Key: 5a7d76e7-2a2f-47d0-8f72-9d2952ca9f02" \
  -H "Content-Type: application/json" \
  -d '{
    "platform": "funpay",
    "displayName": "FunPay Worker Main",
    "description": "Primary runtime template for FunPay accounts",
    "workerProfileId": "funpay-worker",
    "enabled": true,
    "sortOrder": 10,
    "formFields": [
      {
        "key": "displayName",
        "label": "Название аккаунта",
        "inputType": "text",
        "required": true,
        "secret": false,
        "placeholder": "FunPay account"
      }
    ],
    "runtime": {
      "autospawnEnabled": true,
      "workerImage": "ddcrm/funpay-worker:local",
      "workerPathPrefix": "/internal/v2/worker",
      "healthPath": "/health",
      "containerPort": 8080,
      "environmentVariables": {
        "FUNPAY_WORKER_PROVIDER": "funpay"
      },
      "workerCommand": ["python", "-m", "ddcrm_funpay_worker.main"]
    }
  }'
```

### 2.3 Пример для `steam-integration` (DDCRM-Steam)

```bash
curl -X PUT "http://localhost:5073/v1/admin/account-manager/account-types/steam.integration" \
  -H "Authorization: Bearer <jwt>" \
  -H "Idempotency-Key: 35d2f5d8-3cda-4e35-8d38-b5e04d913e2c" \
  -H "Content-Type: application/json" \
  -d '{
    "platform": "steam-integration",
    "displayName": "Steam Integration Runtime",
    "description": "DDCRM-Steam worker runtime for ext.integration.steam.* actions",
    "workerProfileId": "steam-integration-worker",
    "enabled": true,
    "sortOrder": 20,
    "formFields": [
      {
        "key": "displayName",
        "label": "Название runtime",
        "inputType": "text",
        "required": true,
        "secret": false,
        "placeholder": "Steam integration runtime"
      }
    ],
    "runtime": {
      "autospawnEnabled": true,
      "workerImage": "ddcrm/steam-worker:local",
      "workerPathPrefix": "/internal/v2/worker",
      "healthPath": "/health",
      "containerPort": 8080,
      "environmentVariables": {
        "WORKER_API_SERVICE_AUTH_ENABLED": "true",
        "WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS": "worker-token-a,worker-token-b",
        "INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS": "internal-token-a,internal-token-b",
        "SECRETS_MASTER_KEY_B64": "replace-with-real-base64-key"
      },
      "workerCommand": null
    }
  }'
```

## 3. UI путь
- Откройте: `/admin/account-manager/templates`.
- Заполните:
  - `Account type ID`
  - `Platform`
  - `Display name`
  - `Worker image`
  - `Worker command`
  - `Path prefix`/`Health path`
- Нажмите `Сохранить`.

## 4. Важные ограничения
- После чистого старта шаблонов нет, это нормально.
- Для одной платформы разрешен только один активный template (`enabled=true`), иначе вернется конфликт.
- Используйте локальные теги образов, которые собираются через `eng/build-local-images.*`.
