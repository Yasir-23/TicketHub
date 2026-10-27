# Production image for TicketHub.Api. Build from the repository root:
#   docker build -f src/TicketHub.Api/Dockerfile .

# ---- base: runtime only ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
# The compose healthcheck probes /health with curl, which the aspnet image does not ship.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# ---- build: restore, then publish ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Project files first, so the restore layer stays cached until a dependency changes.
# global.json is deliberately not copied: this image's tag already pins the .NET 8 SDK.
COPY Directory.Build.props ./
COPY src/TicketHub.Domain/TicketHub.Domain.csproj src/TicketHub.Domain/
COPY src/TicketHub.Application/TicketHub.Application.csproj src/TicketHub.Application/
COPY src/TicketHub.Infrastructure/TicketHub.Infrastructure.csproj src/TicketHub.Infrastructure/
COPY src/TicketHub.Api/TicketHub.Api.csproj src/TicketHub.Api/
RUN dotnet restore src/TicketHub.Api/TicketHub.Api.csproj

COPY src/ src/
RUN dotnet publish src/TicketHub.Api/TicketHub.Api.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- final ----
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# Run as the image's built-in non-root user; port 8080 needs no privileges.
USER $APP_UID
ENTRYPOINT ["dotnet", "TicketHub.Api.dll"]
