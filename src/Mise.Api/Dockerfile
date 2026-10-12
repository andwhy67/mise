# syntax=docker/dockerfile:1
# Контекст сборки — корень репозитория.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Mise.Domain/Mise.Domain.csproj src/Mise.Domain/
COPY src/Mise.Application/Mise.Application.csproj src/Mise.Application/
COPY src/Mise.Infrastructure/Mise.Infrastructure.csproj src/Mise.Infrastructure/
COPY src/Mise.Api/Mise.Api.csproj src/Mise.Api/
RUN dotnet restore src/Mise.Api/Mise.Api.csproj
COPY src/ src/
RUN dotnet publish src/Mise.Api/Mise.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
# Без curl в образе: простой HTTP-запрос через bash /dev/tcp.
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=6   CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\r\nHost: localhost\r\n\r\n' >&3 && grep -q '\"status\":\"Healthy\"' <&3"]
ENTRYPOINT ["dotnet", "Mise.Api.dll"]
