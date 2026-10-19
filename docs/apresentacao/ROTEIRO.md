# Roteiro de apresentação — SatusBlockchain

Duas partes. Primeiro os [slides](index.html) (conceito). Depois **este
arquivo no segundo monitor**: cada passo é um request no Postman, o que a
API devolve e o que apontar no Grafana, no Jaeger e no Loki.

A collection é `postman/SatusBlockchain.postman_collection.json`. As pastas
estão na mesma ordem deste roteiro. Enviem de cima para baixo. **Não usem o
Collection Runner**: os passos 4, 5 e 8 exigem um comando de terminal no meio,
e o Runner não espera vocês.

Tempo: **12 min de slides + 12–15 min de demo ao vivo**.

O terminal só entra onde o Postman não alcança: subir a stack, parar e
religar um container, tirar um nó da rede e, se sobrar tempo, criar o node4.

---

## Layout da tela (projetor)

Postman na metade esquerda, collection **SatusBlockchain** aberta nas pastas
0–7. O navegador na metade direita, **quatro abas**, nesta ordem —
`Ctrl+Tab` entre elas. O terminal fica minimizado até o passo 4.

| Aba | URL | Papel na aula |
|-----|-----|----------------|
| 1. Slides | `docs/apresentacao/index.html` | Conceito. Sai de cena depois do slide 22. |
| 2. Grafana | http://localhost:3000/d/satus-nos | Dashboard **SatusBlockchain — Visão dos nós**. Refresh já vem em 5 s. |
| 3. Jaeger | http://localhost:16686 | Trace que atravessa nós. Sempre filtrem a **operação** (senão o scrape de `/metrics` afoga a lista). |
| 4. Loki | http://localhost:3000/explore | Grafana → **Explore** → datasource **Loki** → modo *Code*. |

O Grafana anônimo é Viewer e **não mostra** Explore. Uma vez, antes da aula,
entrem como **admin** / **admin**. O Explore abre em Prometheus; cliquem no
nome da datasource e troquem para **Loki**, depois **Code**.

Regra de ouro: depois de cada **Send**, **esperem 5 segundos** antes de
mudar para o Grafana (o Prometheus raspa a cada 5 s). O Jaeger aparece em
~1–2 s (flush OTLP de 1 s).

Quem opera o Postman não narra. Quem narra aponta a aba e lê o número/curva
em voz alta — “os três nós em 1 bloco”, “a mempool dos três foi a 1”.

O teste verde no Postman é a confirmação rápida do campo que a narrativa
cita. Um teste vermelho quase sempre quer dizer que o request anterior, ou
o comando de terminal do meio do passo, foi pulado.

---

## Antes de entrar na sala

```powershell
cd C:\Dev\Vitor\SatusBlockchain
docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build -d
docker compose ps
```

Os três nós precisam estar `healthy`. Grafana, Jaeger, Prometheus, Loki e Alloy
também. Se a cadeia estiver suja de um ensaio:

```powershell
docker compose restart node1 node2 node3
```

Eles voltam ao genesis (estado em memória — isso é feature).

No Postman: **Import** → `postman/SatusBlockchain.postman_collection.json`.
As variáveis já apontam para a demo:

| Variável | Valor |
|----------|--------|
| `node1Url` | `http://localhost:8080` |
| `node2Url` | `http://localhost:8081` |
| `node3Url` | `http://localhost:8082` |
| `node4Url` | `http://localhost:8083` |
| `difficulty` | `4` |

Combinem com o Gabriel: um no Postman, o outro na narrativa e nas abas.

Plano B se o Docker falhar: os slides se defendem.

---

## Parte A — Slides (~12 min)

Deck em tela cheia (`F`). Notas com `N`. Não leiam o slide.

**Abertura (1–4).** Frase de abertura:

> O problema não é fazer uma criptomoeda. É fazer três computadores
> concordarem sobre o mesmo histórico sem um chefe — e ainda assim
> sobreviver quando um deles cai ou a rede se parte.

**Fundamentos (5–10).** Hash → encadeamento → genesis → mempool → PoW.
Pausa: “até aqui, um computador basta.”

**Rede (11–18).** Três nós, gossip de 1 hop, duas camadas (mempool local ×
cadeia com consenso), longest chain, fork, reorg, consistência eventual.

**Observabilidade (19–22).** Só apresentem as três ferramentas e o gap
honesto do dashboard (não há hash rate nem contador de forks). A prova
visual fica na demo, não neste bloco.

No slide “O que olhar agora”, **saiam dos slides** e vão para a pasta
**0. Baseline** no Postman.

---

## Parte B — Demo ao vivo

Cada passo abaixo tem quatro coisas: o que falar, o que enviar no Postman,
o que a API devolve, o que a observabilidade mostra. Não pulem o “mostrar”.

### Folha de consulta (deixem visível no segundo monitor)

**Grafana** — http://localhost:3000/d/satus-nos

| Painel | Série | Lê-se como |
|--------|--------|------------|
| Cadeia — blocos por nó | `satus_chain_length` | Altura de cada réplica. 1 = só o genesis. |
| Mempool — transações pendentes | `satus_mempool_size` | Fila local de cada nó. |
| Blocos minerados | `satus_blocks_mined_total` | Quem minerou, não quem recebeu. |
| Push para peers | `satus_peer_push_total` por `outcome` | `accepted` / `refused` / `unreachable` |
| HTTP p95 | duração | Pico ~2 s = timeout de peer mudo, não “travou”. |

**Jaeger** — http://localhost:16686

1. *Service* = o nó que **iniciou** o pedido (`node1`, `node2` ou `node3`).
2. *Operation* = a rota (`POST /transactions`, `POST /blocks/mine`, `POST /sync`).
3. *Find Traces* → cliquem no mais recente.
4. Um trace só deve listar **três serviços** quando o pedido saiu para os peers.

**Loki** — Grafana Explore, datasource Loki, modo *Code*. Entrem como
**admin** / **admin** se o menu Explore não aparecer.

```
{compose_project="satusblockchain"}
{compose_project="satusblockchain", container="node1"} |= "minerado"
{compose_project="satusblockchain", container="node2"} |= "recebida de um peer"
{compose_project="satusblockchain"} |= "inacessível"
{compose_project="satusblockchain"} |= "409"
{compose_project="satusblockchain"} |= "Cadeia adotada"
```

A parte entre chaves escolhe o container. `|=` fica só com as linhas que
contêm aquele texto. Cada frase só existe depois do passo que a produz.

---

### 0. Baseline — três cópias da mesma história

**Falar:** “Três processos, três memórias. A prova de que estão de acordo é
a impressão digital do último bloco: o mesmo hash nos três.”

Pasta **0. Baseline**, nesta ordem:

1. `0.1 node1 — GET /`
2. `0.2 node2 — GET /`
3. `0.3 node3 — GET /`
4. `0.4 node1 — GET /peers`
5. `0.5 node1 — GET /chain/validate`
6. `0.6 node2 — GET /chain/validate`
7. `0.7 node3 — GET /chain/validate`

**API.** `GET /` devolve `node`, `difficulty: 4`, `blocks: 1`, `valid: true`.
`GET /peers` no node1 lista `http://node2:8080` e `http://node3:8080` — sem
registry, sem líder. `GET /chain/validate` nos três: `length: 1` e o **mesmo**
`latestHash`. Os testes de 0.6 e 0.7 comparam com o hash que 0.5 gravou.

**Observabilidade (5 s).**

- Grafana → painel **Cadeia — blocos por nó**: três instâncias em **1**.
- Grafana → **Mempool**: três em **0**.
- Loki: `{compose_project="satusblockchain"}` — logs dos três containers,
  labels `container=node1|node2|node3`. É a prova de que o Alloy está
  coletando.
- Prometheus (se perguntarem “os alvos estão no ar?”):
  http://localhost:9090/query?g0.expr=up — três `up=1` dos nós.

---

### 1. Transação inválida — a porta fecha

**Falar:** “Transação inválida nem entra na mempool. Sem gossip, sem rastro
de confirmação.”

Pasta **1. Transação inválida**:

1. `1.1 node1 — POST /transactions (inválida)`

Corpo já preenchido: `from` vazio, `amount` 0.

**API.** **400**. Corpo: `From, To e Amount > 0 são obrigatórios.`

**Observabilidade (5 s).**

- Grafana → **Mempool**: continua **0** nos três. A rejeição não vaza.
- Loki: `{compose_project="satusblockchain"} |= "recebida de um peer"` —
  silêncio. Nada foi anunciado.

---

### 2. Transação válida — gossip da mempool

**Falar:** “Postei num nó só. A mempool não é compartilhada: cada um tem a
sua. O que existe é o repasse. Qualquer um destes três já pode minerar.”

Pasta **2. Gossip da mempool**:

1. `2.1 node1 — POST /transactions (alice → bob)`
2. `2.2 node1 — GET /transactions/pending`
3. `2.3 node2 — GET /transactions/pending`
4. `2.4 node3 — GET /transactions/pending`

**API.** `POST /transactions` no node1 → **201** com `{from, to, amount}`.
`GET /transactions/pending` nos **três** devolve a mesma alice→bob. O gossip
correu dentro desta requisição: o 201 só volta quando os peers já receberam.

**Observabilidade (5 s).**

- Grafana → **Mempool**: as três curvas sobem para **1**.
- Jaeger → Service **node1** → Operation **POST /transactions** → Find
  Traces. Um trace só: raiz no node1, filhos `POST /transactions/receive`
  no node2 e no node3. É o caminho da transação atravessando a rede.
- Loki:

```
{compose_project="satusblockchain", container=~"node2|node3"} |= "recebida de um peer"
```

  `Transação alice->bob recebida de um peer. Mempool com 1 pendentes.`

---

### 3. Mineração — o bloco nasce e é empurrado

**Falar:** “Agora o lote. O node1 resolve o Proof of Work — vai levar cerca
de um segundo — e empurra o bloco. Cada peer valida sozinho: hash, elo,
nonce, dificuldade. Ninguém acredita no remetente.”

Pasta **3. Mineração**. O `3.1` demora ~1 s. Esperem a resposta.

1. `3.1 node1 — POST /blocks/mine`
2. `3.2 node1 — GET /chain/validate`
3. `3.3 node2 — GET /chain/validate`
4. `3.4 node3 — GET /chain/validate`
5. `3.5 node2 — GET /transactions/pending`

**API.** O `POST /blocks/mine` devolve o bloco: `index: 1`, `nonce` (o
número que fechou os zeros), `hash` começando com `0000`, a transação
alice→bob dentro. Os três `validate` com `length: 2` e o **mesmo**
`latestHash`. A mempool do node2 está **vazia** — o bloco chegou e podou
a fila local.

**Observabilidade (5 s).** Este é o momento mais importante do Jaeger.

- Jaeger → Service **node1** → Operation **POST /blocks/mine** → o trace
  mais recente. Devem aparecer **três serviços**. No meio, o span `mine`
  (o PoW). Embaixo, `POST /blocks/receive` no node2 e no node3, com tag
  `satus.block.outcome=accepted`. Um clique prova a disciplina: a mensagem
  atravessou o HTTP com o mesmo `traceparent`.
- Grafana → **Cadeia**: os três sobem de 1 para **2**.
- Grafana → **Mempool**: os três voltam a **0**.
- Grafana → **Blocos minerados**: só o **node1** incrementa. node2 e node3
  receberam, não mineraram.
- Loki:

```
{compose_project="satusblockchain", container="node1"} |= "minerado"
{compose_project="satusblockchain", container=~"node2|node3"} |= "recebido de um peer"
```

Se perguntarem “e o caminho completo da transação, submit até bloco?”:
o submit (passo 2) e o mine (passo 3) são **dois HTTP**, portanto **dois
traces**. O que um único trace mostra é cada hop — o gossip da tx, ou a
propagação do bloco. Digam isso aqui, não no fechamento.

---

### 4. Queda de nó — a rede não espera o morto

**Falar:** “Vou matar o node3. Memória volátil: quando ele voltar, está no
genesis. Ninguém guarda o passado para reenviar. Quem ficou para trás puxa.”

Terminal, uma vez:

```powershell
docker compose stop node3
```

Pasta **4. Queda do node3**, primeiro o bloco de cima:

1. `4.1 node3 — GET /` — deve falhar. Mostrem o erro de conexão.
2. `4.2 node1 — POST /blocks/mine`
3. `4.3 node1 — POST /blocks/mine`
4. `4.4 node1 — GET /chain/validate`
5. `4.5 node2 — GET /chain/validate`

**API.** `GET` no node3 recusa conexão. Os dois mines no node1 devolvem
blocos 2 e 3 **sem esperar** o morto (o push para o node3 falha na hora:
conexão recusada, não timeout). node1 e node2 em `length: 4`, mesmo hash.

**Observabilidade (5 s).**

- Grafana → **Cadeia**: node1 e node2 em **4**. A série do node3 **congela**
  ou some — o scrape dele caiu.
- Prometheus → http://localhost:9090/targets — o alvo do node3 fica
  **DOWN**. Os outros dois, UP. “A métrica também é um detector de falha.”
- Grafana → **Push para peers**: sobe `unreachable` (ou o desfecho de
  recusa imediata).
- Loki:

```
{compose_project="satusblockchain", container="node1"} |= "inacessível"
```

  `Peer http://node3:8080 inacessível…`

Agora o retorno. Terminal:

```powershell
docker compose start node3
```

Esperem uns 3 segundos (`docker compose ps`: node3 `healthy`) e sigam na
mesma pasta:

6. `4.6 node3 — GET /`
7. `4.7 node3 — GET /chain/validate`
8. `4.8 node3 — POST /sync`
9. `4.9 node3 — GET /chain/validate`

**API.** node3 de volta com `blocks: 1`, cadeia válida, hash do **genesis**.
Não está corrompido: está **velho**. `POST /sync` → `adopted` a partir do
node1 (ou node2), `length: 4`, `orphansBackToMempool: 0`. O `validate` do
node3 agora tem o mesmo `latestHash` dos outros.

**Observabilidade (5 s).**

- Jaeger → Service **node3** → Operation **POST /sync**. Span `sync` com
  `satus.sync.outcome=adopted` e `satus.sync.length_after=4`. Os filhos são
  os `GET /chain` nos peers — o pull visível.
- Grafana → **Cadeia**: a curva do node3, sozinha em 1, **salta** para 4 e
  encontra as outras. Esta é a imagem da recuperação.
- Loki:

```
{compose_project="satusblockchain", container="node3"} |= "Cadeia adotada"
```

---

### 5. Partição — dois históricos válidos

**Falar:** “Agora eu não mato o processo. Eu minto para a rede: o node2
continua vivo, com a cadeia dele, mas sem caminho até os vizinhos. O resto
não trava.”

Por que não é `docker pause`: o socket fica vivo, o bloco entra na fila e
é entregue quando o processo descongela — o fork nunca nasce. Validamos
isso ao vivo no desenvolvimento.

Terminal. A rede do Compose tem o prefixo do projeto:

```powershell
docker network disconnect satusblockchain_satus-net node2
```

Pasta **5. Partição**. Cada mine demora ~3 s (1 s de PoW + 2 s de timeout
do peer mudo). Deixem o Postman esperar.

1. `5.1 node1 — POST /blocks/mine`
2. `5.2 node1 — POST /blocks/mine`
3. `5.3 node1 — GET /chain/validate`
4. `5.4 node3 — GET /chain/validate`
5. `5.5 node2 — GET /chain/validate`

**API.** node1 e node3 avançam **2** blocos (`length` sobe 2) e ficam com o
mesmo `latestHash`. O mine “trava” ~2 s no fim — **não é bug**: é o
`PeerClient` esperando o timeout do node2 mudo. node2 (porta do host
intacta, por isso o 5.5 responde) continua na altura **antiga**, cadeia
válida, outro `latestHash`. O teste do 5.5 exige esse hash diferente.

**Observabilidade (esperem os dois mines + 5 s).**

- Grafana → **Cadeia**: duas curvas à frente, uma parada. A partição
  desenhada.
- Grafana → **Push**: pico de `unreachable`. Grafana → **HTTP p95**:
  degrau perto de **2 s** no node1 — o timeout, visível.
- Loki:

```
{compose_project="satusblockchain", container="node1"} |= "inacessível"
```

  Agora o peer mudo é o `http://node2:8080`.

Reconectem. O node2 volta **defasado**, não atualizado.

```powershell
docker network connect satusblockchain_satus-net node2
```

6. `5.6 node2 — GET /chain/validate`

**Falar:** “Ele está errado? Não. A cadeia é válida, só está velha.
Divergência é o estado natural depois de uma partição.”

---

### 6. Fork — os dois lados minam

**Falar:** “Uma transação nasce no lado de trás. Ninguém do lado da frente
vai minerá-la agora. Aí o node2 minera **sem perguntar**.”

Pasta **6. Fork**:

1. `6.1 node2 — POST /transactions (carol → dave)`
2. `6.2 node2 — POST /blocks/mine`
3. `6.3 node1 — GET /chain`
4. `6.4 node2 — GET /chain`

**API.** Comparem o último bloco de cada cadeia. O node2 cria um bloco num
**índice que o node1 já tem**, com **outro hash**. Os dois são PoW-válidos.
A cadeia do node1 continua mais longa (ele minerou dois na partição, o
node2 minerou um). O push do node2 leva **409** nos vizinhos: o índice não
estende a cadeia deles. Isso é o fork. O teste do 6.4 faz essa comparação
com a cadeia que o 6.3 gravou.

**Observabilidade (5 s).**

- Jaeger → Service **node2** → Operation **POST /blocks/mine**. O trace
  mostra `/blocks/receive` nos peers com conflito (tag
  `satus.block.outcome=rejected` ou status 409).
- Grafana → **Cadeia**: node2 um degrau atrás dos outros (eles minaram 2,
  ele minerou 1 — precisa ficar *estritamente* atrás para o sync adotar).
- Grafana → **Push**: sobe `refused`.
- Loki:

```
{compose_project="satusblockchain", container=~"node1|node3"} |= "409"
```

  `Bloco N recusado: não estende a cadeia local.`

---

### 7. Reorg — a mais longa vence, a órfã não se perde

**Falar:** “A regra é local: cadeia válida e estritamente mais longa.
Empate não adota — por isso o outro lado minerou dois e este um. A
transação do bloco descartado volta para a mempool. Nada se perde na
troca de versão.”

Pasta **7. Reorg**:

1. `7.1 node2 — POST /sync`
2. `7.2 node2 — GET /transactions/pending`
3. `7.3 node2 — POST /blocks/mine`
4. `7.4 node1 — GET /chain/validate`
5. `7.5 node2 — GET /chain/validate`
6. `7.6 node3 — GET /chain/validate`

**API.** O `/sync` devolve `adopted` (do node1), `orphansBackToMempool: 1`.
`GET /transactions/pending` no node2 mostra carol→dave de novo. O mine
seguinte a confirma no topo. Os três `validate`: mesma `length`, mesmo
`latestHash`, `valid: true`.

**Observabilidade (5 s).**

- Jaeger → Service **node2** → Operation **POST /sync**. Span `sync`:
  `satus.sync.outcome=adopted`, `satus.sync.orphans=1`.
- Grafana → **Cadeia**: as três curvas **se reencontram**. Esta é a imagem
  da convergência — deixem ela na tela uns segundos.
- Grafana → **Mempool**: um blip de 1 no node2 (a órfã) e volta a 0
  quando ele minera de novo.
- Loki:

```
{compose_project="satusblockchain", container="node2"} |= "Cadeia adotada"
```

  `… N transações voltaram para a mempool.`

---

### 8. Acréscimo de nó (se sobrar ~2 min)

**Falar:** “Um processo novo, mesma imagem. Ele não nasce sabendo a
história: puxa. Os antigos só passam a anunciá-lo quando o `PEERS` deles
for atualizado — a descoberta é estática, de propósito. O bônus de
automação não está implementado.”

Terminal. A imagem `satus-node:local` já existe do `up --build`:

```powershell
docker run -d --name node4 --network satusblockchain_satus-net `
  -e NODE_ID=node4 `
  -e DIFFICULTY=4 `
  -e PEERS=http://node1:8080,http://node2:8080,http://node3:8080 `
  -e OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317 `
  -p 8083:8080 satus-node:local
```

Esperem uns 3 segundos. Pasta **8. Nó novo** (`node4Url` =
`http://localhost:8083`):

1. `8.1 node4 — GET /`
2. `8.2 node4 — POST /sync`
3. `8.3 node4 — GET /chain/validate`

**API.** `GET /` do node4: `blocks: 1` (genesis). Depois do `/sync`:
`adopted`, mesma `length` e mesmo `latestHash` dos outros. O teste do 8.3
compara com o hash gravado pelo último validate do node1 (o 7.4).

**Observabilidade.** O Grafana **não** ganha o quarto painel sozinho — o
Prometheus só raspa node1–node3. Mostrem o Jaeger: Service **node4** (se
o `NODE_ID` registrou o serviço) → `POST /sync` → span `sync` com
`adopted`. A API basta para a prova; digam a limitação do scrape.

Ao sair, limpem para não deixar lixo na próxima aula:

```powershell
docker rm -f node4
```

---

## Se o tempo acabar

Parem no passo 3 (mine + Jaeger) e pulem para o 5 (partição). Queda de nó
e acréscimo cabem numa frase: “o morto volta no genesis e um `/sync`
reconstrói; um nó novo faz a mesma coisa.”

Se um passo manual quebrar, religuem o node2 e recomecem do genesis:

```powershell
docker network connect satusblockchain_satus-net node2
docker compose restart node1 node2 node3
```

Esperem os três `healthy` e voltem ao passo 0 da collection.

---

## Parte C — Fechamento (~2 min)

Voltem ao slide “Enunciado × projeto”. Não peçam desculpa.

| Enunciado | O que a turma acabou de ver | O que não viu |
|-----------|-----------------------------|---------------|
| 3+ nós, sem centro | Três hashes iguais; nenhum era indispensável | — |
| Tx válida / inválida | 400 no passo 1; 201 no passo 2 | — |
| Gossip + dedup, sem laço | Mempool dos três em 1; receive não reenvia | ID explícito (dedup por valor) |
| PoW, nonce, dificuldade | `nonce` e hash `0000…` no mine | — |
| Trace da transação | Trace do gossip; trace do mine→receive | Um único trace submit→bloco |
| Grafana por nó | Altura e mempool divergindo e convergindo | Hash rate, contador de forks |
| Queda / partição / nó novo | Passos 4, 5–7, 8 | Automação de scale-out |
| Teste de carga | — | Não há |

Frase final:

> Vocês viram blockchain como sistema distribuído: sem relógio global, sem
> coordenador, com falhas explícitas — e invariantes (hash, PoW, cadeia
> mais longa) que restauram o acordo quando a rede volta.

---

## Perguntas que vão aparecer

**“Isso é um Bitcoin?”**
Não. Sem carteira, sem saldo, sem assinatura, sem recompensa. O objeto é
replicação, consenso e falha.

**“A mempool é compartilhada?”**
Não, em blockchain nenhuma. Cada nó tem a sua. O passo 2 mostrou o
repasse: três filas locais, o mesmo conteúdo.

**“Por que o sync é manual?”**
Para a demo ser determinística e para ficar visível quem decide
sincronizar. Em produção existiria um loop em background.

**“Por que `disconnect` e não `pause`?”**
O `pause` mantém o socket. O bloco chega na fila e é entregue quando o
processo descongela — o fork não nasce.

**“E se os dois lados tiverem o mesmo tamanho?”**
Empate não adota (*first seen*). Por isso o lado isolado ficou com um
bloco a menos.

**“Cadê o hash rate no Grafana?”**
Não instrumentamos. O que o dashboard prova é altura, mempool e o
desfecho do push — o suficiente para ver partição e convergência.

---

## Divisão sugerida

| Pessoa | Função |
|--------|--------|
| **Vitor** | Slides 1–18 (conceito + rede). Fecha enunciado × projeto. |
| **Gabriel** | Abas Grafana / Jaeger / Loki. Narra o que a curva e o trace mostram. |
| **Os dois** | Um no Postman, o outro apontando a aba. Não invertam no meio de um passo. |

Se só um apresentar: cortes possíveis — pulam o passo 1 (inválida vira uma
frase) e o passo 8 (nó novo). O miolo que precisa aparecer é 2 → 3 → 5 → 7
(gossip, mine+Jaeger, partição, reorg+Grafana).
