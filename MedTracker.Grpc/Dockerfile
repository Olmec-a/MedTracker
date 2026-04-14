# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for layer caching
COPY MedTracker.slnx .
COPY MedTracker.Domain/MedTracker.Domain.csproj MedTracker.Domain/
COPY MedTracker.Application/MedTracker.Application.csproj MedTracker.Application/
COPY MedTracker.Infrastructure/MedTracker.Infrastructure.csproj MedTracker.Infrastructure/
COPY MedTracker.Grpc/MedTracker.Grpc.csproj MedTracker.Grpc/

RUN dotnet restore

# Copy everything and build
COPY . .
WORKDIR /src/MedTracker.Grpc
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5001

EXPOSE 5001

ENTRYPOINT ["dotnet", "MedTracker.Grpc.dll"]