# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем только csproj-файлы runtime-проектов (не тестовых).
# Это позволяет кешировать слой restore: пока csproj не меняются, restore не выполняется заново.
COPY MedTracker.Domain/MedTracker.Domain.csproj MedTracker.Domain/
COPY MedTracker.Application/MedTracker.Application.csproj MedTracker.Application/
COPY MedTracker.Infrastructure/MedTracker.Infrastructure.csproj MedTracker.Infrastructure/
COPY MedTracker.Grpc/MedTracker.Grpc.csproj MedTracker.Grpc/

# ВАЖНО: restore на КОНКРЕТНОМ проекте, а не на солюшене.
# Если делать `dotnet restore` без аргумента, он попытается восстановить все проекты
# из MedTracker.slnx — включая Tests и IntegrationTests, которых нет в build-контексте.
# ProjectReference из Grpc → Infrastructure → Application → Domain подтянутся транзитивно.
RUN dotnet restore MedTracker.Grpc/MedTracker.Grpc.csproj

COPY . .
WORKDIR /src/MedTracker.Grpc
# Publish тоже на конкретном проекте, чтобы Tests/IntegrationTests не попали в build даже если их случайно скопировали.
RUN dotnet publish MedTracker.Grpc.csproj -c Release -o /app/publish --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl для healthcheck'а в compose/k8s
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Каталоги для backwards compat — DataProtection теперь в Redis, логи в stdout,
# но если что-то ещё попытается писать сюда — не упадёт.
RUN mkdir -p /app/keys /app/logs

EXPOSE 5001 5003

ENTRYPOINT ["dotnet", "MedTracker.Grpc.dll"]