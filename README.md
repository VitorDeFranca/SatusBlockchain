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

Pré-requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download) para o nó único e os testes; [Docker Desktop](https://www.docker.com/products/docker-desktop/) para os três nós; [Node.js](https://nodejs.org/) só para compilar a interface do console fora do Docker.

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

### Console web

Uma tela com a **rede inteira**: cada nó num grafo (em dia, atrasado, fork, fora do ar,
parado — em cor, símbolo e texto), as ligações entre peers e, no painel do nó selecionado,
transação, minerar, sincronizar e — com Docker — adicionar, parar, derrubar, religar e remover
nós. Cada operação aparece num registro em português, com o que o nó respondeu.

```powershell
# com Docker: os 3 nós + o console (overlay; o compose base não muda)
docker compose -f docker-compose.yml -f docker-compose.console.yml up --build -d
start http://localhost:3001

# tudo junto: nós + observabilidade + console (os links do cabeçalho passam a funcionar)
docker compose -f docker-compose.yml -f docker-compose.observability.yml -f docker-compose.console.yml up --build -d
```

O console roda com o socket do Docker montado (é como ele cria e derruba nós) — por isso
fica num overlay próprio e só é publicado em `127.0.0.1`. Sem Docker, ele funciona igual para
ver e operar a rede, só sem os botões de ciclo de vida:

```powershell
# 1. a interface (uma vez, ou após mudar ui/): gera src/SatusBlockchain.Gateway/wwwroot
cd ui; npm install; npm run build; cd ..

# 2. os nós em outros terminais (dotnet run, como em "Nó único")

# 3. o console apontando para UM nó — os outros vêm das listas de peers
$env:GATEWAY_NODES = "http://localhost:5165"
dotnet run --project src/SatusBlockchain.Gateway --urls http://localhost:5080
start http://localhost:5080
```

Para mexer na interface com recarga instantânea: `cd ui; npm run dev` (Vite em
`localhost:5173`, repassando `/api` ao gateway em `:5080`). Testes da interface:
`npm test`, `npm run lint` e `npm run typecheck` dentro de `ui/`.

| No console | O que mostra |
|------------|--------------|
| **Rede** — grafo | linha cheia = os dois nós se listam; tracejada com seta = só um lista o outro (a visão da rede é local a cada nó); vermelha pontilhada = lados diferentes de uma partição; borda tracejada = nó dinâmico; um "pulso" quando a cadeia de um nó cresce — a propagação passando de nó em nó |
| **Rede** — cabeçalho e painel | "Rede convergida" ou "divergente: N atrasados, M em fork"; papel, endereço, cadeia, peers, mempool e últimos blocos do nó |
| **Explorador da cadeia** | os blocos de um nó com os zeros do PoW em destaque e o elo conferido; **comparar com** outro nó alinha as cadeias e aponta o **ponto de fork** (ou diz que é só atraso); clicar num bloco mostra as transações e pede ao nó que o verifique |
| **Laboratório de falhas** | o **roteiro de fork** em seis passos (isolar → minerar dos dois lados → observar → curar → sincronizar todos), partição livre (lados A/B), nó lento (acima de 2 s ele parece caído para os peers) e **bloco adulterado** (o nó mostra o hash recalculado divergindo; entregue a outro nó, 409) |

O laboratório também funciona sem o console, direto nos nós:

```powershell
# isolar o node3 (lado B) — os outros ficam "sem partição", o outro lado
Invoke-RestMethod http://localhost:8082/lab/partition -Method Put -ContentType application/json -Body '{"group":"B"}'
Invoke-RestMethod http://localhost:8082/blocks/mine -Method Post      # só o lado B conhece este bloco
Invoke-RestMethod http://localhost:8080/blocks/mine -Method Post      # node1 e node2 seguem por conta própria
Invoke-RestMethod http://localhost:8080/blocks/mine -Method Post
Invoke-RestMethod http://localhost:8082/lab -Method Delete            # curar
Invoke-RestMethod http://localhost:8082/sync -Method Post             # o bloco do node3 vira órfão
```

Como o gateway fala com os nós e com o Docker: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), seção 10.

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

Catálogo de métricas, traces e o caminho dos logs: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), seção 9.

### Testes

```powershell
dotnet test                                              # unidade + integração
dotnet test --filter "FullyQualifiedName~Tests.Unit"         # sem rede
dotnet test --filter "FullyQualifiedName~Tests.Integration"  # sobe o nó e fala HTTP
```

O `dotnet test` inclui os testes do console (`tests/SatusBlockchain.Gateway.Tests`); a
interface tem os próprios, em `ui/` (`npm test`).

O que cada pasta cobre está na [arquitetura](docs/ARCHITECTURE.md). A narrativa
da apresentação está no [roteiro](docs/apresentacao/ROTEIRO.md).
