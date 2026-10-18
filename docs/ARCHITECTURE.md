# Arquitetura — SatusBlockchain

## 1. Visão geral

O sistema é composto por **um único projeto ASP.NET Core** (o "nó"), implantado N vezes
com configurações diferentes via Docker Compose. A distribuição do sistema vem da
**replicação do processo**, não de microsserviços: cada nó é autônomo, mantém sua própria
cópia da blockchain em memória e se comunica com os demais por HTTP/REST.

### Implantação com Docker Compose

Uma **única imagem** (`satus-node:local`), construída por um `Dockerfile` multi-stage —
`sdk:10.0` no estágio de build (compila e publica) e `aspnet:10.0` no estágio final
(só o runtime + o app publicado) — executada 3 vezes, cada uma com seu `NODE_ID`.
Cada container é um processo isolado, com seu próprio estado em memória.

| Serviço | `NODE_ID` | Porta no host | Endereço interno (usado pelos peers) |
|---------|-----------|---------------|--------------------------------------|
| `node1` | `node1` | `localhost:8080` | `http://node1:8080` |
| `node2` | `node2` | `localhost:8081` | `http://node2:8080` |
| `node3` | `node3` | `localhost:8082` | `http://node3:8080` |

## 2. Estrutura de diretórios

```
SatusBlockchain/
├── docs/
│   ├── BLOCKCHAIN-FUNDAMENTOS.md  # conceitos usados pelo projeto
│   ├── ARCHITECTURE.md            # este documento
│   └── apresentacao/              # slides (index.html) e roteiro de fala (ROTEIRO.md)
├── postman/                       # collection da demo e a ordem de envio
├── src/
│   └── SatusBlockchain.Node/
│       ├── Core/              # domínio: Block, Transaction, Blockchain, Mempool, Hasher, ProofOfWork
│       ├── Networking/        # PeerClient (comunicação de saída com outros nós)
│       ├── Api/               # endpoints Minimal API agrupados por recurso
│       ├── Observability/     # NodeTelemetry: contadores/gauges satus.* e spans próprios
│       ├── Program.cs         # composição da aplicação (DI, endpoints, pipeline OpenTelemetry)
│       └── Dockerfile         # imagem do nó (build multi-stage)
├── tests/
│   └── SatusBlockchain.Node.Tests/
│       ├── Unit/               # domínio isolado (sem rede, sem processo)
│       └── Integration/        # sobe o nó e conversa por HTTP
├── .dockerignore              # contexto de build enxuto (sem bin/obj/docs/tests)
├── docker-compose.yml         # os 3 nós (node1, node2, node3)
├── docker-compose.observability.yml  # overlay: Jaeger, Prometheus, Grafana, Loki, Alloy + OTLP
├── observability/             # config da stack: scrape do Prometheus, Loki, Alloy e provisioning do Grafana
├── SatusBlockchain.sln
└── README.md
```

### Testes

Um projeto só, `tests/SatusBlockchain.Node.Tests`, com uma pasta e um namespace por tipo.
Os comandos estão no README.

- **Unidade** (`Unit/`, namespace `Tests.Unit`): `Block`, `Hasher`, `ProofOfWork`,
  `Blockchain`, `Mempool`, `NodeOptions`, `PeerClient` e `NodeTelemetry`. Classes isoladas,
  sem rede e sem subir processo.
- **Integração** (`Integration/`, namespace `Tests.Integration`): sobe o executável numa
  porta livre e conversa por HTTP. Cobre rotas, status codes, `NODE_ID`/`DIFFICULTY`,
  o fluxo mempool → bloco, o push de blocos, o gossip de transações, o `/sync` e as
  métricas. Um processo novo por teste.

## 3. Componentes e responsabilidades

| Componente | Responsabilidade | Não faz |
|------------|------------------|---------|
| `Block` | Dados do bloco: índice, timestamp, transações, hash anterior, nonce, hash | minerar, validar a cadeia |
| `Transaction` | Dados da transação: From, To, Amount | assinatura, validação de saldo |
| `Hasher` | SHA-256 sobre a serialização canônica (JSON ordenado) do bloco | conhecer a cadeia |
| `ProofOfWork` | Encontrar nonce que satisfaça a dificuldade; verificar nonce | decidir política de dificuldade |
| `Blockchain` | Cadeia em memória: genesis, adição de bloco, validação, substituição (reorg) | falar HTTP |
| `Mempool` | Fila FIFO de transações pendentes, com dedup por valor | priorização por taxa, evicção, expiração |
| `PeerClient` | HTTP de saída tolerante a peer offline: push de bloco, gossip de transação e pull da cadeia (`/sync`) | regras de consenso (a validação é do domínio) |
| `NodeTelemetry` | Instrumentação: contadores/gauges `satus.*` e spans próprios `mine`/`sync` | exportar (papel do pipeline OpenTelemetry do `Program.cs`) ou gravar logs |
| Endpoints | Exposição REST do nó | lógica de domínio |

### Endpoints

| Endpoint | Descrição |
|----------|-----------|
| `GET /` | Identidade, dificuldade, tamanho e validade da cadeia |
| `GET /chain` | Cadeia completa do nó |
| `GET /chain/validate` | Valida encadeamento, hashes e Proof of Work |
| `POST /transactions` | Adiciona transação à mempool e propaga aos peers (gossip) |
| `GET /transactions/pending` | Transações aguardando mineração (visão local do nó) |
| `POST /transactions/receive` | Recebe uma transação propagada (200 aceita; 409 duplicada ou já confirmada) |
| `POST /blocks/mine` | Minera um bloco com a mempool e propaga aos peers (push) |
| `POST /blocks/receive` | Recebe um bloco propagado (200 aceito; 409 se não estende a cadeia) |
| `GET /peers` | Peers configurados (`PEERS`) |
| `POST /sync` | Puxa a cadeia dos peers e adota a válida mais longa |
| `GET /metrics` | Métricas do nó no formato Prometheus |

## 4. Proof of Work

- Dificuldade = número de zeros hexadecimais iniciais exigidos no hash.
- Configurável por variável de ambiente (`DIFFICULTY`, padrão 4 → ~65k tentativas, ~1s).
- Mineração = incrementar `Nonce` até `Hasher.ComputeHash(block)` satisfazer a dificuldade.
- Verificação = recomputar o hash do bloco e conferir o prefixo.

## 5. Comunicação entre nós

### Push (propagação)

- `POST /blocks/mine` mina, propaga e **só então** responde: quando a resposta chega, os
  peers já receberam o bloco (é o que torna a demo e os testes determinísticos).
- `PeerClient.BroadcastBlockAsync` envia em **paralelo** para os peers de `PEERS`
  (`POST /blocks/receive`), com timeout de 2 s no `HttpClient`.
- **Tolerância a falhas**: cada envio é isolado. Peer offline (conexão recusada) ou mudo
  (timeout) vira log de aviso + resultado `inacessível`; resposta **409** vira log de aviso
  + "não aceito". Nada disso lança exceção nem impede a mineração local.
- `POST /blocks/receive` recebe o **bloco cru** (mesmo shape de um item de `GET /chain`) e
  delega a decisão ao domínio: `Blockchain.AddBlock` confere índice, elo de `PreviousHash`,
  hash recalculado e Proof of Work. Aceito → **200** e as transações do bloco saem da
  mempool local; recusado → **409**.
- O receptor **não** re-propaga (sem cascata): com listas de peers completas, um hop
  alcança os 3 nós.
- Não há emissor autenticado: quem recusa bloco ruim é a validação do domínio
  (hash recalculado + PoW), não a boa vontade do remetente.

### Gossip de transações

- `POST /transactions` grava na mempool local **e** propaga a transação aos peers
  (`POST /transactions/receive`), aguardando o envio antes de responder (mesmo critério da
  etapa 6: quando o 201 chega, os peers já têm a transação na fila deles).
- A **mempool é local a cada nó** — não existe mempool compartilhada em blockchain nenhuma.
  O que existe é o repasse: qualquer nó que minerar pode incluir a transação, porque todos a
  receberam. É o fluxo de uma carteira real: ela fala com **um** nó e conta com a rede.
- Dedup em `Mempool.Add` (transação repetida não ocupa a fila duas vezes) e
  `Blockchain.ContainsTransaction` na recepção: se o bloco chegou **antes** da transação, ela
  não volta para a fila (409 "já confirmada").
- Ciclo que mantém as duas camadas coerentes: a transação nasce em um nó → gossip → entra em
  um bloco em qualquer nó → o bloco faz push (etapa 6) → cada nó poda da própria mempool as
  transações confirmadas.
- Escopo: gossip de **1 hop** (o receptor não re-propaga) e sem taxa, priorização ou evicção.

### Pull (sincronização)

- `POST /sync` consulta `GET /chain` de cada peer (`PeerClient.GetChainsAsync`, em paralelo e
  com o mesmo timeout de 2 s) e aplica a **longest chain rule**. É o mecanismo que recupera o
  nó que ficou offline: no push puro ele apenas recusa os blocos com 409, porque não encaixam
  na cadeia dele.
- A cadeia recebida é **validada localmente** (`Blockchain.TryReplaceChain` → `IsValidChain`, o
  mesmo código do `IsValid`): não confiamos no julgamento do peer. Dificuldade diferente gera
  genesis diferente, e aí o nó rejeita a cadeia como "outro universo".
- Só entra cadeia **estritamente mais longa**; empate mantém a local. O `POST /sync` responde
  **200 sempre** — peer ausente é situação normal numa rede parcial — e devolve um relatório por
  peer (`Adopted` / `Kept` / `Invalid` / `Unreachable`).
- Transações dos blocos órfãos voltam à **mempool** (`Mempool.AddRange`), exceto as que a
  cadeia adotada já confirmou.
- O sync é **manual**: não há `BackgroundService` consultando os peers sozinho. Deixa explícito
  quem decide sincronizar e mantém a demonstração e os testes determinísticos.

## 6. Consenso

**Longest chain rule**: uma cadeia recebida substitui a local se, e somente se:
1. for válida (encadeamento de hashes + PoW de todos os blocos); e
2. for estritamente mais longa que a cadeia local.

Empate de comprimento → mantém a cadeia local (regra "first seen").
Como não há estado além da própria cadeia (sem saldos), a substituição (reorg) é
uma operação simples e segura.

### Reorg e blocos órfãos

Ao substituir a cadeia local por uma cadeia mais longa, os blocos descartados se
tornam **órfãos**. Fiel ao comportamento do Bitcoin, as transações dos blocos órfãos
**retornam à mempool**, exceto as que já existem na cadeia adotada (evita
duplicidade). Assim, uma transação que "perdeu" seu bloco volta a ser pendente e
poderá ser confirmada em um bloco futuro — o que também permite demonstrar ao vivo
o conceito de finalidade probabilística (uma confirmação pode ser desfeita por um
reorg; quanto mais blocos sobre ela, mais segura).

## 7. Concorrência

`Blockchain` e `Mempool` são protegidos por `lock`. A API é inerentemente concorrente
(ASP.NET), e blocos podem chegar por dois caminhos simultâneos (mineração local e
recebimento via push). Locks simples são suficientes no volume didático do projeto.

## 8. Configuração por nó

| Variável | Exemplo | Descrição |
|----------|---------|-----------|
| `NODE_ID` | `node1` | Identificador amigável (logs e respostas) |
| `PEERS` | `http://node2:8080,http://node3:8080` | Lista estática de peers (destino do push), validada na subida |
| `DIFFICULTY` | `4` | Zeros hexadecimais exigidos no PoW |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Porta HTTP do nó (definida no `Dockerfile`) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://jaeger:4317` | **Opcional:** habilita o export de traces via OTLP. Se ausente, nenhum exportador de trace é registrado |

No `docker-compose.yml`, a dificuldade dos três containers vem de `NODE_DIFFICULTY`
(padrão 4 = ~1s por bloco; use 2 para mineração instantânea). O nome é
exclusivo do compose de propósito: um `DIFFICULTY` deixado no shell (usado pelo
`dotnet run`) não pode mais alterar os containers sem querer.

## 9. Observabilidade

Os três pilares, cada um com o seu caminho:

| Pilar | Instrumentação | Caminho | Destino |
|-------|----------------|---------|---------|
| **Logs** | `ILogger` | console do container → Docker API | Alloy → Loki → Grafana (com o overlay) / `docker compose logs` (sem ele) |
| **Métricas** | `NodeTelemetry` (`Meter`) + instrumentação ASP.NET/HttpClient | `GET /metrics` (pull, formato Prometheus) | Prometheus → Grafana |
| **Traces** | `ActivitySource` + spans automáticos (servidor e `HttpClient`) | OTLP (push), **só** se `OTEL_EXPORTER_OTLP_ENDPOINT` existir | Jaeger |

- **OpenTelemetry instrumenta, destinos trocam**: nomes de séries e spans moram no código do
  nó (fontes `SatusBlockchain.Node`); trocar Jaeger/Prometheus por outro backend muda apenas
  o compose, não o código do nó.
- **Catálogo de métricas** (tags sempre de domínio fechado):

  | Métrica (nome OTel) | Tipo | Tags |
  |---------------------|------|------|
  | `satus.blocks.mined` | counter | — |
  | `satus.blocks.received` | counter | `outcome=accepted\|rejected` |
  | `satus.peer.push` | counter | `kind=block\|transaction`, `outcome=accepted\|refused\|unreachable` |
  | `satus.peer.pull` | counter | `outcome=read\|unreachable\|invalid` |
  | `satus.sync` | counter | `outcome=adopted\|kept` |
  | `satus.txs.received` | counter | `source=client\|peer`, `outcome=accepted\|rejected` |
  | `satus.chain.length` | gauge | — |
  | `satus.mempool.size` | gauge | — |

- **Trace que atravessa nós**: o `HttpClient` injeta o `traceparent` no push/gossip/pull e o
  ASP.NET o relê na entrada — o `POST /blocks/mine` do node1 vira a RAIZ de um trace cujos
  filhos são os `POST /blocks/receive` do node2 e do node3 (verificado ao vivo: um trace,
  três serviços, com o span `mine` — o PoW — no meio).
- **Cardinalidade**: nunca hash, índice ou timestamp como tag — cada valor distinto é uma
  série permanente no Prometheus. Na saída, o nome aparece na convenção do formato
  (`satus_blocks_mined_total{otel_scope_name=...} 1`) e counter sem nenhuma medição ainda
  não nasce: a série nasce no primeiro fato.
- **Comportamentos aprendidos**: o singleton da telemetria é materializado na subida (sem
  isso, o primeiro `/metrics` sairia sem séries `satus_*`); a leitura tem uma janela de
  atraso assíncrona de segundos — irrelevante para o Prometheus, que raspa a cada 5s, mas os
  testes esperam a série aparecer antes de asserir.
- **Stack** (overlay `docker-compose.observability.yml`, acionado com `-f` duplo): Jaeger
  (`cr.jaegertracing.io/jaegertracing/jaeger:2.21.0`, UI em `:16686`, API em `/api/v3/...`),
  Prometheus (`:9090`, scrape de 5s dos três `:8080/metrics`), Grafana (`:3000`, datasources e
  dashboard provisionados), Loki (`grafana/loki:3.5.12`, `:3100`) e Alloy
  (`grafana/alloy:v1.20.1`, coletor que faz push ao Loki lendo os containers pelo socket do
  Docker). O `docker-compose.yml` base não é alterado.
- **Logs: o filtro e os labels** — o Alloy descobre containers com
  `discovery.docker` filtrando a label `com.docker.compose.project=satusblockchain` (só a
  stack da aula entra na base) e um `discovery.relabel` converte os labels brutos
  `__meta_docker_container_*` em labels consultáveis (`compose_project`, `compose_service`,
  `container`) — sem esse passo o Loki recebe os logs **sem nenhum label de container**, e
  todo LogQL vira varrer tudo.