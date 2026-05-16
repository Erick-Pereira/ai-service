# Multi-stage — contexto: pasta ai-service (monorepo) ou repo standalone.

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS builder

WORKDIR /src

ARG BUILD_CONFIGURATION=Release
ARG TOKEN
ARG NUGET_GITHUB_OWNER

RUN if [ -n "${TOKEN:-}" ] && [ -n "${NUGET_GITHUB_OWNER:-}" ]; then \
    dotnet nuget add source "https://nuget.pkg.github.com/${NUGET_GITHUB_OWNER}/index.json" \
      --name github \
      --username "${NUGET_GITHUB_OWNER}" \
      --password "${TOKEN}" \
      --store-password-in-clear-text; \
  fi

COPY ["Simcag.AIService.Api/Simcag.AIService.Api.csproj", "Simcag.AIService.Api/"]
COPY ["Simcag.AIService.Application/Simcag.AIService.Application.csproj", "Simcag.AIService.Application/"]
COPY ["Simcag.AIService.Domain/Simcag.AIService.Domain.csproj", "Simcag.AIService.Domain/"]
COPY ["Simcag.AIService.Infrastructure/Simcag.AIService.Infrastructure.csproj", "Simcag.AIService.Infrastructure/"]

RUN dotnet restore "Simcag.AIService.Api/Simcag.AIService.Api.csproj"

COPY ["Simcag.AIService.Api/", "Simcag.AIService.Api/"]
COPY ["Simcag.AIService.Application/", "Simcag.AIService.Application/"]
COPY ["Simcag.AIService.Domain/", "Simcag.AIService.Domain/"]
COPY ["Simcag.AIService.Infrastructure/", "Simcag.AIService.Infrastructure/"]

RUN dotnet publish "Simcag.AIService.Api/Simcag.AIService.Api.csproj" \
    --configuration "${BUILD_CONFIGURATION}" \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

WORKDIR /app

RUN groupadd -r appgroup && useradd -r -g appgroup appuser

COPY --from=builder /app/publish .

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && chown -R appuser:appgroup /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

USER appuser

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD sh -c 'p="${ASPNETCORE_HTTP_PORTS:-8080}"; p="${p%%,*}"; curl -fsS "http://localhost:${p}/health" >/dev/null || exit 1'

ENTRYPOINT ["dotnet", "Simcag.AIService.Api.dll"]
