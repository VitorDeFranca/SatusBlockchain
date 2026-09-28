# Teste manual da API no Postman

Roteiro do **nó único** (Etapa 4: `GET /chain`, `GET /chain/validate`,
`POST /transactions`, `GET /transactions/pending`, `POST /blocks/mine`).

## 1. Suba o nó

```powershell
cd C:\Dev\Vitor\SatusBlockchain
$env:NODE_ID = "node1"
$env:DIFFICULTY = "2"      # 2 zeros no hash: mineração instantânea (4 = ~1s, o padrão)
dotnet run --project src/SatusBlockchain.Node
```

O nó sobe em **http://localhost:5165** (definido em
`src/SatusBlockchain.Node/Properties/launchSettings.json`).
No terminal aparece `Now listening on: http://localhost:5165`.

> Use `DIFFICULTY=2` para testar no Postman: um bloco é minerado em milissegundos.
> Se mudar para 4, ajuste também a variável `difficulty` do collection.
>
> **Com Docker (etapa 5):** suba os 3 nós com `docker compose up -d` (em vez do `dotnet run`)
> e troque a variável `baseUrl` do collection para o nó que quiser testar:
> `http://localhost:8080` (node1), `http://localhost:8081` (node2) ou
> `http://localhost:8082` (node3). A dificuldade dos containers vem de `NODE_DIFFICULTY`
> (padrão 2) — mantenha `difficulty = 2` no collection, ou suba com
> `$env:NODE_DIFFICULTY = "4"; docker compose up -d --force-recreate` e ajuste a variável.

## 2. Importe o collection

No Postman: **Import** → arraste
`postman/SatusBlockchain.postman_collection.json`.

O collection já tem as variáveis:

| Variável | Valor padrão | Para que serve |
|---|---|---|
| `baseUrl` | `http://localhost:5165` | endereço do nó |
| `difficulty` | `2` | testar o Proof of Work (`hash` começa com N zeros) |

## 3. Execute na ordem 1 → 10

| # | Request | O que observar |
|---|---------|----------------|
| 1 | `GET /` | nó de pé: `node`, `difficulty`, `blocks = 1`, `valid = true` |
| 2 | `GET /chain` | só o genesis: `index 0`, `previousHash "0"`, `hash` já com N zeros |
| 3 | `GET /chain/validate` | `valid: true`, `length: 1` |
| 4 | `POST /transactions` | **201 Created** + `Location: /transactions/pending` |
| 5 | `POST /transactions` | segunda transação (a mempool aceita várias) |
| 6 | `GET /transactions/pending` | as 2 transações **ainda não** estão em bloco nenhum |
| 7 | `POST /blocks/mine` | bloco `index 1`, com as 2 transações, PoW válido (4 ms na dificuldade 2; ~1 s na 4) |
| 8 | `GET /transactions/pending` | **vazio** — as transações entraram no bloco |
| 9 | `POST /transactions` (inválida) | **400 Bad Request** (`from` vazio, `amount <= 0`, etc.) |
| 10 | `GET /chain` | cadeia com 2 blocos: cada `previousHash` igual ao `hash` do anterior |

## 4. Rodar tudo de uma vez (Collection Runner)

No collection, clique em **Run** → **Run SatusBlockchain**. O Postman executa
os requests em ordem e mostra os testes (verde/vermelho) de cada um.

Isso é o mesmo que os **testes de integração** automatizados fazem
(`tests/SatusBlockchain.Node.Tests/Integration/ApiTests.cs`), com uma diferença: o
Postman precisa do nó já rodando e de alguém clicando; o teste em C# sobe o
próprio nó, escolhe uma porta livre, faz os mesmos requests e roda com
`dotnet test --filter "FullyQualifiedName~Tests.Integration"` (~20s).

## 5. Se preferir sem Postman (PowerShell)

```powershell
$base = "http://localhost:5165"

# objetos simples: Invoke-RestMethod basta
Invoke-RestMethod "$base/" | Format-List
Invoke-RestMethod "$base/chain/validate" | Format-List

# criar transacoes (vão para a mempool)
Invoke-RestMethod "$base/transactions" -Method Post -ContentType "application/json" `
  -Body '{"from":"alice","to":"bob","amount":10}'

Invoke-RestMethod "$base/blocks/mine" -Method Post | Format-List

# listas: pegue o corpo cru e converta, para não perder os itens
((Invoke-WebRequest -UseBasicParsing "$base/chain").Content | ConvertFrom-Json).Count
((Invoke-WebRequest -UseBasicParsing "$base/transactions/pending").Content)
```

> **Pegadinha do Windows PowerShell 5.1:** `Invoke-RestMethod` embrulha um array
> JSON em um único objeto — `$r.Count` de um `/chain` com 2 blocos responde `1`
> e `$r[0]` devolve a lista inteira, o que confunde na hora de conferir.
> O servidor está correto (o corpo cru mostra `[{...},{...}]`): para arrays, use
> `(Invoke-WebRequest -UseBasicParsing <url>).Content | ConvertFrom-Json`.

## 6. Erros comuns

| Sintoma | Causa provável |
|---|---|
| `Unable to connect` no Postman | o nó não está rodando, ou a porta não é 5165 (`ASPNETCORE_URLS` diferente) |
| Nó encerra na subida com "DIFFICULTY inválida" | `DIFFICULTY` fora de 0–64 |
| `POST /blocks/mine` retorna **409** | a cadeia mudou durante a mineração (outro bloco entrou). É proposital: as transações continuam na memória; mine de novo |
| Teste do PoW falhando | variável `difficulty` do collection diferente do `DIFFICULTY` do nó |
