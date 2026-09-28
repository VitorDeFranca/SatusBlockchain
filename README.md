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
  `PEERS` para cobrir o push (bloco propagado, duplicado, adulterado, órfão e peer offline).
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
| `POST /transactions` | Adiciona transação à mempool |
| `GET /transactions/pending` | Transações aguardando mineração |
| `POST /blocks/mine` | Minera um bloco com a mempool e propaga aos peers (push) |
| `POST /blocks/receive` | Recebe um bloco propagado (**200** aceito · **409** se não estende a cadeia) |
| `GET /peers` | Peers configurados (`PEERS`) — o destino do push |

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

## Etapa 6: propagação entre os nós (push)

Minou, propagou. Ao minerar, o nó envia o novo bloco a cada peer (`POST /blocks/receive`) —
sem nenhuma sincronização — e os três passam a mostrar **a mesma cadeia**. Nó sem `PEERS`
continua funcionando isolado, como na etapa 5.

```powershell
curl http://localhost:8080/peers     # node1 → http://node2:8080, http://node3:8080

curl -X POST http://localhost:8080/transactions `
  -H "Content-Type: application/json" -d '{"from":"alice","to":"bob","amount":10}'

curl -X POST http://localhost:8080/blocks/mine     # ~1s de PoW (dificuldade 4) + push

curl http://localhost:8081/          # node2: blocks = 2, valid = true
curl http://localhost:8082/          # node3: blocks = 2, valid = true
```

O push é **aguardado** antes de o mine responder (e os envios são paralelos): quando o
`POST /blocks/mine` retorna, os peers já receberam o bloco — por isso a conferência acima é
determinística. Quem envia aparece no log:

```powershell
docker compose logs -f node1         # "Bloco 1 propagado para http://node2:8080", ...
```

**Tolerância a peer offline** — derrube um nó e minere de novo:

```powershell
docker compose stop node3
curl -X POST http://localhost:8080/blocks/mine     # 200: a mineração local NÃO depende do node3
docker compose logs node1 --tail 5                 # "Peer http://node3:8080 inacessível: ..."
docker compose start node3
curl http://localhost:8082/                        # blocks = 1: voltou ao genesis (estado em memória)
```

**O que ainda *não* acontece (e é esperado nesta etapa):** o nó que estava fora **não** se
recupera sozinho. Ele recebe os próximos blocos, mas os recusa com **409** — eles não
encaixam na cadeia dele (índice/elo fora de sequência). É exatamente a lacuna que o
`POST /sync` da etapa 7 fecha (longest chain rule + reorg).

> Duplicidade e adulteração são recusadas na porta de entrada: reenviar o mesmo bloco, ou
> postar o bloco com o conteúdo trocado, responde **409** e não mexe na cadeia —
> `Blockchain.AddBlock` recalcula o hash e valida o Proof of Work antes de anexar.

O roadmap completo (propagação, consenso e forks) está em [docs/ROADMAP.md](docs/ROADMAP.md).
