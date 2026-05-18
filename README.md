# MedTracker API

Production-shaped gRPC backend на .NET 10 для трекинга приёма медикаментов, добавок и побочных эффектов. Чистая архитектура, multi-replica deploy в Kubernetes, distributed state в Redis, outbox-паттерн для надёжной отправки писем.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791)](https://www.postgresql.org/)
[![Redis](https://img.shields.io/badge/Redis-7-DC382D)](https://redis.io/)
[![Kubernetes](https://img.shields.io/badge/Kubernetes-kind-326CE5)](https://kubernetes.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## О проекте

MedTracker API — это backend для медицинского трекера, выросший из учебного pet-проекта в production-shaped сервис. Главная инженерная задача — построить систему, которая корректно работает при горизонтальном масштабировании: общий rate limit между репликами, единый keyring для шифрования, отдельный воркер для фоновых задач, гарантированная доставка писем при падении SMTP.

Этот репозиторий — не «hello world», а **рабочий пример того, как один gRPC-сервис деплоится в Kubernetes с правильным учётом stateful-аспектов**.

## Tech Stack

**Backend**
- .NET 10 / ASP.NET Core (gRPC)
- Entity Framework Core 10 + Npgsql
- FluentValidation, Serilog
- BCrypt, JWT (JwtBearer)
- MailKit (SMTP)
- Hangfire (background jobs)
- HybridCache (L1 in-memory + L2 Redis)

**Хранилища**
- PostgreSQL 16 (основные данные + Hangfire schema)
- Redis 7 (rate limit, DataProtection keys, cache, user status)
- pgbouncer (connection pooling: transaction + session modes)

**Инфраструктура**
- Docker Compose (локальная инфра)
- Kubernetes / kind (multi-replica deploy)
- Kustomize (overlays для dev/prod)
- ingress-nginx (gRPC-aware L7 балансировка)
- HorizontalPodAutoscaler (CPU-based)

**Архитектурные паттерны**
- Clean Architecture (Domain / Application / Infrastructure / Grpc)
- Outbox pattern для надёжной асинхронной отправки писем
- Distributed rate limiting через Redis sliding window
- CQRS-light через repository pattern

## Архитектура

```
                            ┌──────────────────────┐
                            │   gRPC clients       │
                            │   (mobile, web, ...) │
                            └──────────┬───────────┘
                                       │ HTTP/2
                            ┌──────────▼───────────┐
                            │   ingress-nginx      │
                            │   (gRPC L7 LB)       │
                            └──────────┬───────────┘
                                       │
                  ┌────────────────────┼────────────────────┐
                  ▼                    ▼                    ▼
            ┌──────────┐         ┌──────────┐         ┌──────────┐
            │  API #1  │         │  API #2  │         │  API #3  │
            │ (HPA-    │         │          │         │          │
            │  managed)│         │          │         │          │
            └────┬─────┘         └─────┬────┘         └────┬─────┘
                 │                     │                   │
                 └─────────┬───────────┴───────────┬───────┘
                           │                       │
                  ┌────────▼─────────┐    ┌────────▼────────┐
                  │      Redis       │    │   pgbouncer     │
                  │  (rate limit,    │    │  (transaction   │
                  │   cache, keys)   │    │   pool)         │
                  └──────────────────┘    └────────┬────────┘
                                                   │
                                          ┌────────▼────────┐
                                          │   PostgreSQL    │
                                          │   (data +       │
                                          │    Hangfire)    │
                                          └────────▲────────┘
                                                   │
                                          ┌────────┴────────┐
                                          │    Worker       │
                                          │  (Hangfire:     │
                                          │   outbox,       │
                                          │   cleanup)      │
                                          └────────┬────────┘
                                                   │ SMTP
                                          ┌────────▼────────┐
                                          │  Mail provider  │
                                          │  (Mailtrap/...) │
                                          └─────────────────┘
```

**Ключевые решения:**

| Проблема | Решение |
|----------|---------|
| Rate limit между репликами | Redis sliding window через atomic Lua script |
| Шифрование cookies/токенов при rolling restart | DataProtection keys в Redis, общий keyring |
| Hangfire-воркеры × N реплик = нагрузка на БД | Разделение API (без worker) и отдельный Worker-pod |
| 100 connection slots Postgres переполняются | pgbouncer с двумя пулами: transaction + session |
| Hangfire не работает в transaction mode | Отдельная DB `medtracker_admin` в session mode |
| Гарантия доставки писем при падении SMTP | Outbox pattern: сохраняем в БД, шлём из Hangfire с retry |
| Реальный client IP теряется за ingress | Парсинг `X-Real-IP` / `X-Forwarded-For` из заголовков |

## Быстрый старт

### Требования

- Docker Desktop (Mac / Linux / Windows)
- .NET 10 SDK
- `kind`, `kubectl` (для K8s-варианта): `brew install kind kubectl`

### Kubernetes через kind

```bash
# 1. Поднять инфраструктуру (Postgres, Redis, pgbouncer) в compose
docker compose up -d medtracker-db medtracker-redis medtracker-pgbouncer

# 2. Поднять K8s-кластер с API и Worker
./scripts/k8s-up.sh

# 3. Проверка
grpcurl -plaintext localhost:5001 list
kubectl get pods,hpa -n medtracker
```

Скрипт автоматически:
- создаёт kind-кластер с правильными port mappings
- ставит ingress-nginx и metrics-server
- собирает Docker-образ и грузит в kind
- генерирует Secret-патч из вашего `.env`
- прогоняет EF Core миграции
- применяет манифесты через Kustomize

Снести: `./scripts/k8s-down.sh`

## Структура проекта

```
MedTracker/
├── MedTracker.Domain/          # Entities, value objects, domain exceptions
├── MedTracker.Application/     # Use cases, DTOs, validators, service interfaces
├── MedTracker.Infrastructure/  # EF Core, repositories, external services, Hangfire jobs
├── MedTracker.Grpc/            # gRPC services, interceptors, .proto files, Program.cs
├── MedTracker.Tests/           # Unit tests
├── MedTracker.IntegrationTests/# Integration tests
│
├── compose.yaml                # Docker Compose (Postgres + Redis + pgbouncer + API + nginx)
├── pgbouncer.ini               # Connection pooler config
├── nginx.conf                  # LB для compose-варианта
│
├── k8s/                        # Kubernetes manifests
│   ├── kind-config.yaml
│   ├── base/                   # базовые ресурсы
│   └── overlays/dev/           # dev-overlay с patch для секретов
│
└── scripts/
    ├── k8s-up.sh               # one-command bootstrap K8s
    ├── k8s-down.sh             # teardown
    └── load-test.sh            # нагрузочное тестирование (для HPA)
```

## Разработка

```
### Тестирование HPA

```bash
# В одном терминале — наблюдение
watch -n 2 'kubectl get hpa,pods -n medtracker'

# В другом — нагрузка
./scripts/load-test.sh 30 localhost:5001 180
```

Через ~60 секунд после старта load-test увидите, как HPA добавляет реплики при росте CPU выше 70%.

## Что внутри: интересные технические детали

**Distributed rate limiter.** Per-client sliding window в Redis через atomic Lua script. Извлекает реальный IP клиента из `X-Real-IP` / `X-Forwarded-For`, не теряет точность за ingress. См. `MedTracker.Grpc/Interceptors/RateLimitInterceptor.cs`.

**Outbox pattern.** Все письма сохраняются в `OutboxMessages` в одной транзакции с бизнес-данными. Hangfire-job каждую минуту забирает batch'ем (с advisory lock через `LockToken`/`LockedUntil`), отправляет через SMTP, помечает `ProcessedAt`. Гарантирует at-least-once-доставку, не теряет письма при перезапуске. См. `MedTracker.Infrastructure/Services/OutboxJob.cs`.

**Hangfire mode-switch.** Один и тот же Docker-образ работает как API (`Hangfire__RunServer=false`) или как Worker (`RunServer=true`, `RegisterRecurringJobs=true`). В K8s API-pods не запускают worker-потоки, Worker-pod — единственная реплика, держит все 4 worker'а. Снижает нагрузку на БД, упрощает scaling. См. `MedTracker.Infrastructure/Services/HangfireOptions.cs`.

**pgbouncer с двумя пулами.** В transaction mode (`pool_mode=transaction`) переиспользует серверные соединения между транзакциями разных клиентов — это работает для EF Core CRUD. Hangfire требует session mode (использует LISTEN/NOTIFY и advisory locks), поэтому отдельная база `medtracker_admin` в session mode. Один pgbouncer-контейнер, два пула. См. `pgbouncer.ini`.

**Design-time DbContext.** `AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>` позволяет EF Core CLI работать без полного запуска приложения. Это решает проблему миграций в multi-replica деплое: миграции применяются отдельным шагом до подъёма подов, не через auto-migrate в `Program.cs` (где была бы race между репликами).
## Лицензия

## Автор

<!-- TODO: добавить свои ссылки
- GitHub: [@username](https://github.com/username)
- LinkedIn: [Your Name](https://linkedin.com/in/yourname)
- Email: you@example.com
-->
