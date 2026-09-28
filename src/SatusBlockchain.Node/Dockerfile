# Imagem do nó da SatusBlockchain (Etapa 5).
#
# Multi-stage: o SDK existe só no estágio de build; a imagem final fica com o
# runtime + os arquivos publicados (bem menor que o SDK).
#
# Build a partir da RAIZ do repositório (que é o contexto):
#   docker build -f src/SatusBlockchain.Node/Dockerfile -t satus-node:local .
# O `docker compose` faz isso automaticamente (ver docker-compose.yml).

# ---------- estágio 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia só o .csproj primeiro: assim o restore fica em cache de camada e não
# se repete a cada mudança de código (só quando as dependências mudarem).
COPY src/SatusBlockchain.Node/SatusBlockchain.Node.csproj src/SatusBlockchain.Node/
RUN dotnet restore src/SatusBlockchain.Node/SatusBlockchain.Node.csproj

COPY src/ src/
RUN dotnet publish src/SatusBlockchain.Node/SatusBlockchain.Node.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---------- estágio 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl entra apenas para o HEALTHCHECK: a imagem oficial de runtime não traz
# cliente HTTP. Instalado em uma única camada, com o cache do apt descartado.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Porta interna do container. 8080 já é o padrão das imagens ASP.NET desde o
# .NET 8; declarada aqui para ficar explícito. Quem publica no host é o compose.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

# GET / responde identidade, dificuldade, tamanho E validade da cadeia: é um
# healthcheck com significado, não apenas "a porta abriu".
HEALTHCHECK --interval=5s --timeout=3s --start-period=10s --retries=5 \
    CMD curl -fsS http://localhost:8080/ || exit 1

# Usuário não-root já existente nas imagens oficiais .NET 8+ ($APP_UID).
USER $APP_UID

ENTRYPOINT ["dotnet", "SatusBlockchain.Node.dll"]
