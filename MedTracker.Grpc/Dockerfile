# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MedTracker.slnx .
COPY MedTracker.Domain/MedTracker.Domain.csproj MedTracker.Domain/
COPY MedTracker.Application/MedTracker.Application.csproj MedTracker.Application/
COPY MedTracker.Infrastructure/MedTracker.Infrastructure.csproj MedTracker.Infrastructure/
COPY MedTracker.Grpc/MedTracker.Grpc.csproj MedTracker.Grpc/

RUN dotnet restore

COPY . .
WORKDIR /src/MedTracker.Grpc
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl для healthcheck
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /app/keys /app/logs

EXPOSE 5001 5003

ENTRYPOINT ["dotnet", "MedTracker.Grpc.dll"]