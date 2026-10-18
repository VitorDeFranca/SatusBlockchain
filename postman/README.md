# Demo no Postman

A collection segue o [roteiro de apresentação](../docs/apresentacao/ROTEIRO.md).
Cada pasta é um passo. O que falar, e o que apontar no Grafana, no Jaeger e
no Loki, está no roteiro — aqui fica só a ordem de envio e o terminal que o
Postman não substitui.

## 1. Suba a stack

```powershell
cd C:\Dev\Vitor\SatusBlockchain
docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build -d
docker compose ps
```

Os três nós precisam estar `healthy`. Se a cadeia estiver suja de um ensaio,
`docker compose restart node1 node2 node3` devolve os três ao genesis.

## 2. Importe a collection

No Postman: **Import** → `postman/SatusBlockchain.postman_collection.json`.

Se a collection antiga (requests 1–21, variável `baseUrl`) ainda estiver
aberta, substitua por esta. O id é o mesmo.

| Variável | Valor | Nó |
|---|---|---|
| `node1Url` | `http://localhost:8080` | node1 |
| `node2Url` | `http://localhost:8081` | node2 |
| `node3Url` | `http://localhost:8082` | node3 |
| `node4Url` | `http://localhost:8083` | node4, só no passo 8 |
| `difficulty` | `4` | zeros exigidos no hash (`0000`) |

Não usem o **Collection Runner**. As pastas 4, 5 e 8 param no meio para um
comando de terminal, e o Runner não espera.

Enviem de cima para baixo. O teste verde confirma o campo que a narrativa
cita (mesmo `latestHash`, 400, 201, órfã de volta na mempool). Vermelho quase
sempre quer dizer que o request anterior, ou o `docker` do meio do passo,
foi pulado.

## 3. Ordem

| Pasta | Enviar | Terminal no meio |
|---|---|---|
| **0. Baseline** | `0.1` … `0.7` | — |
| **1. Transação inválida** | `1.1` | — |
| **2. Gossip da mempool** | `2.1` … `2.4` | — |
| **3. Mineração** | `3.1` … `3.5` | — O `3.1` demora ~1 s. |
| **4. Queda do node3** | `4.1` … `4.5`, depois `4.6` … `4.9` | Antes do `4.1`: `docker compose stop node3`. O `4.1` tem de falhar. Antes do `4.6`: `docker compose start node3` e esperar `healthy`. |
| **5. Partição** | `5.1` … `5.5`, depois `5.6` | Antes do `5.1`: `docker network disconnect satusblockchain_satus-net node2`. Cada mine demora ~3 s. Antes do `5.6`: `docker network connect satusblockchain_satus-net node2`. |
| **6. Fork** | `6.1` … `6.4` | — O `6.4` compara com a cadeia gravada pelo `6.3`. |
| **7. Reorg** | `7.1` … `7.6` | — |
| **8. Nó novo** | `8.1` … `8.3` | Antes: o `docker run` do roteiro. Ao sair: `docker rm -f node4`. |

Grafana, Jaeger e Loki ficam no navegador, não nesta collection. O Explore
do Loki só aparece depois do login **admin** / **admin**.

## 4. Erros comuns

| Sintoma | Causa provável |
|---|---|
| `Unable to connect` no `0.1` | a stack não está no ar, ou a porta não é 8080 |
| `4.1` responde 200 | `docker compose stop node3` não rodou |
| `4.6` não conecta | o node3 ainda não ficou `healthy` |
| `5.1` demora ~3 s e mesmo assim volta 200 | é o timeout do peer mudo, não um travamento |
| Teste de “mesmo latestHash” vermelho | o validate do node1 daquela pasta não foi enviado antes |
| Teste do PoW vermelho | `difficulty` da collection diferente do `NODE_DIFFICULTY` dos containers |
| `POST /blocks/mine` retorna **409** | outro bloco entrou durante a mineração; as transações continuam na mempool, mine de novo |
| `6.4` não acha o índice | o `6.3` não foi enviado nesta passagem |
| `7.2` sem carol → dave | o `6.2` não minerou essa transação, ou o `7.1` não adotou |
