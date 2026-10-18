# SatusBlockchain

Blockchain distribuída **educacional e simplificada**, desenvolvida para a disciplina de
**Sistemas Distribuídos**. Múltiplos nós independentes em .NET 10, comunicação via REST,
execução local com Docker Compose. O objetivo é demonstrar replicação, consenso,
consistência eventual e tolerância a falhas.

## Documentação

- [docs/BLOCKCHAIN-FUNDAMENTOS.md](docs/BLOCKCHAIN-FUNDAMENTOS.md) — conceitos usados pelo projeto: hash, bloco, Proof of Work, mempool, fork e cadeia mais longa
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — entidades, pastas, testes, API, consenso e observabilidade
- [docs/apresentacao/index.html](docs/apresentacao/index.html) — slides da apresentação (abra no navegador). O que falar está em [docs/apresentacao/ROTEIRO.md](docs/apresentacao/ROTEIRO.md)
- [postman/README.md](postman/README.md) — ordem de envio da collection, acompanhando o roteiro

## Como executar

Pré-requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download) para o nó único e os testes; [Docker Desktop](https://www.docker.com/products/docker-desktop/) para os três nós.

### Nó único

```powershell
dotnet build
$env:NODE_ID = "node1"
$env:DIFFICULTY = "4"      # zeros hexadecimais do PoW; padrão 4 (~1s por bloco)
dotnet run --project src/SatusBlockchain.Node
```

O nó sobe em `http://localhost:5165`
(`src/SatusBlockchain.Node/Properties/launchSettings.json`).
`PEERS` é opcional, por exemplo `http://localhost:5166,http://localhost:5167`.

```powershell
curl http://localhost:5165/

curl -X POST http://localhost:5165/transactions `
  -H "Content-Type: application/json" `
  -d '{"from":"alice","to":"bob","amount":10}'

curl -X POST http://localhost:5165/blocks/mine
curl http://localhost:5165/chain
```

O contrato completo da API está em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
A collection do Postman fala com os três containers (portas 8080–8082).

### Três nós

Cada container é o mesmo processo, com cadeia própria em memória. Dentro da rede,
os nós se enxergam pelo nome do serviço.

```powershell
docker compose up --build -d
docker compose ps                 # os 3 devem aparecer como "healthy"
curl http://localhost:8080/       # node1
curl http://localhost:8081/       # node2
curl http://localhost:8082/       # node3
docker compose logs -f node1
docker compose down
```

| Nó | Do host | Dentro da rede |
|----|---------|----------------|
| node1 | `http://localhost:8080` | `http://node1:8080` |
| node2 | `http://localhost:8081` | `http://node2:8080` |
| node3 | `http://localhost:8082` | `http://node3:8080` |

Os três nós precisam da mesma dificuldade: o genesis é minerado nela. No Compose a
variável é `NODE_DIFFICULTY` (padrão `4`, cerca de 1 s por bloco). Para mineração
em milissegundos:

```powershell
$env:NODE_DIFFICULTY = "2"
docker compose up -d --force-recreate
```

### Observabilidade

O compose base sobe só os três nós. O overlay acrescenta Jaeger, Prometheus, Grafana,
Loki e Alloy. `GET /metrics` existe nos dois casos.

```powershell
docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build -d
```

| Serviço | URL |
|---------|-----|
| Jaeger | http://localhost:16686 |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 |
| Loki | http://localhost:3100 |

Catálogo de métricas, traces e o caminho dos logs: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), seção 10.

### Testes

```powershell
dotnet test                                              # unidade + integração
dotnet test --filter "FullyQualifiedName~Tests.Unit"         # sem rede
dotnet test --filter "FullyQualifiedName~Tests.Integration"  # sobe o nó e fala HTTP
```

O que cada pasta cobre está na [arquitetura](docs/ARCHITECTURE.md). A narrativa
da apresentação está no [roteiro](docs/apresentacao/ROTEIRO.md).
