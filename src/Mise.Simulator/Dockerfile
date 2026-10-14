# syntax=docker/dockerfile:1
# Контекст сборки — корень репозитория.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Mise.Simulator/Mise.Simulator.csproj src/Mise.Simulator/
RUN dotnet restore src/Mise.Simulator/Mise.Simulator.csproj
COPY src/Mise.Simulator/ src/Mise.Simulator/
# Протокол API ↔ симулятор подключён в csproj ссылкой на файл.
COPY src/Mise.Application/Simulator/SimulatorProtocol.cs src/Mise.Application/Simulator/
RUN dotnet publish src/Mise.Simulator/Mise.Simulator.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Mise.Simulator.dll"]
