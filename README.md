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

## Estrutura

```
src/SatusBlockchain.Node/          # nó da blockchain (ASP.NET Core)
tests/SatusBlockchain.Node.Tests/  # testes unitários (xUnit)
```

## Como executar (nó único)

```powershell
dotnet build
dotnet test

# sobe o nó (NODE_ID e DIFFICULTY são opcionais)
$env:NODE_ID = "node1"
$env:DIFFICULTY = "4"      # zeros hexadecimais do PoW; padrão 4 (~1s por bloco)
dotnet run --project src/SatusBlockchain.Node
```

### Uso da API

```powershell
# situação do nó
curl http://localhost:5000/

# criar transações (vão para a mempool)
curl -X POST http://localhost:5000/transactions `
  -H "Content-Type: application/json" `
  -d '{"from":"alice","to":"bob","amount":10}'

curl http://localhost:5000/transactions/pending

# minerar um bloco com as transações pendentes (~1s)
curl -X POST http://localhost:5000/blocks/mine

# conferir a cadeia
curl http://localhost:5000/chain
curl http://localhost:5000/chain/validate
```

| Endpoint | Descrição |
|----------|-----------|
| `GET /` | Identidade, dificuldade, tamanho e validade da cadeia |
| `GET /chain` | Cadeia completa do nó |
| `GET /chain/validate` | Valida encadeamento, hashes e Proof of Work |
| `POST /transactions` | Adiciona transação à mempool |
| `GET /transactions/pending` | Transações aguardando mineração |
| `POST /blocks/mine` | Minera um bloco com a mempool |

O roadmap completo (incluindo Docker Compose com 3 nós, propagação e consenso)
está em [docs/ROADMAP.md](docs/ROADMAP.md).
