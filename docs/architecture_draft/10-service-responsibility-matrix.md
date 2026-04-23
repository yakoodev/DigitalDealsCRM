# Матрица ответственности сервисов

| Сервис | Основная роль | Что делает | С кем общается |
|---|---|---|---|
| Frontend | UI | Показывает данные, вызывает Core и Gateway | Core, Account API Gateway |
| Core | Бизнес-ядро | Пользователи, проекты, аккаунты, бизнес-логика | Frontend, IAM, Billing, Entitlement, Accounts Manager, Core DB |
| Core DB | Бизнес-хранилище | Хранит бизнес-данные и read-снапшоты billing/entitlement | Core, IAM, Billing, Entitlement |
| Identity & Access | Роли и доступы | Проверяет роли проекта и системных админов, поставляет данные для membership cache | Core, Core DB, Gateway |
| Billing | Коммерческий слой | Источник истины по платежам/подписке, webhook, возвраты, reconciliation | Core, Acquiring, Entitlement, Core DB |
| Entitlement Service | Ограничения и фичи | Источник истины по доступам, лимитам, блокировкам, trial/grace | Core, Billing, Gateway, Core DB |
| Accounts Manager | Lifecycle | Создаёт, изменяет, переносит и удаляет worker | Core, Accounts Manager DB, Gateway, Worker |
| Accounts Manager DB | Runtime metadata | Хранит данные серверов и размещения | Accounts Manager |
| Route Registry DB | Route metadata | Канонические `routeKey -> worker binding` | Account API Gateway |
| Account API Gateway | Проксирование и доступ | Проверяет токен, role и entitlement, маршрутизирует и проксирует | Frontend, Route Registry DB, Entitlement, IAM, Accounts Manager, Worker |
| Account Worker | Mini-backend аккаунта | Выполняет всю работу с площадкой | Gateway, Accounts Manager, Worker State Storage, Marketplace |
| Worker State Storage | Runtime-state | Хранит состояние worker и proxy-секреты | Account Worker |
