# MedTracker — Документация

gRPC-микросервис для учёта приёма лекарственных средств по гинекологическим диагнозам (ПМР, СПКЯ, Эндометриоз, Менопауза).

---

## Содержание

1. [Быстрый старт](#быстрый-старт)
2. [Архитектура](#архитектура)
3. [Схема базы данных](#схема-базы-данных)
4. [API Reference](#api-reference)
5. [Аутентификация и безопасность](#аутентификация-и-безопасность)
6. [Гайд для фронтенда](#гайд-для-фронтенда)
7. [Импорт справочных данных](#импорт-справочных-данных)
8. [Коды ошибок](#коды-ошибок)
9. [Переменные окружения](#переменные-окружения)
10. [Troubleshooting](#troubleshooting)

---

## Быстрый старт

### Предварительные требования
- Docker Desktop
- .NET 10 SDK (для локальной разработки)
- PostgreSQL 16 (опционально — есть в Docker)

### Запуск через Docker

```bash
# 1. Создай .env из шаблона
cp .env.example .env

# 2. Сгенерируй JWT-секрет (минимум 32 символа)
openssl rand -base64 48
# Вставь результат в .env как JWT_SECRET=...

##КИНУ ГОТОВЫЙ .ENV  в личку

# 3. Запусти
docker-compose up --build
```

Сервис доступен:
- `http://localhost:5001` — HTTP/2 (gRPC)
- `https://localhost:5002` — HTTPS (self-signed dev cert)
- PostgreSQL: `localhost:5434`

### Локальная разработка

```bash
# 1. Настрой connection string в appsettings.Development.json
# 2. Создай базу
createdb medtracker

# 3. Примени миграции
cd MedTracker.Grpc
dotnet ef database update \
  --project ../MedTracker.Infrastructure \
  --startup-project .

# 4. Запусти
dotnet run
```

---

## Архитектура

### Clean Architecture — слои

```
┌─────────────────────────────────────────────────────┐
│  MedTracker.Grpc                                    │
│  - gRPC service implementations                     │
│  - Interceptors (Auth, Exception, RateLimit)        │
│  - Program.cs (startup, Kestrel, DI)                │
└──────────────────┬──────────────────────────────────┘
                   │ depends on
                   ▼
┌─────────────────────────────────────────────────────┐
│  MedTracker.Infrastructure                          │
│  - AppDbContext, EF configurations                  │
│  - Repositories                                     │
│  - JwtService, ExcelImportService, PasswordHasher   │
└──────────────────┬──────────────────────────────────┘
                   │ depends on
                   ▼
┌─────────────────────────────────────────────────────┐
│  MedTracker.Application                             │
│  - Interfaces (IServices, IRepositories)            │
│  - Services (бизнес-логика)                         │
│  - DTOs, Validators                                 │
└──────────────────┬──────────────────────────────────┘
                   │ depends on
                   ▼
┌─────────────────────────────────────────────────────┐
│  MedTracker.Domain                                  │
│  - Entities                                         │
│  - Enums                                            │
│  - Domain Exceptions                                │
└─────────────────────────────────────────────────────┘
```

**Правило зависимостей:** внутренние слои ничего не знают о внешних. Domain — чистый C#, без зависимостей. Application знает только Domain. Infrastructure реализует интерфейсы Application. Grpc — композиционный слой.

### Технический стек

| Категория | Технология |
|---|---|
| Runtime | .NET 10, C# 13 |
| Транспорт | gRPC (Protobuf v3) |
| БД | PostgreSQL 16 + EF Core 10 (Code-First) |
| Аутентификация | JWT Bearer + Refresh tokens в БД |
| Хеширование паролей | BCrypt |
| Логирование | Serilog (console + файл) |
| Валидация | FluentValidation |
| Парсинг Excel | EPPlus |
| Контейнеризация | Docker + docker-compose |

### Ключевые решения

- **gRPC вместо REST** — строгая типизация, code-gen клиентов, эффективность
- **Refresh-токены в БД, не JWT** — возможность отзыва, rotation chain с replay detection
- **Soft delete** через `IsDeleted` + query filters — для пользовательских данных (UserMedication, UserSupplement, логи, cycle entries)
- **jsonb для Symptoms** в `MenstrualCycleEntry` — избегаем отдельной таблицы для списка строк
- **Транзакционный импорт Excel** — всё или ничего; rollback при любой ошибке
- **Client streaming** для загрузки файлов — позволяет слать большие файлы чанками

---

## Схема базы данных

### Основные сущности

**User** — пользователь системы
- `Id` (Guid, PK)
- `Login` (unique, string) — логин
- `PasswordHash` — BCrypt hash
- `FullName`, `Age`
- `Role` (User / Admin)
- `FailedLoginAttempts`, `LockoutUntil` — защита от брутфорса
- `CreatedAt`, `UpdatedAt`

**Diagnosis** — справочник диагнозов (4 записи в seed)
- ПМР, СПКЯ, Эндометриоз, Менопауза

**UserDiagnosis** — M2M связь User ↔ Diagnosis (пользователь может иметь несколько диагнозов)

**Medication** — справочник ЛС
- Связан с `Diagnosis`
- `HormonalGroup`, `INN`, `TradeName`, `Dosage`, `Form`, `Frequency`, `Diet`

**Supplement** — БАДы, зависящие от Medication

**SideEffect** — побочные эффекты, зависящие от Medication

**UserMedication** — ЛС, назначенные пользователю
- `StartDate`, `EndDate`, `IsActive`
- Soft deletable

**UserSupplement** — аналогично для БАДов

**UserSideEffectLog** — журнал побочек
- `Date`, `Intensity` (Low/Medium/High/Severe), `Comment`

**ExternalMedication** — сторонние ЛС (не из справочника)
- `Name`, `Dosage`, `Date`, `Comment`

**MenstrualCycleEntry** — записи о цикле
- `StartDate`, `EndDate` (nullable — цикл может быть в процессе)
- `Intensity` (Light/Moderate/Heavy/VeryHeavy)
- `Symptoms` — массив строк в `jsonb`
- `Notes`

**RefreshToken** — хранилище refresh-токенов
- `Token`, `ExpiresAt`, `IsRevoked`, `RevokedAt`
- `ReplacedByTokenId` — цепочка ротации для replay detection

**ImportRecord** — история административных импортов
- `FileName`, `DiagnosisName`, `RecordsImported`, `ImportedAt`, `ImportedBy`

### Связи

```
User ──┬─< UserDiagnosis >── Diagnosis
       ├─< UserMedication >── Medication ──< Supplement
       ├─< UserSupplement ───── Supplement     └─< SideEffect
       ├─< UserSideEffectLog ── SideEffect
       ├─< ExternalMedication
       ├─< MenstrualCycleEntry
       └─< RefreshToken
```

---

## API Reference

Все методы доступны по адресу `localhost:5001` (HTTP/2) или `localhost:5002` (HTTPS).

**Authorization:** JWT в gRPC metadata:
```
authorization: Bearer <access_token>
```

Публичные методы (без auth): `AuthService/Register`, `AuthService/Login`, `AuthService/RefreshToken`.
Методы с ролью Admin: все в `AdminService`.

---

### AuthService

#### `Register`

Регистрация нового пользователя.

**Request:**
```json
{
  "login": "maria",
  "password": "password123",
  "full_name": "Иванова Мария Петровна",
  "age": 32
}
```

**Response:**
```json
{
  "access_token": "eyJhbGci...",
  "refresh_token": "base64string...",
  "expires_at": 1713456789
}
```

**Ошибки:** `INVALID_ARGUMENT` (валидация), `ALREADY_EXISTS` (логин занят), `RESOURCE_EXHAUSTED` (rate limit).

#### `Login`

**Request:**
```json
{ "login": "maria", "password": "password123" }
```

**Response:** как у Register.

**Защита от брутфорса:** 5 неудачных попыток → аккаунт блокируется на 15 минут. Rate limit: 5 попыток в минуту с одного IP.

**Ошибки:** `UNAUTHENTICATED` (неверный пароль, блокировка аккаунта), `RESOURCE_EXHAUSTED`.

#### `RefreshToken`

Обновление пары токенов.

**Request:**
```json
{ "refresh_token": "<старый refresh>" }
```

**Response:** новая пара токенов.

**⚠ Важно:** refresh-токены **одноразовые** (rotation). При использовании возвращается новая пара, старый токен отзывается. **Повторное использование уже отозванного токена** → все сессии пользователя инвалидируются (replay detection).

---

### UserProfileService

#### `GetProfile`

**Request:** `{}` (Empty)

**Response:**
```json
{
  "id": "uuid",
  "login": "maria",
  "full_name": "Иванова Мария Петровна",
  "age": 32,
  "created_at": "2026-04-01T10:00:00Z",
  "updated_at": "2026-04-18T14:30:00Z"
}
```

#### `UpdateProfile`

**Request:**
```json
{ "full_name": "Новое ФИО", "age": 33 }
```

Валидация: возраст 10-120, ФИО ≤ 200 символов.

#### `AssignDiagnoses`

Назначает список диагнозов пользователю (перезаписывает существующие).

**Request:**
```json
{
  "diagnosis_ids": [
    "10000000-0000-0000-0000-000000000001",
    "10000000-0000-0000-0000-000000000002"
  ]
}
```

**Seed UUIDs диагнозов:**
- ПМР — `10000000-0000-0000-0000-000000000001`
- СПКЯ — `10000000-0000-0000-0000-000000000002`
- Эндометриоз — `10000000-0000-0000-0000-000000000003`
- Менопауза — `10000000-0000-0000-0000-000000000004`

#### `GetMyDiagnoses`

**Request:** `{}`

**Response:**
```json
{
  "diagnoses": [
    {
      "diagnosis_id": "uuid",
      "diagnosis_name": "СПКЯ",
      "assigned_at": "2026-04-01T10:00:00Z"
    }
  ]
}
```

---

### MedicationCatalogService

#### `GetDiagnoses`

Список всех диагнозов. Request: `{}`.

#### `GetMedicationsByDiagnosis`

**Request:** `{ "diagnosis_id": "uuid" }`

**Response:**
```json
{
  "medications": [
    {
      "id": "uuid",
      "diagnosis_id": "uuid",
      "hormonal_group": "Комбинированные ОК",
      "inn": "Дроспиренон+Этинилэстрадиол",
      "trade_name": "Джес",
      "dosage": "3 мг + 0.02 мг",
      "form": "Таблетки",
      "frequency": "1 р/день",
      "diet": "Ограничить жирное"
    }
  ]
}
```

#### `GetSupplementsByMedication`

**Request:** `{ "medication_id": "uuid" }`

**Response:** список БАДов с `id`, `medication_id`, `name`, `dosage`, `frequency`.

#### `GetSideEffectsByMedication`

**Request:** `{ "medication_id": "uuid" }`

**Response:** список побочек с `id`, `medication_id`, `name`.

---

### UserMedicationService

#### `AssignMedication`

**Request:**
```json
{
  "medication_id": "uuid",
  "start_date": "2026-04-01T00:00:00Z",
  "end_date": "2026-07-01T00:00:00Z"
}
```

`end_date` опционально (приём может быть бессрочным).

**Response:**
```json
{
  "id": "uuid",
  "medication_id": "uuid",
  "medication_trade_name": "Джес",
  "medication_inn": "Дроспиренон+Этинилэстрадиол",
  "start_date": "...",
  "end_date": "...",
  "is_active": true
}
```

#### `RemoveMedication`

**Request:** `{ "user_medication_id": "uuid" }`. Soft delete.

#### `GetMyMedications`

Request: `{}`. Вернёт все назначения пользователя (включая неактивные).

#### `AssignSupplement`, `RemoveSupplement`, `GetMySupplements`

Аналогично для БАДов.

---

### SideEffectLogService

#### `LogSideEffect`

**Request:**
```json
{
  "side_effect_id": "uuid",
  "date": "2026-04-15T10:30:00Z",
  "intensity": "SIDE_EFFECT_INTENSITY_MEDIUM",
  "comment": "После утренней таблетки"
}
```

Значения `intensity`: `SIDE_EFFECT_INTENSITY_LOW`, `_MEDIUM`, `_HIGH`, `_SEVERE`.

#### `GetMySideEffectLogs`

**Request:**
```json
{
  "date_range": {
    "from": "2026-04-01T00:00:00Z",
    "to": "2026-04-30T23:59:59Z"
  },
  "pagination": { "page": 1, "page_size": 20 }
}
```

`date_range` и `pagination` опциональны.

**Response:** `logs[]`, `total_count`.

#### `DeleteSideEffectLog`

`{ "log_id": "uuid" }` — soft delete.

---

### ExternalMedicationService

Сторонние ЛС (не из справочника). Структура запросов и методов аналогична `SideEffectLogService`:
- `AddExternalMedication` — { name, dosage, date, comment? }
- `GetMyExternalMedications` — с фильтром по датам и пагинацией
- `DeleteExternalMedication` — { id }

---

### MenstrualCycleService

#### `AddCycleEntry`

**Request:**
```json
{
  "start_date": "2026-04-01T00:00:00Z",
  "end_date": "2026-04-06T00:00:00Z",
  "intensity": "CYCLE_INTENSITY_MODERATE",
  "symptoms": ["боль внизу живота", "усталость"],
  "notes": "Обычный цикл"
}
```

Значения `intensity`: `CYCLE_INTENSITY_LIGHT`, `_MODERATE`, `_HEAVY`, `_VERY_HEAVY`.

`end_date` опционально — цикл может быть в процессе.

#### `UpdateCycleEntry`

Как `AddCycleEntry`, но с обязательным `id`.

#### `GetCycleHistory`

С фильтром по `date_range` и пагинацией. Фильтрация по `StartDate`.

#### `DeleteCycleEntry`

`{ "id": "uuid" }` — soft delete.

---

### AdminService

**Требует роль `Admin`.** Назначение роли через SQL:
```sql
UPDATE "Users" SET "Role" = 'Admin' WHERE "Login" = 'admin';
```

#### `ImportMedicationData` — client streaming RPC

Загружает Excel-файл с лекарствами, БАДами и побочками для указанного диагноза.

**Client stream chunks:**
```json
{
  "chunk_data": "<bytes (base64)>",
  "file_name": "spkya.xlsx",
  "diagnosis_name": "СПКЯ"
}
```

Клиент отправляет N чанков (для больших файлов), затем закрывает стрим — сервер парсит собранный файл и возвращает результат.

**Response (unary):**
```json
{
  "success": true,
  "medications_imported": 25,
  "supplements_imported": 47,
  "side_effects_imported": 83,
  "message": "Successfully imported..."
}
```

Формат Excel — см. [Импорт справочных данных](#импорт-справочных-данных).

#### `GetImportHistory`

Request: `{}`. Вернёт список всех импортов с автором и датой.

---

## Аутентификация и безопасность

### Модель токенов

- **Access token** — JWT, 15 минут, HS256-подпись
  - Claims: `sub` (user_id), `unique_name` (login), `role`, `jti`
- **Refresh token** — криптостойкая случайная строка (64 байта, base64), 7 дней
  - Хранится в БД в таблице `RefreshTokens`
  - Rotation: при каждом использовании отзывается, создаётся новый
  - Replay detection: попытка использовать отозванный токен → все сессии пользователя инвалидируются

### Уровни защиты

1. **Rate limiting** (per IP):
   - Login: 5 попыток / мин
   - Register: 3 / час
   - RefreshToken: 10 / мин

2. **Account lockout:**
   - 5 неудачных попыток входа → блокировка на 15 минут
   - Счётчик сбрасывается при успешном входе или после истечения блокировки

3. **Password hashing:** BCrypt (cost factor 11 по умолчанию)

4. **JWT secret validation:** сервис не стартует, если секрет < 32 символов

5. **TLS:** в dev — self-signed на порту 5002, в prod — через volume-смонтированный сертификат

6. **Секреты через env-vars:** никаких plain-text паролей в коде или docker-compose

### Флоу авторизации

```
┌──────┐                          ┌────────┐
│Client│                          │ Server │
└───┬──┘                          └────┬───┘
    │  POST /Register или /Login      │
    ├────────────────────────────────>│
    │                                  │
    │  AuthResponse(access, refresh)  │
    │<────────────────────────────────┤
    │                                  │
    │  Любой защищённый вызов          │
    │  Metadata: authorization: Bearer │
    ├────────────────────────────────>│
    │                                  │
    │  (через ~15 мин access истёк)   │
    │  Server: 401 UNAUTHENTICATED    │
    │<────────────────────────────────┤
    │                                  │
    │  POST /RefreshToken { old }     │
    ├────────────────────────────────>│
    │                                  │
    │  Новая пара токенов              │
    │  (старый refresh отозван)        │
    │<────────────────────────────────┤
    │                                  │
    │  Повтор исходного запроса       │
    │  с новым access                 │
    ├────────────────────────────────>│
```

---

## Гайд для фронтенда

### Протокол: gRPC-Web

Браузеры не поддерживают обычный gRPC (нет HTTP/2 trailer'ов в fetch). Используется **gRPC-Web** поверх HTTP/1.1.

⚠️ **Для работы с браузером нужно добавить на сервер:**

```xml
<!-- MedTracker.Grpc.csproj -->
<PackageReference Include="Grpc.AspNetCore.Web" Version="2.67.0" />
```

```csharp
// Program.cs — после builder.Build()
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

// CORS (если фронт на другом origin)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyMethod()
        .AllowAnyHeader()
        .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding"));
});
// app.UseCors() после app.Build()
```

### Генерация TypeScript-клиента

Скопировать `.proto` файлы из `MedTracker.Grpc/Protos/` к себе, затем:

```bash
npm install --save-dev @protobuf-ts/plugin @protobuf-ts/grpcweb-transport

protoc \
  --plugin=protoc-gen-ts=./node_modules/.bin/protoc-gen-ts \
  --ts_out=./src/generated \
  --proto_path=./protos \
  ./protos/*.proto
```

### Пример вызова

```typescript
import { GrpcWebFetchTransport } from '@protobuf-ts/grpcweb-transport';
import { AuthServiceClient } from './generated/auth.client';

const transport = new GrpcWebFetchTransport({
  baseUrl: 'http://localhost:5001'
});

const authClient = new AuthServiceClient(transport);

const { response } = await authClient.login({
  login: 'maria',
  password: 'password123'
});

localStorage.setItem('access_token', response.accessToken);
```

### Авторизованный запрос

```typescript
const metadata = {
  authorization: `Bearer ${localStorage.getItem('access_token')}`
};

const { response } = await userProfileClient.getProfile({}, { meta: metadata });
```

### Обработка 401 и refresh

```typescript
async function callWithRefresh<T>(fn: () => Promise<T>): Promise<T> {
  try {
    return await fn();
  } catch (err: any) {
    if (err.code === 'UNAUTHENTICATED') {
      const refresh = localStorage.getItem('refresh_token');
      const { response } = await authClient.refreshToken({ refreshToken: refresh });

      localStorage.setItem('access_token', response.accessToken);
      localStorage.setItem('refresh_token', response.refreshToken);

      return await fn(); // retry один раз
    }
    throw err;
  }
}
```

### Хранение токенов — best practices

- **access_token** — в памяти (state management), истекает быстро
- **refresh_token** — httpOnly cookie с `Secure` и `SameSite=Strict` флагами (требует серверной настройки)
- **Минимум** — localStorage (уязвим к XSS, но допустимо для dev)

### Типы данных — подводные камни

| Proto type | TS type (протогенератор) | Заметка |
|---|---|---|
| `string` (UUID) | `string` | Формат `"uuid-4-format"` |
| `google.protobuf.Timestamp` | `{ seconds, nanos }` или `Date` | Зависит от генератора |
| `int32`, `int64` | `number` / `bigint` | `int64` может быть `string` в JSON |
| `enum` | `number` или enum-имя | `SIDE_EFFECT_INTENSITY_MEDIUM` |
| nullable `string` (`Comment`, `Notes`) | `string` (приходит как `""`) | Не `null`! |
| `repeated string` (`Symptoms`) | `string[]` | |

---

## Импорт справочных данных

### Формат Excel-файла

**Первая строка — заголовки** (порядок не важен, регистр учитывается):

| Колонка | Обязательна | Описание |
|---|---|---|
| Гормональные препараты | ✓ | Категория/группа ЛС |
| МНН | ✓ | Международное непатентованное наименование (или "Международное непатентованное наименование") |
| Торговое наименование | ✓ | Бренд |
| Доза | ✓ | Дозировка (мг, мкг) |
| Форма применения | ✓ | Таблетки, капсулы, инъекции |
| Частота применения | ✓ | Схема приёма (1 р/день) |
| Диета |  | Рекомендации по питанию |
| БАД |  | БАДы через запятую или точку с запятой |
| Побочные эффекты |  | Эффекты через запятую или точку с запятой |

### Пример строки

| Гормональные препараты | МНН | Торговое наименование | Доза | Форма | Частота | Диета | БАД | Побочные |
|---|---|---|---|---|---|---|---|---|
| Комбинированные ОК | Дроспиренон+Этинилэстрадиол | Джес | 3 мг + 0.02 мг | Таблетки | 1 р/день | Ограничить жирное | Омега-3, Магний B6 | Тошнота, Головная боль |

### Парсинг

- БАДы и побочки парсятся по запятой или точке с запятой → отдельные строки в `Supplements` / `SideEffects`
- Пустые строки пропускаются (строка считается пустой если пустые и МНН, и Торговое название)
- Импорт **транзакционный** — при ошибке на любой строке ничего не сохраняется
- Запись в `ImportRecords` создаётся после успеха

### Тестирование через grpcurl

```bash
# 1. Получить токен админа
TOKEN=$(grpcurl -plaintext -d '{"login":"admin","password":"..."}' \
  localhost:5001 medtracker.AuthService/Login | jq -r .access_token)

# 2. Импорт
grpcurl -plaintext \
  -H "authorization: Bearer $TOKEN" \
  -d @ localhost:5001 medtracker.AdminService/ImportMedicationData <<EOF
{
  "file_name": "spkya.xlsx",
  "diagnosis_name": "СПКЯ",
  "chunk_data": "$(base64 -i spkya.xlsx)"
}
EOF
```

---

## Коды ошибок

| gRPC Code | HTTP eq. | Когда возникает |
|---|---|---|
| `OK` | 200 | Успех |
| `INVALID_ARGUMENT` | 400 | Ошибка валидации (FluentValidation), невалидный UUID, отсутствуют колонки в Excel |
| `UNAUTHENTICATED` | 401 | Нет токена, истёкший/невалидный токен, неверный пароль, блокировка аккаунта |
| `PERMISSION_DENIED` | 403 | Доступ к чужой записи, AdminService без роли Admin |
| `NOT_FOUND` | 404 | Сущность не найдена по ID |
| `ALREADY_EXISTS` | 409 | Дубликат (занятый логин при регистрации) |
| `RESOURCE_EXHAUSTED` | 429 | Rate limit |
| `INTERNAL` | 500 | Необработанная ошибка сервера |

Маппинг реализован в `ExceptionInterceptor`. Все доменные исключения (`NotFoundException`, `DuplicateException`, `DomainValidationException`, `UnauthorizedException`, `ForbiddenException`) автоматически конвертируются в соответствующие gRPC Status.

---

## Переменные окружения

В `.env` (см. `.env.example`):

| Переменная | Обязательна | По умолчанию | Описание |
|---|---|---|---|
| `POSTGRES_USER` | ✓ | — | Пользователь БД |
| `POSTGRES_PASSWORD` | ✓ | — | Пароль |
| `POSTGRES_DB` | ✓ | — | Имя базы |
| `JWT_SECRET` | ✓ | — | Секрет JWT (≥ 32 символа, сгенерировать `openssl rand -base64 48`) |
| `JWT_ISSUER` |  | `MedTracker` | Issuer |
| `JWT_AUDIENCE` |  | `MedTrackerClients` | Audience |
| `JWT_ACCESS_TOKEN_LIFETIME_MINUTES` |  | `15` | Время жизни access token |

---

## Troubleshooting

### "JWT secret must be at least 32 characters"
Сгенерируй нормальный секрет: `openssl rand -base64 48` и положи в `.env`.

### "Bind for 0.0.0.0:5434 failed: port is already allocated"
Занят порт хоста. Либо выключи конфликтующий сервис, либо поменяй маппинг в `docker-compose.yml`: `"5435:5432"`.

### "password authentication failed for user"
Volume от старого запуска с другим паролем. Снести: `docker-compose down -v`.

### "relation __EFMigrationsHistory does not exist"
Нормально при первом запуске — EF создаст таблицу вместе с миграциями.

### "The name 'InitialCreate' is used by an existing migration"
Миграции закешированы в `bin/` или `obj/`. Почистить: `dotnet clean && find . -name "*InitialCreate*"`.

### "value too long for type character varying(N)"
Длина поля в БД меньше, чем присылаемые данные. Увеличить `HasMaxLength` в конфигурации сущности, создать новую миграцию.

### Postman: "Could not load server reflection"
Сервис работает на HTTP/1.1 или TLS. Включить HTTP/2 в Kestrel (см. `Program.cs`), в Postman отключить TLS для `localhost:5001`.

### gRPC-Web в браузере не работает
На сервере не настроен gRPC-Web. См. секцию [Гайд для фронтенда](#гайд-для-фронтенда) — добавить пакет `Grpc.AspNetCore.Web` и `app.UseGrpcWeb()`.

---

**Автор:** Vlad  
**Стек:** .NET 10 · gRPC · PostgreSQL 16 · Docker  
**Репозиторий:** /Users/vladbelyavtsev/Documents/MedTracker/
