# Матрица ответственности сервисов

| Сервис | Основная роль | Что делает | С кем общается |
|---|---|---|---|
| Frontend | UI | Показывает данные, вызывает Core и Gateway | Core, Account API Gateway |
| Core | Бизнес-ядро | Пользователи, проекты, аккаунты, бизнес-логика | Frontend, IAM, Billing, Entitlement, Accounts Manager, Core DB |
| Core DB | Бизнес-хранилище | Хранит бизнес-данные, billing и entitlement snapshot | Core, Gateway, IAM, Billing, Entitlement |
| Identity & Access | Роли и доступы | Проверяет роли проекта и системных админов | Core, Core DB |
| Billing | Коммерческий слой | Платежи, тарифы, add-on, подписки, возвраты | Core, Acquiring, Entitlement, Core DB |
| Entitlement Service | Ограничения и фичи | Доступные платформы, лимиты, блокировки, trial | Core, Billing, Gateway, Core DB |
| Accounts Manager | Lifecycle | Создаёт, изменяет, переносит и удаляет worker | Core, Accounts Manager DB, Gateway, Worker |
| Accounts Manager DB | Runtime metadata | Хранит данные серверов и размещения | Accounts Manager |
| Account API Gateway | Проксирование и доступ | Проверяет доступ, entitlement, маршрутизирует, проксирует | Frontend, Core DB, Entitlement, Accounts Manager, Worker |
| Account Worker | Mini-backend аккаунта | Выполняет всю работу с площадкой | Gateway, Accounts Manager, Worker State Storage, Marketplace |
| Worker State Storage | Runtime-state | Хранит состояние worker | Account Worker |
