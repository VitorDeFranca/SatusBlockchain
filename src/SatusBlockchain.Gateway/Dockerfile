# Imagem do console web (etapa 11): gateway ASP.NET + a SPA de ui/.
#
# Build a partir da RAIZ do repositório (que é o contexto):
#   docker build -f src/SatusBlockchain.Gateway/Dockerfile -t satus-console:local .
# O overlay docker-compose.console.yml faz isso automaticamente.

# ---------- estágio 1: UI (React + Vite) ----------
FROM node:24-alpine AS ui
WORKDIR /repo/ui

# Dependências primeiro: ficam em cache de camada até o package-lock mudar.
COPY ui/package.json ui/package-lock.json ./
RUN npm ci --no-fund --no-audit

COPY ui/ ./
# O vite.config.ts escreve em ../src/SatusBlockchain.Gateway/wwwroot
# (= /repo/src/SatusBlockchain.Gateway/wwwroot), o mesmo lugar do build local.
RUN npm run build

# ---------- estágio 2: gateway ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SatusBlockchain.Gateway/SatusBlockchain.Gateway.csproj src/SatusBlockchain.Gateway/
RUN dotnet restore src/SatusBlockchain.Gateway/SatusBlockchain.Gateway.csproj

COPY src/SatusBlockchain.Gateway/ src/SatusBlockchain.Gateway/
RUN dotnet publish src/SatusBlockchain.Gateway/SatusBlockchain.Gateway.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---------- estágio 3: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
COPY --from=ui /repo/src/SatusBlockchain.Gateway/wwwroot ./wwwroot

# root de propósito: o console fala com o Docker pelo socket montado
# (/var/run/docker.sock), que o usuário não-root da imagem ($APP_UID) não pode abrir.
# Mesmo motivo do Prometheus no overlay de observabilidade.
USER root

ENTRYPOINT ["dotnet", "SatusBlockchain.Gateway.dll"]
