# Mise

> *mise en place* /miz ɑ̃ plas/ · фр. «всё на своих местах»

Мониторинг остатков, сроков годности и стоп-листов ресторана. Хакатон-кейс «Флагман».

Поставки по партиям → остатки по партиям → заказы со списанием по **FEFO** → авто-стоп-лист → сигнализация сроков и автосписание → **горячие предложения** с объяснением → аналитика, прогноз, предложения поставок → real-time UI.

> Статус: **Фаза 7 — безопасность: вход, роли, аудит, лимиты; prod-конфигурация.** Дальше — фронтенд. Стек поднимается одной командой; бизнес-функции появляются по фазам из `PLAN.md` §12.

## Быстрый старт (Docker)

Нужен Docker Desktop / Docker Engine с Compose v2.

```bash
cp .env.example .env        # при необходимости поменяйте порты/пароли
docker compose up -d --build
```

| Что | Адрес |
|---|---|
| Веб-приложение | http://localhost:8081 |
| API | http://localhost:5080/api/v1 |
| Health | http://localhost:5080/health |
| Swagger | http://localhost:5080/swagger |
| Grafana | http://localhost:3001 (просмотр без входа; админ — `GRAFANA_ADMIN_*` из `.env`) |

Postgres и Redis публикуются только на `127.0.0.1` (порты `5432` и `6380`; Redis на `6380`, чтобы не конфликтовать с локальным Redis). Порты меняются в `.env`.

Остановить: `docker compose down` (данные сохраняются в volume; `-v` — удалить и их).

### Демо-данные за две недели

Чистая база + история демо-ресторана за 14 дней (поставки, ~1 500 заказов, отмены, отказы и замены, списания, стоп-лист) —
через настоящие сервисы на «ускоренных» часах, поэтому всё согласовано: остатки, FEFO, деньги, прогнозы. Детерминировано (`--seed`).

```bash
docker compose down && docker volume rm mise_pgdata   # стирает все данные демо-стека
docker compose up -d postgres redis
docker compose run --rm --no-deps api --seed-demo-history --days 14
docker compose up -d
```

### Рестораны, регистрация и приглашения

Каждый ресторан — отдельная организация: свои продукты, склад, заказы и сотрудники. На экране входа — «Новый ресторан»
(регистрирующийся становится менеджером; можно сразу заполнить справочник примером) и «По коду» (приглашение менеджера,
`Пользователи → Приглашения`, ссылка `/join/КОД`). Демо-учётки ниже живут в демо-ресторане «Mise · демо» с симулятором.

### Вход и роли

Всё API, кроме `/health`, `/api/v1/ping` и `/api/v1/auth/*`, требует входа (`Authorization: Bearer <accessToken>`).

| Логин | Роль | Пароль |
|---|---|---|
| `manager` | Manager — всё: справочники, пользователи, аналитика, аудит, симулятор | `DEMO_PASSWORD` (по умолчанию `mise-demo`) |
| `chef` | Chef — заказы, KPI дашборда, подтверждение алертов | то же |
| `warehouse` | Warehouse — поставки, списания, предложения поставок | то же |
| `simulator` | Simulator — служебная: поставки и заказы, без лимитов | `SIMULATOR_PASSWORD` |

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/auth/login -H "Content-Type: application/json" \
  -d '{"login":"manager","password":"mise-demo"}' | python -c "import json,sys; print(json.load(sys.stdin)['accessToken'])")
curl -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/v1/stock
```

Access-токен живёт 15 мин; `POST /auth/refresh` выдаёт новый по httpOnly-cookie (ротация). Подробно — `docs/assumptions.md`, H1–H13.

### Prod-запуск

```bash
cp .env.example .env    # заменить все секреты: AUTH_SIGNING_KEY (openssl rand -base64 48), DEMO_PASSWORD, SIMULATOR_PASSWORD,
                        # POSTGRES_PASSWORD, DB_READONLY_PASSWORD, GRAFANA_ADMIN_PASSWORD
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

Наружу — только web (`:8081`: SPA + `/api`, `/hubs`, `/health` через nginx) и Grafana (`:3001`). Без секретов compose не стартует.

## Разработка

Требования: .NET SDK 9.0.3xx, Node.js ≥ 22 (рекомендуется 24), Docker.

```bash
# инфраструктура для локального запуска
docker compose up -d postgres redis

# backend (http://localhost:5080, миграции применяются автоматически в Development)
dotnet run --project src/Mise.Api

# frontend (http://localhost:5173, прокси /api и /health → :5080)
cd web && npm i && npm run dev
```

### Проверки

```bash
dotnet build && dotnet test                               # интеграционные тесты требуют запущенный Docker
cd web && npm run lint && npm run typecheck && npm test && npm run build
```

### Миграции

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Mise.Infrastructure -s src/Mise.Api -o Persistence/Migrations
```

## API (`/api/v1`, подробно — в Swagger)

| Метод | Путь | Назначение |
|---|---|---|
| `GET/POST` | `/products` | Словарь продуктов (единица, срок по умолчанию, мин. остаток, кратность) |
| `GET/PUT/DELETE` | `/products/{id}` | Продукт; удаление/смена единицы используемого продукта → 409 |
| `GET/POST` | `/dishes` | Блюда с составом |
| `GET/PUT/DELETE` | `/dishes/{id}`, `PUT /dishes/{id}/ingredients` | Блюдо, замена состава |
| `POST` | `/supplies` | Приём поставки (`Idempotency-Key`); каждая позиция → партия со своим `expiresAt` |
| `GET` | `/supplies`, `/supplies/{id}` | Список (пагинация) и детали поставки с партиями |
| `GET` | `/stock` | Остатки по продуктам: доступно (без просроченного), всего, ближайший срок, статус |
| `GET` | `/stock/{productId}/batches` | Партии продукта в порядке FEFO |
| `GET` | `/batches?status=&expiringWithinHours=&productId=` | Лента партий по сроку |
| `POST` | `/batches/{id}/write-off` | Ручное списание (причина обязательна; без `quantity` — весь остаток) |
| `POST` | `/orders` | Заказ (`Idempotency-Key`): суммарная проверка наличия, списание по FEFO; нехватка → 422 с `missing[]` |
| `GET` | `/orders`, `/orders/{id}` | Список (фильтр `status`) и заказ с разбивкой списания по партиям |
| `POST` | `/orders/{id}/cancel` | Отмена с возвратом в те же непросроченные партии |
| `PATCH` | `/orders/{id}/status` | `Created → Cooking → Done` |
| `GET` | `/menu/availability` | Все блюда: `maxPortions`, ограничивающий продукт, статус |
| `GET` | `/stoplist`, `/stoplist/history` | Стоп-лист (нехватка / вручную) и история интервалов |
| `GET` | `/stoplist/forecast?horizonHours=24` | Какие блюда и когда уйдут в стоп: ETA, ограничивающий продукт, причина, уверенность ([алгоритм](docs/forecast.md)) |
| `GET` | `/supply-suggestions?horizonHours=72` | Что и сколько заказать: партия кратно упаковке, срочность, причины |
| `GET` | `/analytics/writeoffs`, `/deficit`, `/consumption`, `/kpi` | Списания, дефицитные ингредиенты, расход, KPI и плитки дашборда (`from`/`to`, по умолчанию 7 дней) |
| `GET` | `/alerts?type=&severity=&acknowledged=&active=&productId=&batchId=&dishId=` | Лента алертов: скоро истечёт, истёк, мало остатка, стоп-лист вкл/выкл |
| `GET` | `/alerts/summary` | Сколько алертов требуют внимания (по важности) |
| `POST` | `/alerts/{id}/ack` | Подтвердить алерт |
| `POST` | `/auth/login`, `/auth/refresh`, `/auth/logout`; `GET /auth/me` | Вход (access в ответе, refresh — httpOnly-cookie), ротация, выход |
| `GET/POST/PUT` | `/users`, `/users/{id}`, `POST /users/{id}/password` | Пользователи и роли (Manager) |
| `GET` | `/audit?action=&userId=&entity=&from=&to=` | Журнал аудита (Manager) |
| `GET` | `/hot-offers?limit=&scorer=` | Что продавать в первую очередь: score 0–100, `reasons[]` и объяснение ([алгоритм](docs/hot-offers.md)) |
| `GET` | `/simulator/state` | Желаемое и фактическое состояние симулятора, счётчики, последние события |
| `POST` | `/simulator/start`, `/stop`, `/scenario`, `/speed` | Управление: сценарии `Normal`, `RushHour`, `SupplyDelay`, `Spoilage`, `FreshDelivery`, `Chaos`; скорость ×1…×60 |
| `POST` | `/demo/seed-stock` | Демо-наполнение склада (при `DEMO_ENABLED=true`) |

Фоновый `ExpiryWorker` раз в 30 с (`EXPIRY_INTERVAL_SECONDS`): поднимает алерты при пересечении порогов срока (Warning ≤ 50% и ≤ 24 ч, Critical ≤ 20% и ≤ 6 ч), переводит истёкшие партии в `Expired` и списывает остаток (`EXPIRY_AUTO_WRITE_OFF=true`), ведёт LowStock и пересчитывает стоп-лист. При нескольких инстансах тик выполняет один (advisory lock).

### Real-time и симулятор

SignalR-хаб `/hubs/stock` (через nginx — тот же origin, что и веб): события `StockChanged`, `AvailabilityChanged`, `StopListChanged`, `AlertRaised`, `BatchStatusChanged`, `OrderCreated`, `OrderStatusChanged`, `SupplyReceived`, `HotOffersChanged`, `SimulatorStateChanged`. Только дельты, склеенные за 250 мс; Redis backplane для нескольких инстансов API.

Симулятор (`simulator` в compose) — отдельный процесс, работает с API по REST как внешняя касса и склад. Стартует остановленным:

```bash
curl -X POST http://localhost:5080/api/v1/demo/seed-stock
curl -X POST http://localhost:5080/api/v1/simulator/start -H "Content-Type: application/json" -d '{"scenario":"RushHour","speed":60}'
curl http://localhost:5080/api/v1/simulator/state
curl -X POST http://localhost:5080/api/v1/simulator/stop
```

Срок годности партии: явная дата в позиции → `createdAt` поставки + `shelfLifeHours` позиции → срок по умолчанию из словаря → иначе 400. Ошибки — RFC 7807 (`400` валидация с `errors`, `404`, `409` с `code`, `422`).

Пример:

```bash
curl -X POST http://localhost:5080/api/v1/supplies \
  -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"number":"SUP-2026-000123","supplier":"ООО Свежесть","items":[
        {"productId":"10000000-0000-0000-0000-000000000001","quantity":5,"unit":"Liter","shelfLifeHours":168},
        {"productId":"10000000-0000-0000-0000-000000000009","quantity":3000,"expiresAt":"2026-10-15T08:30:00Z"}]}'
```

## Стек

- **Backend:** C# / ASP.NET Core (.NET 9), EF Core + Npgsql, SignalR (Redis backplane), JWT + роли, rate limiting, Serilog, Swagger, ProblemDetails (RFC 7807)
- **Хранилища:** PostgreSQL 16, Redis 7
- **Frontend:** React 19 + TypeScript + Vite + Chakra UI v3 + SCSS-модули, TanStack Query, React Router
- **Тесты:** xUnit + Testcontainers, Vitest + Testing Library
- **Инфра:** Docker Compose, GitHub Actions

## Структура

```
src/
  Mise.Domain/          доменная модель и логика, без зависимостей (IClock, позже FEFO/expiry/scoring)
  Mise.Application/     use cases, DTO, валидация
  Mise.Infrastructure/  EF Core, миграции, Redis, часы (SystemClock/FakeClock)
  Mise.Api/             HTTP API, health, Swagger, middleware
  Mise.Simulator/       симулятор ресторана: заказы и поставки через REST, управление через Redis
tests/                  Domain / Application / Simulator / Api.IntegrationTests (Testcontainers)
web/                    React-клиент
deploy/grafana/         provisioning Grafana: источник PostgreSQL и дашборды
docs/                   допущения, идеи, алгоритмы (hot offers, прогноз)
```

## Документы

- `docs/hot-offers.md` — алгоритм горячих предложений: формула, параметры, разобранный пример
- `docs/forecast.md` — расход (EWMA), прогноз стоп-листа, предложения поставок
