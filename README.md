# Mise

> *mise en place* /miz ɑ̃ plas/ · фр. «всё на своих местах»

Мониторинг остатков, сроков годности и стоп-листов ресторана. Хакатон-кейс «Флагман».

Поставки по партиям → остатки по партиям → заказы со списанием по **FEFO** → авто-стоп-лист → сигнализация сроков и автосписание → **горячие предложения** с объяснением → аналитика, прогноз, предложения поставок → real-time UI.

> Статус: **Фаза 0 — фундамент.** Стек поднимается одной командой; бизнес-функции появляются по фазам из `PLAN.md` §12.

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
