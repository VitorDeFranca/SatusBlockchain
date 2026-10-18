# Arquitetura — SatusBlockchain

## 1. Visão geral

O sistema é composto por **um único projeto ASP.NET Core** (o "nó"), implantado N vezes
com configurações diferentes via Docker Compose. A distribuição do sistema vem da
**replicação do processo**, não de microsserviços: cada nó é autônomo, mantém sua própria
cópia da blockchain em memória e se comunica com os demais por HTTP/REST.

```
┌───────────────────────────────────────────────┐
│              SatusBlockchain.Node             │
│                                               │
│  ┌──────────┐    ┌─────────────────────────┐  │
│  │ REST API │───▶│ Blockchain (em memória) │  │
│  │(Minimal  │    │ - List<Block> + lock    │  │
│  │  APIs)   │    │ - Validate()            │  │
│  └────┬─────┘    │ - ReplaceChain()        │  │
│       │          └───────────▲─────────────┘  │
│       │                      │                │
│       │          ┌───────────┴────────────┐   │
│       │          │ Hasher / ProofOfWork   │   │
│       │          └────────────────────────┘   │
│       │          ┌────────────────────────┐   │
│       │          │ Mempool (pendentes)    │   │
│       │          └────────────────────────┘   │
│       │                                       │
│       │          ┌────────────────────────┐   │
│       └─────────▶│ PeerClient (HttpClient)│───┼──▶ outros nós
│                  └───────────┬────────────┘   │
│                              │ destinos       │
│  ┌──────────────────┐  ┌─────▼──────────────┐ │
│  │MembershipService │─▶│ PeerRegistry       │ │
│  │(entra ao subir,  │  │ (lista VIVA de     │ │
│  │ sai ao desligar) │  │  peers, etapa 10)  │ │
│  └──────────────────┘  └────────────────────┘ │
└───────────────────────────────────────────────┘
```

### Implantação com Docker Compose (etapa 5)

Uma **única imagem** (`satus-node:local`), construída por um `Dockerfile` multi-stage —
`sdk:10.0` no estágio de build (compila e publica) e `aspnet:10.0` no estágio final
(só o runtime + o app publicado) — executada 3 vezes, cada uma com seu `NODE_ID`.
Cada container é um processo isolado, com seu próprio estado em memória.

| Serviço | `NODE_ID` | Porta no host | Endereço interno (usado pelos peers) |
|---------|-----------|---------------|--------------------------------------|
| `node1` | `node1` | `localhost:8080` | `http://node1:8080` |
| `node2` | `node2` | `localhost:8081` | `http://node2:8080` |
| `node3` | `node3` | `localhost:8082` | `http://node3:8080` |

Na rede bridge `satus-net`, o **nome do serviço resolve para o container** (DNS interno do
Docker): é esse endereço que as etapas 6 (push), 7 (gossip) e 8 (pull/`/sync`) usam para
falar nó a nó. Na etapa 10 os três viram as **sementes** da rede: nós extras (`node4`,
`node5`, ...) entram e saem com o sistema no ar, criados pelo `scripts/add-node.ps1` a partir
do molde `node-extra` do compose (profile `extra`, que o `docker compose up` não sobe) — mesma
imagem, mesma rede, mesma dificuldade; o nome do container também resolve pelo DNS do Docker.
O container escuta na 8080 (padrão das imagens ASP.NET desde o .NET 8);
o `HEALTHCHECK` bate em `GET /`, então `docker compose ps` mostra `healthy` quando o nó
está de fato pronto (e não apenas com a porta aberta).

## 2. Estrutura de diretórios

```
SatusBlockchain/
├── docs/
│   ├── REQUIREMENTS.md        # requisitos e limites do projeto
│   ├── ARCHITECTURE.md        # este documento
│   └── ROADMAP.md             # etapas incrementais e status
├── src/
│   └── SatusBlockchain.Node/
│       ├── Core/              # domínio: Block, Transaction, Blockchain, Mempool, Hasher, ProofOfWork
│       ├── Networking/        # PeerClient (saída), PeerRegistry (lista viva), MembershipService, ChainSynchronizer
│       ├── Api/               # endpoints Minimal API agrupados por recurso
│       ├── Observability/     # NodeTelemetry: contadores/gauges satus.* e spans próprios
│       ├── Program.cs         # composição da aplicação (DI, endpoints, pipeline OpenTelemetry)
│       └── Dockerfile         # imagem do nó (build multi-stage)
├── tests/
│   └── SatusBlockchain.Node.Tests/
│       ├── Unit/               # domínio isolado (sem rede, sem processo)
│       └── Integration/        # sobe o nó e conversa por HTTP
├── scripts/                   # add-node.ps1 / remove-node.ps1: nós entrando e saindo em execução (etapa 10)
├── .dockerignore              # contexto de build enxuto (sem bin/obj/docs/tests)
├── docker-compose.yml         # os 3 nós-semente (node1..node3) + o molde node-extra
├── docker-compose.observability.yml  # overlay: Jaeger, Prometheus, Grafana, Loki, Alloy + OTLP
├── observability/             # config da stack: scrape do Prometheus, Loki, Alloy e provisioning do Grafana
├── SatusBlockchain.sln
└── README.md
```

## 3. Componentes e responsabilidades

| Componente | Responsabilidade | Não faz |
|------------|------------------|---------|
| `Block` | Dados do bloco: índice, timestamp, transações, hash anterior, nonce, hash | minerar, validar a cadeia |
| `Transaction` | Dados da transação: From, To, Amount | assinatura, validação de saldo |
| `Hasher` | SHA-256 sobre a serialização canônica (JSON ordenado) do bloco | conhecer a cadeia |
| `ProofOfWork` | Encontrar nonce que satisfaça a dificuldade; verificar nonce | decidir política de dificuldade |
| `Blockchain` | Cadeia em memória: genesis, adição de bloco, validação, substituição (reorg) | falar HTTP |
| `Mempool` | Fila FIFO de transações pendentes, com dedup por valor | priorização por taxa, evicção, expiração |
| `PeerClient` | HTTP de saída tolerante a peer offline: push de bloco, gossip de transação, pull da cadeia (`/sync`), re-propagação e mensagens de membership (anúncio, handshake, despedida); conta as falhas de cada peer no registro | regras de consenso (a validação é do domínio); decidir quem entra (regra do `POST /peers`) |
| `PeerRegistry` | A lista **viva** de peers do nó: sementes de `PEERS` + quem se anunciou − quem se despediu ou parou de responder (3 chamadas seguidas sem resposta; sementes nunca saem por falha) | handshake, HTTP |
| `MembershipService` | Ciclo de vida do nó na rede: ao subir (com `ADVERTISE_URL`) anuncia-se, descobre os peers dos peers e sincroniza; ao desligar, despede-se; reanuncia no `POST /sync` | rodar em laço — não é `BackgroundService` |
| `ChainSynchronizer` | O pull da longest chain rule (etapa 8), compartilhado pelo `POST /sync` e pela entrada na rede | julgar a cadeia (é o `Blockchain.TryReplaceChain`) |
| `NodeTelemetry` | Instrumentação: contadores/gauges `satus.*` e spans próprios `mine`/`sync` | exportar (papel do pipeline OpenTelemetry do `Program.cs`) ou gravar logs |
| Endpoints | Exposição REST do nó | lógica de domínio |

## 4. Modelo de dados

### Bloco

```
Block {
  Index: long            // posição na cadeia (genesis = 0)
  Timestamp: DateTimeOffset
  Transactions: Transaction[]
  PreviousHash: string   // hash do bloco anterior (hex)
  Nonce: long            // solução do Proof of Work
  Hash: string           // SHA-256 do conteúdo acima (hex)
}
```

### Hashing canônico

O hash é calculado sobre a serialização JSON **com campos em ordem fixa** de
`{ Index, Timestamp, Transactions, PreviousHash, Nonce }` — o campo `Hash`
nunca participa do próprio cálculo. Sem canonicalização, o mesmo bloco poderia
gerar hashes diferentes em nós diferentes.

## 5. Proof of Work

- Dificuldade = número de zeros hexadecimais iniciais exigidos no hash.
- Configurável por variável de ambiente (`DIFFICULTY`, padrão 4 → ~65k tentativas, ~1s).
- Mineração = incrementar `Nonce` até `Hasher.ComputeHash(block)` satisfazer a dificuldade.
- Verificação = recomputar o hash do bloco e conferir o prefixo.

## 6. Comunicação entre nós

### Push (propagação) — etapa 6

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
- Até a etapa 9 o receptor **não** re-propagava: com listas de peers completas, um hop
  alcançava os 3 nós. Na etapa 10 as listas passaram a mudar em execução e podem estar
  incompletas — quem **aceita** o bloco o repassa (ver "Membros dinâmicos" abaixo).
- Não há emissor autenticado: quem recusa bloco ruim é a validação do domínio
  (hash recalculado + PoW), não a boa vontade do remetente.

### Gossip de transações — etapa 7

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
- Escopo: sem taxa, priorização ou evicção. O gossip era de **1 hop** até a etapa 9; desde a
  etapa 10 quem aceita a transação a repassa (mesma regra do bloco).

### Pull (sincronização) — etapa 8

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
  quem decide sincronizar e mantém a demonstração e os testes determinísticos. A única exceção
  (etapa 10) é o nó com `ADVERTISE_URL` que ENTRA na rede: ele sincroniza uma vez ao subir, com
  o mesmo `ChainSynchronizer` do endpoint. E o `POST /sync` passou a reanunciar o nó antes de
  puxar (no-op sem `ADVERTISE_URL`) — voltar de uma queda é voltar à cadeia **e** às listas.

### Membros dinâmicos — etapa 10

Até a etapa 9 a lista de peers era o `PEERS` lido na subida, imutável. Agora ela é **estado**
de cada nó (`PeerRegistry`), local e sem consenso — como a mempool:

| Evento | Como acontece | Efeito na lista dos outros |
|--------|---------------|----------------------------|
| Nó sobe com `ADVERTISE_URL` | `MembershipService` (no `ApplicationStarted`, depois de a porta abrir) faz `POST /peers` a cada semente; a resposta traz os peers que ela conhece e o nó se anuncia a eles também (busca em largura) | entra na lista de todos |
| Nó entra | logo após o anúncio, **sincroniza** a cadeia (`ChainSynchronizer`): o push não reenvia o passado | — |
| Desligamento gracioso (`docker stop`, Ctrl+C) | `StopAsync` faz `DELETE /peers?url=...` a todos | sai na hora |
| Queda (`docker kill`, crash) | ninguém é avisado: cada chamada sem resposta (recusa, DNS, timeout) conta contra o peer | sai após **3 chamadas seguidas** sem resposta (uma resposta qualquer zera a conta) |
| Nó volta de queda/partição | `POST /sync` reanuncia o nó antes de puxar a cadeia | volta à lista |

- **Sementes** (`PEERS`) nunca saem da lista por falha — são configuração do operador e o
  caminho de volta à rede depois de uma queda ou partição. Só saem se se despedirem.
- **Handshake** no `POST /peers`: o nó chama de volta o endereço anunciado (`GET /`, que
  agora traz `genesis`) e só registra se ele **responder** (senão **502**), tiver o **mesmo
  genesis** (senão **409**: outra dificuldade = outro universo) e outro `NODE_ID` (senão
  **409**). A resposta (`PeerWelcome`) leva o genesis de quem respondeu — o recém-chegado
  confere do lado dele, então o handshake vale nos dois sentidos. URL inválida ou o próprio
  endereço → **400**; lista cheia (`MaxPeers = 32`) → **503**.
- **Re-propagação**: quem aceita um bloco (`POST /blocks/receive`) ou transação
  (`POST /transactions/receive`) de um peer repassa aos próprios peers. Não vira laço: quem já
  tem responde 409 e não repassa. É **sem esperar** (`PeerClient.RelayBlock`): o receptor
  responde ao remetente e repassa em seguida. Se aguardasse, a lentidão de um peer dois saltos
  adiante (~2s de timeout) viraria lentidão na resposta ao remetente — que a contaria como
  falha do receptor e poderia removê-lo por engano. Quem **minera** continua aguardando o
  próprio push (etapa 6): quando o `POST /blocks/mine` responde, os peers diretos já têm o
  bloco; os demais saltos chegam logo depois.
- **Nó sem `ADVERTISE_URL`** (os 3 do compose, o `dotnet run` padrão, os testes antigos) não se
  anuncia nem se despede — se comporta como nas etapas 6–9 —, mas aceita quem se anuncia a
  ele. É o papel das sementes, e mantém a demo da etapa 8 (nó religado volta ao genesis e
  precisa do `POST /sync` manual).
- **Nada em segundo plano**: só os dois ganchos do ciclo de vida do processo e o `POST /sync`.
  Sem heartbeat — a detecção de falha usa as próprias chamadas de push, gossip e sync.
- Peers continuam **validados na subida** (`NodeOptions.ParsePeers`), e a mesma regra de
  endereço (`NodeOptions.TryNormalizeUrl`: URL http(s) absoluta, sem espaços nem barra final)
  vale para `ADVERTISE_URL` e para o corpo do `POST /peers`.
- Pré-requisito continua: **mesma dificuldade** em todos os nós — é exatamente o que o
  handshake confere, comparando o genesis.

## 7. Consenso

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

## 8. Concorrência

`Blockchain`, `Mempool` e `PeerRegistry` são protegidos por `lock`. A API é inerentemente
concorrente (ASP.NET), e blocos podem chegar por dois caminhos simultâneos (mineração local e
recebimento via push); a lista de peers é lida por cada push/gossip/sync enquanto anúncios,
despedidas e falhas a alteram. Locks simples são suficientes no volume didático do projeto.

## 9. Configuração por nó

| Variável | Exemplo | Descrição |
|----------|---------|-----------|
| `NODE_ID` | `node1` | Identificador amigável (logs e respostas) |
| `PEERS` | `http://node2:8080,http://node3:8080` | Sementes: a lista de peers com que o nó nasce (muda em execução, etapa 10), validada na subida |
| `ADVERTISE_URL` | `http://node4:8080` | **Opcional:** o endereço pelo qual os outros nós chamam este. Com ele o nó se anuncia ao subir e se despede ao desligar; sem ele, não (etapa 10) |
| `DIFFICULTY` | `4` | Zeros hexadecimais exigidos no PoW |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Porta HTTP do nó (definida no `Dockerfile`) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://jaeger:4317` | **Opcional:** habilita o export de traces via OTLP. Se ausente, nenhum exportador de trace é registrado |

No `docker-compose.yml`, a dificuldade dos três containers vem de `NODE_DIFFICULTY`
(padrão 4 = ~1s por bloco; use 2 para mineração instantânea). O nome é
exclusivo do compose de propósito: um `DIFFICULTY` deixado no shell (usado pelo
`dotnet run`) não pode mais alterar os containers sem querer.

## 10. Observabilidade

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
  | `satus.peer.membership` | counter | `change=added\|removed\|evicted` (etapa 10) |
  | `satus.sync` | counter | `outcome=adopted\|kept` |
  | `satus.txs.received` | counter | `source=client\|peer`, `outcome=accepted\|rejected` |
  | `satus.chain.length` | gauge | — |
  | `satus.mempool.size` | gauge | — |
  | `satus.peers.count` | gauge | — (etapa 10: tamanho da lista viva de peers) |

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
  Prometheus (`:9090`, scrape de 5s do `:8080/metrics` de cada nó — alvos descobertos pelo
  Docker desde a etapa 10, ver abaixo), Grafana (`:3000`, datasources e
  dashboard provisionados), Loki (`grafana/loki:3.5.12`, `:3100`) e Alloy
  (`grafana/alloy:v1.20.1`, coletor que faz push ao Loki lendo os containers pelo socket do
  Docker). O `docker-compose.yml` base não é alterado.
- **Logs: o filtro e os labels** — o Alloy descobre containers com
  `discovery.docker` filtrando a label `com.docker.compose.project=satusblockchain` (só a
  stack da aula entra na base) e um `discovery.relabel` converte os labels brutos
  `__meta_docker_container_*` em labels consultáveis (`compose_project`, `compose_service`,
  `container`) — sem esse passo o Loki recebe os logs **sem nenhum label de container**, e
  todo LogQL vira varrer tudo.
- **Alvos dinâmicos (etapa 10)**: com nós entrando e saindo, a lista fixa
  `node1..node3:8080` do Prometheus deixou de servir. O scrape passou a usar
  `docker_sd_configs` — o mesmo filtro por label do projeto que o Alloy usa —, mantendo só os
  serviços `node*` na porta 8080 e reescrevendo `instance` para `<container>:8080` (os painéis
  continuam mostrando `node4:8080`, não um IP). Por isso o container do Prometheus monta o
  socket do Docker e roda como `root` (o usuário padrão da imagem não lê o socket). Os logs
  de um nó novo chegam ao Loki sem mudança alguma: o `docker compose run` do add-node já põe
  a label do projeto no container. O dashboard ganhou os painéis `satus_peers_count` (lista
  de cada nó) e `satus_peer_membership_total` (entradas, despedidas e remoções por falha).

## 11. Alternativas consideradas e descartadas

| Alternativa | Motivo do descarte |
|-------------|--------------------|
| Controllers MVC | Minimal APIs têm menos cerimônia com a mesma clareza |
| Mineração em background | Remove o controle didático; dificulta reproduzir forks |
| Registro central de nós (Consul, etcd, um "nó diretório") | Ponto único de falha e dependência externa — o oposto do que uma blockchain demonstra; a semente só é usada para ENTRAR (etapa 10) |
| Descoberta pelo DNS do Docker / orquestrador (Swarm, Kubernetes) | Amarra a membership à plataforma: o nó deixaria de funcionar fora dela (`dotnet run`, testes) |
| Heartbeat periódico (`BackgroundService`) | Quebraria a regra "nada roda sozinho"; as chamadas de push/gossip/sync já servem de detecção de falha |
| Re-propagação aguardada (receptor espera os peers dele) | A lentidão de um peer dois saltos adiante pareceria falha do vizinho e o tiraria da lista por engano |
| Assinatura digital de transações | Escopo de criptomoeda, não de sistemas distribuídos |
| Persistência (SQLite/arquivo) | Estado em memória já atende; a falta de persistência facilita a demo de recuperação |
| gRPC / message broker | REST + BCL resolvem; menos dependências para explicar |
