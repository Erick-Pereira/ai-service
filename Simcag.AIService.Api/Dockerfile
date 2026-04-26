FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Simcag.AIService.Api/Simcag.AIService.Api.csproj", "Simcag.AIService.Api/"]
COPY ["Simcag.AIService.Application/Simcag.AIService.Application.csproj", "Simcag.AIService.Application/"]
COPY ["Simcag.AIService.Domain/Simcag.AIService.Domain.csproj", "Simcag.AIService.Domain/"]
COPY ["Simcag.AIService.Infrastructure/Simcag.AIService.Infrastructure.csproj", "Simcag.AIService.Infrastructure/"]
COPY ["shared/shared.csproj", "shared/"]
RUN dotnet restore "Simcag.AIService.Api/Simcag.AIService.Api.csproj"
COPY . .
WORKDIR "/src/Simcag.AIService.Api"
RUN dotnet build "Simcag.AIService.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Simcag.AIService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Simcag.AIService.Api.dll"]