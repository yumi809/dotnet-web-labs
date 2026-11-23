# Багатоетапна збірка API Gateway (YARP). Контекст збірки — корінь репозиторію:
#   docker build -f Platform.ApiGateway/Dockerfile -t bookhub/gateway:1.0 .

# ---------- 1. build: образ SDK ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Шар restore: лише .csproj (і спільні налаштування збірки)
COPY global.json Directory.Build.props ./
COPY Platform.ServiceDefaults/Platform.ServiceDefaults.csproj Platform.ServiceDefaults/
COPY Platform.ApiGateway/Platform.ApiGateway.csproj Platform.ApiGateway/
RUN dotnet restore Platform.ApiGateway/Platform.ApiGateway.csproj

# Шар збірки: вихідний код
COPY Platform.ServiceDefaults/ Platform.ServiceDefaults/
COPY Platform.ApiGateway/ Platform.ApiGateway/
RUN dotnet publish Platform.ApiGateway/Platform.ApiGateway.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- 2. final: runtime ASP.NET ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Platform.ApiGateway.dll"]
