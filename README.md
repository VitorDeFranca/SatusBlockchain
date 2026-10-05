# SatusBlockchain

Blockchain distribuída **educacional e simplificada**, desenvolvida para a disciplina de
**Sistemas Distribuídos**. Múltiplos nós independentes em .NET 10, comunicação via REST,
execução local com Docker Compose.

> Este projeto não é uma criptomoeda nem uma blockchain de produção — é um instrumento
> de aprendizado de conceitos distribuídos (replicação, consenso, consistência eventual,
> tolerância a falhas).

## Documentação

- [docs/BLOCKCHAIN-FUNDAMENTOS.md](docs/BLOCKCHAIN-FUNDAMENTOS.md) — **comece aqui** se você nunca estudou blockchain: os conceitos necessários para entender o projeto
- [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) — requisitos e limites do projeto
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — arquitetura e decisões técnicas
- [docs/ROADMAP.md](docs/ROADMAP.md) — etapas incrementais e status
- [postman/README.md](postman/README.md) — roteiro de teste manual da API no Postman

## Estrutura

```
src/SatusBlockchain.Node/                  # nó da blockchain (ASP.NET Core)
tests/SatusBlockchain.Node.Tests/          # testes: unidade (Unit/) e integração (Integration/)
```

## Como executar (nó único)

```powershell
dotnet build
dotnet test                                             # tudo: unidade + integração (~50s)

# atalhos: uma pasta por tipo de teste (namespaces Tests.Unit / Tests.Integration)
dotnet test --filter "FullyQualifiedName~Tests.Unit"         # só unidade  (~20s, sem rede)
dotnet test --filter "FullyQualifiedName~Tests.Integration"  # só integração (~26s, sobe nós reais)

# sobe o nó (NODE_ID, DIFFICULTY e PEERS são opcionais)
$env:NODE_ID = "node1"
$env:DIFFICULTY = "4"      # zeros hexadecimais do PoW; padrão 4 (~1s por bloco)
$env:PEERS = "http://localhost:5166,http://localhost:5167"   # opcional: outros nós (etapa 6)
dotnet run --project src/SatusBlockchain.Node
```

### Testes

Um projeto de teste só, com **uma pasta por tipo** — `Unit/` e `Integration/` — cada uma
com seu namespace, o que permite rodar só um conjunto pelo filtro:

- **Unidade** (`tests/SatusBlockchain.Node.Tests/Unit`): `Block`, `Hasher`, `ProofOfWork`,
  `Blockchain`, `Mempool`, `NodeOptions`. Testam classes isoladas — sem rede, sem HTTP,
  sem subir processo. Determinísticos e instantâneos.
- **Integração** (`tests/SatusBlockchain.Node.Tests/Integration`): sobe o executável do nó em
  uma porta livre e conversa por HTTP — valida rotas, status codes, JSON, `NODE_ID`/`DIFFICULTY`
  do ambiente e o fluxo mempool → bloco. Na etapa 6, `PropagationTests` liga 2–3 nós por
  `PEERS` para cobrir o push (bloco propagado, duplicado, adulterado, órfão e peer offline) e,
  na etapa 7, `TransactionPropagationTests` cobre o gossip (transação que nasce em um nó e é
  minerada em outro, dedup e transação já confirmada).
  Um processo novo por teste (isolamento total).

### Uso da API

O nó sobe em `http://localhost:5165` (definido em
`src/SatusBlockchain.Node/Properties/launchSettings.json`). Para testar no
Postman, importe `postman/SatusBlockchain.postman_collection.json` — roteiro
passo a passo em [postman/README.md](postman/README.md).

```powershell
# situação do nó
curl http://localhost:5165/

# criar transações (vão para a mempool)
curl -X POST http://localhost:5165/transactions `
  -H "Content-Type: application/json" `
  -d '{"from":"alice","to":"bob","amount":10}'

curl http://localhost:5165/transactions/pending

# minerar um bloco com as transações pendentes (~1s)
curl -X POST http://localhost:5165/blocks/mine

# conferir a cadeia
curl http://localhost:5165/chain
curl http://localhost:5165/chain/validate
```

| Endpoint | Descrição |
|----------|-----------|
| `GET /` | Identidade, dificuldade, tamanho e validade da cadeia |
| `GET /chain` | Cadeia completa do nó |
| `GET /chain/validate` | Valida encadeamento, hashes e Proof of Work |
| `POST /transactions` | Adiciona transação à mempool **e propaga aos peers** (gossip) |
| `GET /transactions/pending` | Transações aguardando mineração (**visão local** do nó) |
| `POST /transactions/receive` | Recebe uma transação propagada (**200** aceita · **409** duplicada ou já confirmada) |
| `POST /blocks/mine` | Minera um bloco com a mempool e propaga aos peers (push) |
| `POST /blocks/receive` | Recebe um bloco propagado (**200** aceito · **409** se não estende a cadeia) |
| `GET /peers` | Peers configurados (`PEERS`) — o destino do push |
| `POST /sync` | Puxa a cadeia dos peers e adota a **válida mais longa** (órfãs voltam à mempool) |
| `GET /metrics` | Métricas do nó no formato **Prometheus** (etapa 9): `satus_chain_length`, contadores `satus_*_total`, duração HTTP |

## Como executar (3 nós com Docker)

Cada nó roda em seu próprio container — mesma imagem, 3 processos independentes, cada um
com sua cadeia em memória. Os nós se enxergam pelo **nome do serviço** dentro da rede.

```powershell
docker compose up --build -d      # sobe node1, node2 e node3
docker compose ps                 # os 3 devem aparecer como "healthy"

curl http://localhost:8080/       # node1
curl http://localhost:8081/       # node2
curl http://localhost:8082/       # node3

docker compose logs -f node1      # log de um nó
docker compose down               # derruba tudo
```

| Nó | Do host | Dentro da rede | `NODE_ID` |
|----|---------|----------------|-----------|
| node1 | `http://localhost:8080` | `http://node1:8080` | `node1` |
| node2 | `http://localhost:8081` | `http://node2:8080` | `node2` |
| node3 | `http://localhost:8082` | `http://node3:8080` | `node3` |

Os endereços `http://nodeN:8080` são o canal **entre os nós** — é por eles que a propagação
passa (é o valor de `PEERS` no `docker-compose.yml`). Para comprovar que eles se enxergam,
de dentro de um container:

```powershell
docker compose exec node2 curl -s http://node1:8080/
```

**Dificuldade do PoW:** os containers usam `NODE_DIFFICULTY` (padrão `4` = ~1s por bloco,
tempo suficiente para acompanhar a propagação acontecendo). Para uma demo rápida, com
mineração em milissegundos:

```powershell
$env:NODE_DIFFICULTY = "2"
docker compose up -d --force-recreate      # mineração instantânea
```

> Os **três** nós precisam da mesma dificuldade: o genesis é minerado na dificuldade do
> nó, então genesis(dif 2) ≠ genesis(dif 4) e um bloco minerado com 2 zeros não satisfaz o
> PoW de quem exige 4 — o peer recusaria tudo (409). Por isso os serviços compartilham a
> mesma `${NODE_DIFFICULTY:-4}`; confira em `GET /` (`difficulty`).

## Observabilidade

O nó instrumenta os três pilares com **OpenTelemetry**: logs (o `ILogger`, coletados pelo
**Alloy** e enviados ao **Loki**), métricas (`GET /metrics`, raspadas pelo **Prometheus** e
exibidas no **Grafana**) e traces (enviados por OTLP ao **Jaeger** quando
`OTEL_EXPORTER_OTLP_ENDPOINT` está definido).

```powershell
# stack completa: 3 nós + Jaeger + Prometheus + Grafana + Loki + Alloy (overlay; o compose base não muda)
docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build -d

curl http://localhost:8080/metrics            # métricas do node1 (sempre disponível)
curl -X POST http://localhost:8080/blocks/mine
Start-Sleep -Seconds 3                        # a leitura tem ~2s de atraso assíncrono
curl http://localhost:8080/metrics            # satus_blocks_mined_total{...} 1
```

| Serviço | URL | O que mostrar |
|---------|-----|---------------|
| Jaeger | http://localhost:16686 | trace do `POST /blocks/mine` atravessando os 3 nós (serviços `node1/node2/node3`) |
| Prometheus | http://localhost:9090 | targets `up`; query `satus_chain_length` por nó |
| Grafana | http://localhost:3000 | dashboard **SatusBlockchain — Visão dos nós** (provisionado, acesso anônimo); **Explore → Loki** para logs |
| Loki | http://localhost:3100 | API de logs — `{compose_project="satusblockchain"}` |

**Demo de trace:** mine no node1 → no Jaeger, `Service: node1` → operação
`POST /blocks/mine` → um trace com o span `mine` (o PoW) e os spans `POST /blocks/receive`
do **node2** e do **node3** — o `traceparent` atravessa a rede: é a prova visual de sistemas
distribuídos, com um trace só para toda a propagação.

**Demo de logs:** mine de novo → no Grafana, **Explore → datasource Loki** → LogQL
`{compose_project="satusblockchain", container="node1"} |= "minerado"` → a linha
`Bloco N minerado...`. O caminho é: `ILogger` → console do container → Docker → Alloy →
Loki → Grafana (o Alloy só coleta os containers deste compose, por label do Docker).

Sem o overlay, `docker compose up` continua sendo apenas os 3 nós: o `/metrics` segue
disponível (é pull), mas nada é exportado — sem Jaeger, sem Prometheus, e os logs ficam só
no `docker compose logs`.

> Detalhes, catálogo de métricas e decisões em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
> (seção 10) e [docs/ROADMAP.md](docs/ROADMAP.md) (notas da etapa 9).
