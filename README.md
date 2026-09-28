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
dotnet test                                             # tudo: unidade + integração (~25s)

# atalhos: uma pasta por tipo de teste (namespaces Tests.Unit / Tests.Integration)
dotnet test --filter "FullyQualifiedName~Tests.Unit"         # só unidade  (~5s)
dotnet test --filter "FullyQualifiedName~Tests.Integration"  # só integração (~20s)

# sobe o nó (NODE_ID e DIFFICULTY são opcionais)
$env:NODE_ID = "node1"
$env:DIFFICULTY = "4"      # zeros hexadecimais do PoW; padrão 4 (~1s por bloco)
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
  do ambiente e o fluxo mempool → bloco. Um processo novo por teste (isolamento total).

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
| `POST /blocks/mine` | Minera um bloco com a mempool |

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
da etapa 6 vai passar. Para comprovar que eles se enxergam, de dentro de um container:

```powershell
docker compose exec node2 curl -s http://node1:8080/
```

**Dificuldade do PoW:** os containers usam `NODE_DIFFICULTY` (padrão `2` = mineração em
milissegundos). Para sentir o custo do Proof of Work:

```powershell
$env:NODE_DIFFICULTY = "4"
docker compose up -d --force-recreate      # ~1s por bloco
```

**O que ainda *não* acontece (e é esperado nesta etapa):** não há propagação entre os nós.
Minerando em `node1`, os outros dois continuam com a própria cadeia — os três compartilham
exatamente o mesmo bloco genesis (timestamp fixo), mas nada é replicado até a etapa 6.

> Estado em memória: `docker compose restart node1` faz o node1 voltar ao genesis, sem
> afetar os outros dois. É exatamente a situação que a etapa 7 (`POST /sync`) vai resolver.

O roadmap completo (propagação, consenso e forks) está em [docs/ROADMAP.md](docs/ROADMAP.md).
