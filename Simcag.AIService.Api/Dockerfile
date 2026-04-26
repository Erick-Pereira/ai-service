# Build context: raiz do repositório (monorepô) — ex.: docker build -f ai-service/Simcag.AIService.Api/Dockerfile -t simcag/ai-service .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["shared/shared.csproj", "shared/"]
COPY ["ai-service/Simcag.AIService.Api/Simcag.AIService.Api.csproj", "ai-service/Simcag.AIService.Api/"]
COPY ["ai-service/Simcag.AIService.Application/Simcag.AIService.Application.csproj", "ai-service/Simcag.AIService.Application/"]
COPY ["ai-service/Simcag.AIService.Domain/Simcag.AIService.Domain.csproj", "ai-service/Simcag.AIService.Domain/"]
COPY ["ai-service/Simcag.AIService.Infrastructure/Simcag.AIService.Infrastructure.csproj", "ai-service/Simcag.AIService.Infrastructure/"]
RUN dotnet restore "ai-service/Simcag.AIService.Api/Simcag.AIService.Api.csproj"

COPY shared/ shared/
COPY ai-service/ ai-service/
RUN dotnet publish "ai-service/Simcag.AIService.Api/Simcag.AIService.Api.csproj" \
  -c ${BUILD_CONFIGURATION} \
  -o /app/publish \
  /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Simcag.AIService.Api.dll"]
