# syntax=docker/dockerfile:1
# Tracto Orchestrator — multi-stage Docker build
# CI check: run `ldd /app/Tracto.Orchestrator.Api` or `grep -r "DllImport" /app` inside the
# container to confirm no Windows-only P/Invoke DLLs are present.  The .NET 8 Linux runtime
# base image ensures only cross-platform managed code and Linux-native native libs are shipped.

# ── Stage 1: SDK build ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer-caching of NuGet restore.
COPY Tracto.Orchestrator.slnx .
COPY src/Tracto.Orchestrator.Domain/Tracto.Orchestrator.Domain.csproj             src/Tracto.Orchestrator.Domain/
COPY src/Tracto.Orchestrator.Application/Tracto.Orchestrator.Application.csproj   src/Tracto.Orchestrator.Application/
COPY src/Tracto.Orchestrator.Infrastructure/Tracto.Orchestrator.Infrastructure.csproj src/Tracto.Orchestrator.Infrastructure/
COPY src/Tracto.Orchestrator.Api/Tracto.Orchestrator.Api.csproj                   src/Tracto.Orchestrator.Api/

RUN dotnet restore src/Tracto.Orchestrator.Api/Tracto.Orchestrator.Api.csproj

# Copy all source code and publish.
COPY src/ src/
RUN dotnet publish src/Tracto.Orchestrator.Api/Tracto.Orchestrator.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        --runtime linux-x64 \
        --self-contained false

# ── Stage 2: Runtime image ────────────────────────────────────────────────────
# Use the ASP.NET 8.0 runtime base — Linux-only, no Windows DLLs.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create a non-root user for least-privilege execution.
RUN addgroup --system orchestrator && adduser --system --ingroup orchestrator orchestrator
USER orchestrator

# Copy published output from build stage.
COPY --from=build /app/publish .

# TRACTO_MODE is required at startup; provide it via docker run -e TRACTO_MODE=Connected.
# TRACTO_DB_PATH overrides the default SQLite path; mount a volume to persist data.
ENV TRACTO_MODE=Connected \
    ASPNETCORE_URLS=http://0.0.0.0:8080

EXPOSE 8080

# Health check so orchestrators (Docker, Kubernetes) know when the service is ready.
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --quiet --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Tracto.Orchestrator.Api.dll"]
