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
> **Com Docker (etapas 5–6):** suba os 3 nós com `docker compose up -d` (em vez do `dotnet run`)
> e troque a variável `baseUrl` do collection para o nó que quiser testar:
> `http://localhost:8080` (node1), `http://localhost:8081` (node2) ou
> `http://localhost:8082` (node3). A dificuldade dos containers vem de `NODE_DIFFICULTY`
> (**padrão 4 = ~1s por bloco**) — ajuste `difficulty = 4` no collection para os testes de
> Proof of Work baterem. Para mineração instantânea:
> `$env:NODE_DIFFICULTY = "2"; docker compose up -d --force-recreate` (e volte `difficulty = 2`).

## 2. Importe o collection

No Postman: **Import** → arraste
`postman/SatusBlockchain.postman_collection.json`.

O collection já tem as variáveis:

| Variável | Valor padrão | Para que serve |
|---|---|---|
| `baseUrl` | `http://localhost:5165` | endereço do nó |
| `difficulty` | `2` | testar o Proof of Work (`hash` começa com N zeros) |

## 3. Execute na ordem 1 → 12

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
| 11 | `GET /peers` | peers configurados (`PEERS`) — vazio no `dotnet run` sem `PEERS`; com Docker, `http://node2:8080` e `http://node3:8080` |
| 12 | `POST /transactions/receive` | entrega a transação como um **peer** faria (gossip): **200** entra na mempool; rodando de novo, **409** (dedup) |
| 13 | `POST /sync` | **pull**: o nó pergunta a cadeia dos peers e adota a válida mais longa (request 4.1) |

## 4. Propagação entre os nós (etapa 6)

O roteiro acima testa **um** nó. Para ver a propagação, suba os 3 containers
(`docker compose up -d --build`) e deixe o `baseUrl` no `node1`:

1. `POST /transactions` (request 4) → a transação entra na mempool do node1;
2. `POST /blocks/mine` (request 7) → ~1s de PoW e o bloco é **enviado aos peers**
   (`POST /blocks/receive`) ainda dentro da mesma requisição;
3. troque `baseUrl` para `http://localhost:8081` e chame `GET /` (request 1) e
   `GET /chain` (request 2): o node2 já mostra o mesmo bloco, com o **mesmo hash**;
4. repita com `http://localhost:8082` (node3) e com `GET /peers` (request 11).

O resultado do push (aceito / recusado / inacessível) fica no log do nó que minerou:

```powershell
docker compose logs -f node1
```

**Peer offline:** com `docker compose stop node3`, o `POST /blocks/mine` continua
respondendo **200** — a mineração local não depende dos peers — e o log mostra
`Peer http://node3:8080 inacessível`. Ao religar (`docker compose start node3`), o node3
volta ao genesis e **não** se recupera sozinho: ele recusa os blocos seguintes com **409**
(índice/elo fora de sequência). É exatamente o que o `POST /sync` da etapa 8 resolve.

**Reenvio e adulteração:** `POST /blocks/receive` com um bloco que o nó já tem — ou com o
conteúdo alterado — responde **409** e não mexe na cadeia. Para testar, copie um bloco de
`GET /chain` de um nó e cole no corpo de `POST {{baseUrl}}/blocks/receive` do outro.

**Gossip de transações:** postar a transação no `node1` (request 4) a faz aparecer em
`GET /transactions/pending` (request 6) do `node2` e do `node3` — cada nó com a **sua** mempool,
alimentada pelo repasse. E o bloco pode ser minerado em **outro** nó: troque `baseUrl` para
`http://localhost:8082` antes do request 7 e a transação que nasceu no node1 entra no bloco
minerado pelo node3. A request 12 entrega uma transação direto no endpoint de gossip, como um
peer faria (rodando duas vezes, a segunda responde 409 por dedup).

### 4.1 Sincronização de um nó atrasado (etapa 8)

O push (request 7) resolve o nó em dia; o **pull** resolve o nó que ficou para trás. Com os
containers no ar, o roteiro é:

```powershell
docker compose stop node3                    # o nó cai e perde a memória
curl -X POST http://localhost:8080/blocks/mine    # node1 mineia; o node2 recebe por push
curl -X POST http://localhost:8080/blocks/mine
docker compose start node3                   # volta só com o genesis
```

Depois, troque a URL da request 13 para `{{node3Url}}` e envie: o node3 puxa a cadeia dos
peers, adota a mais longa e passa a mostrar os mesmos blocos (`GET /chain` com a mesma
impressão digital do node1). A resposta traz `adopted` e `orphansBackToMempool` — este último
aparece quando o nó tinha um bloco próprio que perdeu: as transações órfãs voltam para
`GET /transactions/pending` (request 6).

## 5. Rodar tudo de uma vez (Collection Runner)

No collection, clique em **Run** → **Run SatusBlockchain**. O Postman executa
os requests em ordem e mostra os testes (verde/vermelho) de cada um.

Isso é o mesmo que os **testes de integração** automatizados fazem
(`tests/SatusBlockchain.Node.Tests/Integration/ApiTests.cs`), com uma diferença: o
Postman precisa do nó já rodando e de alguém clicando; o teste em C# sobe o
próprio nó, escolhe uma porta livre, faz os mesmos requests e roda com
`dotnet test --filter "FullyQualifiedName~Tests.Integration"` (~20s).

## 6. Se preferir sem Postman (PowerShell)

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

## 7. Erros comuns

| Sintoma | Causa provável |
|---|---|
| `Unable to connect` no Postman | o nó não está rodando, ou a porta não é 5165 (`ASPNETCORE_URLS` diferente) |
| Nó encerra na subida com "DIFFICULTY inválida" | `DIFFICULTY` fora de 0–64 |
| `POST /blocks/mine` retorna **409** | a cadeia mudou durante a mineração (outro bloco entrou). É proposital: as transações continuam na memória; mine de novo |
| Teste do PoW falhando | variável `difficulty` do collection diferente do `DIFFICULTY` do nó |
| `POST /blocks/receive` retorna **409** | o bloco não estende a cadeia local: duplicado (já está na cadeia), órfão (índice fora de sequência — nó que ficou para trás) ou adulterado (hash/PoW não conferem) |
| Bloco minerado não aparece no outro nó | o nó não tem o peer em `PEERS` (veja `GET /peers`), os nós estão com **dificuldades diferentes**, ou o outro nó reiniciou e voltou ao genesis — nesse caso ele recusa tudo com 409 até o `POST /sync` da etapa 8 |
| `POST /transactions/receive` retorna **409** | a transação já está pendente na mempool local (dedup) ou já está **confirmada** em um bloco — nenhum dos dois é erro: é o gossip chegando duas vezes |
