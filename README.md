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

## Como executar (por enquanto — nó único, sem blockchain ainda)

```bash
dotnet build
dotnet test
dotnet run --project src/SatusBlockchain.Node
```

O roadmap completo (incluindo `docker compose up` com 3 nós) está em
[docs/ROADMAP.md](docs/ROADMAP.md).
