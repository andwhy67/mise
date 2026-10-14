# Mise

> *mise en place* /miz ɑ̃ plas/ · фр. «всё на своих местах»

Мониторинг остатков, сроков годности и стоп-листов ресторана. Хакатон-кейс «Флагман».

Поставки по партиям → остатки по партиям → заказы со списанием по **FEFO** → авто-стоп-лист → сигнализация сроков и автосписание → **горячие предложения** с объяснением → аналитика, прогноз, предложения поставок → real-time UI.

> Статус: **Фаза 3 — мониторинг сроков, алерты, автосписание.** Стек поднимается одной командой; бизнес-функции появляются по фазам из `PLAN.md` §12.

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

Postgres и Redis публикуются только на `127.0.0.1` (порты `5432` и `6380`; Redis на `6380`, чтобы не конфликтовать с локальным Redis). Порты меняются в `.env`.

Остановить: `docker compose down` (данные сохраняются в volume; `-v` — удалить и их).

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
| `GET` | `/alerts?type=&severity=&acknowledged=&active=&productId=&batchId=&dishId=` | Лента алертов: скоро истечёт, истёк, мало остатка, стоп-лист вкл/выкл |
| `GET` | `/alerts/summary` | Сколько алертов требуют внимания (по важности) |
| `POST` | `/alerts/{id}/ack` | Подтвердить алерт |

Фоновый `ExpiryWorker` раз в 30 с (`EXPIRY_INTERVAL_SECONDS`): поднимает алерты при пересечении порогов срока (Warning ≤ 50% и ≤ 24 ч, Critical ≤ 20% и ≤ 6 ч), переводит истёкшие партии в `Expired` и списывает остаток (`EXPIRY_AUTO_WRITE_OFF=true`), ведёт LowStock и пересчитывает стоп-лист. При нескольких инстансах тик выполняет один (advisory lock).

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

- **Backend:** C# / ASP.NET Core (.NET 9), EF Core + Npgsql, Serilog, Swagger, ProblemDetails (RFC 7807)
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
  Mise.Simulator/       генератор поставок и заказов (фаза 4)
tests/                  Domain / Application / Api.IntegrationTests (Testcontainers)
web/                    React-клиент
docs/                   допущения, идеи, (позже) алгоритмы и демо-сценарий
```

## Документы

- `PLAN.md` — полный план, алгоритмы, API, дорожная карта
- `docs/assumptions.md` — принятые допущения
- `docs/ideas.md` — идеи сверх ТЗ
