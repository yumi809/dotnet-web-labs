# Багатоетапна збірка catalog-api. Контекст збірки — корінь репозиторію:
#   docker build -f Services/Catalog/Catalog.Api/Dockerfile -t bookhub/catalog-api:1.0 .

# ---------- 1. build: образ SDK ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Спершу лише файли проєктів → restore окремим шаром (кешується, доки не змінились залежності)
COPY global.json Directory.Build.props ./
COPY Platform.ServiceDefaults/Platform.ServiceDefaults.csproj Platform.ServiceDefaults/
COPY Platform.EventBus/Platform.EventBus.csproj Platform.EventBus/
COPY Services/Catalog/Catalog.Domain/Catalog.Domain.csproj Services/Catalog/Catalog.Domain/
COPY Services/Catalog/Catalog.Dal/Catalog.Dal.csproj Services/Catalog/Catalog.Dal/
COPY Services/Catalog/Catalog.Bll/Catalog.Bll.csproj Services/Catalog/Catalog.Bll/
COPY Services/Catalog/Catalog.Api/Catalog.Api.csproj Services/Catalog/Catalog.Api/
RUN dotnet restore Services/Catalog/Catalog.Api/Catalog.Api.csproj

# Потім вихідний код і публікація
COPY Platform.ServiceDefaults/ Platform.ServiceDefaults/
COPY Platform.EventBus/ Platform.EventBus/
COPY Services/Catalog/ Services/Catalog/
RUN dotnet publish Services/Catalog/Catalog.Api/Catalog.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- 2. final: лише runtime ASP.NET ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Catalog.Api.dll"]
