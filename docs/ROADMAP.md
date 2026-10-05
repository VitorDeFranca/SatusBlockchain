# Roadmap — SatusBlockchain

Desenvolvimento incremental: cada etapa é pequena, compilável e testável.
Ao final de cada etapa: build → testes → resumo → teste manual → **pausa para aprovação**.

Legenda de status: ✅ concluída · 🚧 em andamento · ⬜ pendente

| # | Etapa | Entrega | Conceitos de Sist. Distribuídos | Status |
|---|-------|---------|----------------------------------|--------|
| 0 | Fundação | Solution, projetos (nó + testes), `docs/*.md` v1 | — | ✅ |
| 1 | Bloco e hash | `Block`, `Transaction`, `Hasher` (SHA-256), testes | Imutabilidade, integridade, fingerprint criptográfico | ✅ |
| 2 | Cadeia e genesis | `Blockchain`, genesis block, `IsValid()`, testes | Encadeamento, detecção de adulteração, base do estado replicado | ✅ |
| 3 | Proof of Work | `ProofOfWork`, dificuldade configurável, testes | Custo computacional, resistência à reescrita do histórico | ✅ |
| 4 | API REST (nó único) | Endpoints: chain, transactions, mine (sem peers) | Nó como serviço autônomo, contrato de comunicação | ✅ |
| 5 | Docker + Compose | `Dockerfile` multi-stage, `docker-compose.yml` com 3 nós isolados | Processos independentes, redes, configuração por ambiente | ✅ |
| 6 | Propagação (push) | `PeerClient`, broadcast no mine, `/blocks/receive` | Comunicação nó-a-nó, replicação de estado, tolerância a peer offline | ✅ |
| 7 | Gossip de transações | `PeerClient.BroadcastTransactionAsync`, `/transactions/receive`, dedup na mempool | Replicação em duas camadas: mempool (estado local, sem consenso) x cadeia | ✅ |
| 8 | Consenso e sync (pull) | `POST /sync`, longest chain rule, substituição de cadeia (reorg) | Consenso, consistência eventual, recuperação após falha | ✅ |
| 9 | Observabilidade | OpenTelemetry: `GET /metrics` (Prometheus + Grafana) e traces (Jaeger) — logs já existiam | Os três pilares de observabilidade; telemetria como janela para o sistema distribuído | ✅ |
| 10 | Fork e convergência | Roteiro e scripts de demo: fork forçado e resolução | Partição de rede, forks, convergência | ⬜ |
| 11 | Fechamento | README final, docs atualizados, roteiro de apresentação | — | ⬜ |

## Notas de ordenação

- Testes acompanham as etapas 1–9 (não são etapa separada): **unidade** para o domínio, para
  o `PeerClient` e para a telemetria (etapas 1–3, 6, 7 e 9) e **integração HTTP** para a API
  (etapas 4, 6–9), subindo o nó real em um processo isolado, em porta livre.
- Os testes ficam em **um projeto só** (`tests/SatusBlockchain.Node.Tests`), com uma pasta
  por tipo: `Unit/` (domínio isolado, sem rede) e `Integration/` (sobe o nó e conversa por
  HTTP). Os namespaces `…Tests.Unit` / `…Tests.Integration` permitem rodar só um conjunto:
  `dotnet test --filter "FullyQualifiedName~Tests.Unit"` (~20s, sem rede) ou
  `…~Tests.Integration` (~26s, sobe o nó real em porta livre). Um projeto só mantém a árvore
  da solução curta.
- Correção na etapa 4: o `DIFFICULTY` do ambiente passou a construir a `Blockchain`.
  Antes ele era apenas lido e reportado pelo `GET /` — a cadeia minerava sempre na
  dificuldade padrão (4), o que quebrava o conceito de "configuração por ambiente"
  da etapa 5. Regressão coberta por `ApiTests.DifficultyDoAmbiente_ChegaNaCadeia`.
- Etapa 5 (containers): uma imagem (`satus-node:local`), construída por um `Dockerfile`
  multi-stage (`sdk:10.0` compila, `aspnet:10.0` executa), rodada 3 vezes com `NODE_ID`
  e porta diferentes. O container escuta na 8080; o host publica 8080/8081/8082. Na rede
  `satus-net` cada nó responde por `http://nodeN:8080` (DNS interno do Docker) — verificado
  com `docker compose exec node2 curl -s http://node1:8080/`, que é exatamente o endereço
  que a etapa 6 usará como `PEERS`.
- Etapa 5 **sem mudança de código C#**: o `GET /` da etapa 4 já serve de healthcheck
  (`curl -fsS http://localhost:8080/`) e o `Dockerfile` apenas publica a aplicação.
  O `Dockerfile` e o `docker-compose.yml` foram escritos para a forma como os nós vão
  conversar (porta 8080 + nome de serviço), não para o estado atual.
- Armadilha encontrada e resolvida na etapa 5: a dificuldade dos containers vinha de
  `${DIFFICULTY:-2}`, então os três nós subiram com **dificuldade 6** herdada de um
  `$env:DIFFICULTY` deixado no shell por um teste de PoW anterior — nada no compose
  indicava essa herança. Agora a variável de interpolação é `NODE_DIFFICULTY` (exclusiva
  do compose), o que mantém a configuração por ambiente sem colidir com o `DIFFICULTY`
  que o README manda usar no `dotnet run`.
- A API (etapa 4) vem antes do Docker (etapa 5): depurar um nó via `dotnet run`
  é muito mais rápido que dentro de container.
- O desacoplamento push (etapa 6) / pull (etapa 8) permite demonstrar primeiro a
  propagação "feliz" e só depois falhas, recuperação e consenso.
- O gossip de transações (etapa 7) vem **entre** o push e o pull de propósito: é a mesma
  natureza da etapa 6 (plano de propagação, sem longest chain rule) e responde a uma pergunta
  que aparece sempre em aula — "a mempool é compartilhada entre os nós?" (não é: cada nó tem a
  sua, e o que existe é o repasse). Por isso o `/sync` deixou de ser a etapa 7: virou a **8**.
- Etapa 6 (push): `Networking/PeerClient` envia o bloco minerado a cada peer em **paralelo**
  (`POST /blocks/receive`) e o `POST /blocks/mine` **aguarda** o push antes de responder —
  decisão que torna demo e testes determinísticos: quando o mine responde, os peers já têm
  o bloco. O timeout do cliente HTTP é 2 s e cada envio é isolado: peer offline/mudo vira
  log de aviso + `inacessível`, nunca exceção. O receptor **não** re-propaga (sem cascata:
  com as listas completas de `PEERS`, um hop alcança os 3 nós).
- Etapa 6 (contrato do recebimento): `POST /blocks/receive` aceita o **bloco cru** — o mesmo
  shape de um item de `GET /chain`, o que permite copiar um bloco de um nó e postar no
  outro — e responde **200** ao anexar, **409** quando o bloco não estende a cadeia local
  (duplicado, órfão ou adulterado) e **400** em JSON malformado. A decisão é do domínio
  (`Blockchain.AddBlock`: índice, elo, hash recalculado e PoW), sem autenticação de emissor.
  As transações do bloco aceito saem da mempool local; sem isso o nó re-mineraria a mesma
  transação e criaria um fork à toa.
- Etapa 6 (configuração): `PEERS` entrou no `NodeOptions` com validação na subida
  (`ParsePeers`: URL http(s) absoluta, espaços/barra final normalizados, repetições
  descartadas) e ficou visível em `GET /peers`. O contrato do `POST /blocks/mine` **não**
  mudou: o resultado do push fica no log, o endpoint continua devolvendo o bloco — por isso
  os testes e o Postman da etapa 4 seguem válidos sem edição.
- Etapa 6 (dificuldade 4 no compose): `NODE_DIFFICULTY` passou a ter padrão **4** (~1s por
  bloco) para a propagação ser observável no log. Os testes de integração continuam fixando
  `DIFFICULTY=2` por processo, então a suíte não ficou mais lenta por causa disso.
  - Armadilha registrada: os três nós precisam da **mesma** dificuldade. O genesis é
    minerado na dificuldade do nó, então genesis(2) ≠ genesis(4) e um bloco de 2 zeros não
    satisfaz o PoW de quem exige 4 — o peer recusaria tudo. Por isso os três serviços usam
    a mesma `${NODE_DIFFICULTY:-4}`, e `PEERS` é fixo por serviço (sem interpolação de
    shell, pelo mesmo motivo que originou o `NODE_DIFFICULTY`).
  - Efeito colateral bem-vindo: com ~1s de mineração, a corrida "a cadeia mudou durante a
    mineração" (409 no `POST /blocks/mine`) fica reproduzível — minerar em dois nós dentro
    da janela faz o segundo receber o bloco do primeiro por push e descartar o próprio PoW,
    mantendo a transação na mempool. É o caminho de concorrência que o `Blockchain` já
    protegia e que só passa a acontecer de verdade quando existe propagação.
- Teste de unidade da propagação: `PeerClientTests` usa um `HttpMessageHandler` escrito à
  mão (sem Moq — o projeto não tem pacote de mock) para verificar, de forma
  determinística, que todo peer recebe o bloco, que 409 é reportado como "não aceito" sem
  exceção e que um peer inacessível não impede o envio aos demais.
- Etapa 7 (gossip de transações): `POST /transactions` grava na mempool local **e** propaga
  aos peers (`POST /transactions/receive`) — a transação postada em UM nó pode ser minerada em
  qualquer outro, que é o fluxo real (a carteira fala com um nó e conta com a propagação).
  É a segunda malha de replicação: **mempool = estado local + gossip (sem consenso)** x
  **cadeia = consenso (longest chain, etapa 8)**.
- Etapa 7 (dedup): `Mempool.Add` passou a devolver `bool` e a ignorar repetida (`Transaction` é
  record: igualdade por valor). Sem isso, com três escritores possíveis na mesma fila (o
  cliente e dois peers), a mesma transação ocuparia a fila várias vezes e poderia entrar
  repetida em um bloco.
- Etapa 7 (transação já confirmada): `Blockchain.ContainsTransaction` + checagem em
  `/transactions/receive`. O bloco pode chegar ANTES da transação (basta o peer do bloco
  responder primeiro); sem a checagem, a transação confirmada voltaria a ficar "pendente para
  sempre" e poderia ser minerada de novo. A recusa é **409** (mesma semântica do bloco
  duplicado), com mensagem distinguindo "já confirmada" de "já pendente".
- Etapa 7 (contrato): o cliente continua recebendo **201 Created** mesmo repetindo a transação
  (reenviar não é erro de quem postou — e não há reanúncio, porque os peers já foram avisados
  na primeira vez); o **409** fica do lado do peer, que é quem reenvia algo que já temos. Como
  na etapa 6, o gossip é **aguardado** antes de responder, para demo e testes determinísticos.
- Etapa 7 (escopo): gossip de **1 hop** (o receptor não re-propaga: com `PEERS` completos, um
  hop alcança os três nós) e sem taxa, priorização, evicção ou expiração — o foco é o conceito
  de replicação de estado, não política de mempool.
- Etapa 8 (pull): `POST /sync` lê `GET /chain` de cada peer em paralelo
  (`PeerClient.GetChainsAsync`) e adota a cadeia **válida e estritamente mais longa**
  (`Blockchain.TryReplaceChain`). O relatório diz o desfecho por peer — `Adopted` (adotou),
  `Kept` (a local já era maior ou do mesmo tamanho), `Invalid` (quebrada ou de outro
  "universo"), `Unreachable` (fora do ar) — e o endpoint responde **200 sempre**: peer ausente
  é situação normal numa rede parcial, não erro de quem pediu.
- Etapa 8 (três regras, nesta ordem): (1) **mesmo genesis** — dificuldade diferente gera outro
  "universo", e o PoW de lá não valeria aqui; (2) **cadeia válida**, conferida **localmente**
  (`IsValidChain`, o mesmo código do `IsValid`) — não confiamos no julgamento do peer;
  (3) **estritamente mais longa** — empate mantém a local (regra "first seen"), para que dois
  nós com a mesma cadeia não fiquem trocando de versão.
- Etapa 8 (órfãs): as transações dos blocos descartados voltam à mempool
  (`Mempool.AddRange`), **menos** as que a cadeia adotada já confirmou. Sem essa exclusão, a
  mesma transação minerada nos dois lados do fork voltaria para a fila e seria minerada de novo.
- Etapa 8 (sync manual): não há `BackgroundService` consultando os peers sozinho — o nó
  sincroniza quando alguém pede. Mantém a demonstração e os testes determinísticos e deixa
  explícito quem decide sincronizar.
- Etapa 8 (o que a falha de um teste ensinou): com push (etapa 6) e gossip (etapa 7), um nó
  "em dia" **não** diverge de ninguém — o bloco que ele minera chega aos peers na mesma
  requisição. Fork só nasce quando um nó está **à frente** e outro mina depois (o bloco que
  sobe é recusado com 409), que é o mesmo cenário de um nó isolado. Por isso o teste de fork
  usa um nó **sem `PEERS`**, que avança sozinho antes de o outro minerar.

- Etapa 9 (três pilares, um código): **logs** já existiam (`ILogger` das etapas 6–8, no
  console) e ganharam destino (Alloy → Loki → Grafana); **métricas** via `NodeTelemetry`
  (`Meter` da BCL) → `GET /metrics` (pull, formato Prometheus) → Prometheus → Grafana;
  **traces** via `ActivitySource` + spans automáticos (servidor e `HttpClient`) → OTLP (push)
  → Jaeger. O OpenTelemetry é só a camada de instrumentação/exportação: Jaeger, Prometheus,
  Loki e Grafana são destinos trocáveis sem mexer no código do nó.
- Etapa 9 (logs sem código novo): completar o pilar de logs não exigiu uma linha de C# — o
  `ILogger` já escrevia no console do container, e bastou montar o caminho de coleta
  (Docker API → Alloy → Loki → Grafana). É a divisão clássica de responsabilidade em
  observabilidade: **o código da aplicação instrumenta, a plataforma transporta**.
- Etapa 9 (OTLP condicional): o exportador de traces só é registrado se
  `OTEL_EXPORTER_OTLP_ENDPOINT` existir (e o `NodeServer` dos testes REMOVE a variável do
  ambiente herdado). Sem Jaeger, nada tenta falar com `:4317` — testes e `dotnet run` ficam
  limpos; o `/metrics`, por ser pull, sempre está no ar. O batch de export com flush de 1s
  (padrão 5s) existe para a demonstração em sala ver o trace quase junto com o curl.
- Etapa 9 (cardinalidade): tags só de domínio fechado (`outcome`, `kind`, `source`, `peer`).
  Hash, índice ou timestamp como tag viraria uma série permanente nova a cada bloco e
  explodiria a base do Prometheus. No scrape, o instrumento `satus.blocks.mined` aparece como
  `satus_blocks_mined_total{otel_scope_name=...} 1` — mesmo instrumento na convenção do
  formato Prometheus — e counter sem nenhuma medição ainda não nasce: a série nasce no
  primeiro fato.
- Etapa 9 (overlay do compose): `docker-compose.observability.yml` só ACRESCENTA serviços e a
  env de OTLP, acionado com `-f` duplo — o `docker-compose.yml` da etapa 5 continua byte a
  byte igual e `docker compose up` (sem overlay) segue sendo os 3 nós de sempre.
- Etapa 9 (o que os testes ensinaram): (1) o singleton da telemetria era preguiçoso —
  construído só no primeiro endpoint que o injetasse, o primeiro scrape do `/metrics` saía
  sem nenhuma série `satus_*`; resolveu-se materializando-o já na subida. (2) A leitura do
  `/metrics` tem uma janela de atraso assíncrona: raspagem imediata pode ver o snapshot
  anterior — o teste espera a série aparecer (polling), exatamente o papel do Prometheus ao
  raspar a cada 5s. (3) Gauge criado com `() => Length` infere `T=int` no `MeterListener`, e
  só registrar callback `long` faria os gauges sumirem do teste.
- Etapa 9 (o que subir a stack ensinou — alinhar a versão da API com a da ferramenta): o
  **Jaeger v2 removeu as APIs v1/v2** (`/api/services`, `/api/traces` devolvem 404): são
  `/api/v3/services` e `/api/v3/traces`, que exigem `query.startTimeMin/Max` em RFC3339 e
  respondem no **formato OTLP** (`result.resourceSpans[].scopeSpans[].spans[]`), não mais
  `data[].spans[]`. E o scrape de 5s do Prometheus gera traces próprios (`GET /metrics`) que
  afogam o `limit` da busca — daí o filtro `operation_name=POST /blocks/mine` nos requests.
- Etapa 9 (o que subir a stack ensinou — o coletor também é software): o `filter` do
  `discovery.docker` do Alloy é **bloco** (`name = "label"` + `values`), não atributo; e,
  mais importante, o `loki.source.docker` **descarta os labels `__meta_*`** — sem um
  `discovery.relabel` no meio, o Loki recebe os logs sem label de container (consultável por
  `service_name=unknown_service`) e não dá para filtrar por nó no LogQL.
- Etapa 9 (validação ao vivo): um único trace do `POST /blocks/mine` encadeou node1 → node2 e
  node1 → node3 pelo `traceparent` (18 spans nos três serviços), o Jaeger listou os três
  serviços, o Prometheus reportou os 4 alvos `up` e o Loki entregou o log `Bloco N minerado…`
  com `container=node1`.

## Cenário de demonstração final (referência para as etapas)

1. `docker compose up` com 3 nós;
2. criar transações em **qualquer** nó (o gossip da etapa 7 leva aos demais) e minerar no
   node1 → o bloco propaga para node2 e node3;
3. validar a cadeia em todos os nós;
4. derrubar o node3, minerar mais blocos nos demais;
5. religar o node3 e sincronizá-lo (`POST /sync`, etapa 8);
6. provocar um fork (isolar node3, minerar em ambos os lados);
7. religar e observar a convergência pela longest chain rule;
8. (opcional, etapa 9) subir a stack de observabilidade e repetir 2–7 vendo as métricas
   mudarem no Grafana e o trace de cada mineração atravessando os três nós no Jaeger.
