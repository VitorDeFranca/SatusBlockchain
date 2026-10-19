# SatusBlockchain — resumo completo do projeto

> **Para quem lê isto (humano ou IA):** este documento foi escrito para dar contexto completo do projeto
> a quem vai ajudar a montar uma apresentação. Ele explica **o que é**, **por que existe**, **como cada
> parte funciona**, **que regras foram adotadas** e **que tecnologias são usadas onde**. As seções 13 a 15
> trazem sugestões de roteiro, perguntas prováveis da banca e limitações conhecidas. Tudo aqui descreve o
> código como está no repositório; nada é prometido além disso.

---

## 1. O que é o projeto

**SatusBlockchain** é uma **blockchain distribuída, educacional e simplificada**, desenvolvida para a
disciplina de **Sistemas Distribuídos**. Vários **nós** independentes (processos, normalmente containers
Docker) mantêm, cada um, a sua cópia de uma cadeia de blocos, conversam entre si por **HTTP/REST** e
chegam a um acordo sobre qual é a cadeia "verdadeira".

**O que ele NÃO é:** não é uma criptomoeda nem uma blockchain de produção. Não há carteiras, assinaturas
digitais, saldos, nem prevenção de gasto duplo. Essas coisas são criptografia e economia — o foco aqui são
os **conceitos de sistemas distribuídos** que a blockchain torna visíveis.

**A tese do projeto:** uma blockchain é um ótimo laboratório de sistemas distribuídos, porque obriga a
resolver, de forma concreta e demonstrável, os problemas clássicos:

| Conceito de Sistemas Distribuídos | Onde aparece no projeto |
|---|---|
| Replicação de estado | cada nó guarda a sua cópia da cadeia |
| Propagação de dados | push de blocos, gossip de transações |
| Consenso | regra da cadeia mais longa (*longest chain rule*) |
| Consistência eventual | nós divergem por um tempo e convergem depois |
| Tolerância a falhas | nó offline não derruba a rede; recupera-se por `sync` |
| Partição de rede e fork | laboratório de falhas: isolar um nó, minerar dos dois lados, reconverger |
| Membership (quem faz parte da rede) | nós entram e saem com o sistema rodando; detecção de falha |
| Observabilidade | logs, métricas e traces distribuídos |
| Segurança sem confiança no remetente | cada nó recalcula e valida tudo; bloco adulterado é recusado |

---

## 2. Conceitos de blockchain usados (em linguagem simples)

- **Bloco:** um lote de transações + metadados. Tem `Index`, `Timestamp`, `Transactions`, `PreviousHash`,
  `Nonce` e `Hash`.
- **Hash (SHA-256):** a "impressão digital" do bloco, calculada sobre o conteúdo. Mudou um byte, mudou o hash.
- **Encadeamento:** cada bloco guarda o hash do anterior (`PreviousHash`). Adulterar um bloco antigo muda o
  hash dele e quebra o elo com todos os seguintes — por isso adulteração é **detectável**.
- **Genesis:** o primeiro bloco (índice 0), criado automaticamente em todo nó. Aqui ele tem timestamp **fixo**
  (2026-01-01), então todos os nós com a mesma dificuldade geram **exatamente o mesmo genesis**. Isso é a
  "certidão de nascimento" da rede: nós com genesis diferente são de "universos" diferentes e nunca convergem.
- **Proof of Work (PoW):** para um bloco ser aceito, o hash dele precisa **começar com N zeros hexadecimais**
  (N = dificuldade). Para achar isso, o minerador varia o `Nonce` até acertar — trabalho caro de fazer, barato
  de verificar (um único hash).
- **Mempool:** fila de transações pendentes de um nó, esperando entrar num bloco.
- **Fork:** duas versões concorrentes da cadeia (por exemplo, dois nós mineram ao mesmo tempo, ou um nó fica
  isolado e minera sozinho).
- **Longest chain rule (consenso):** vence a cadeia **válida mais longa**. Os blocos da perdedora viram
  **órfãos**, e as transações deles voltam para a mempool.
- **Finalidade probabilística:** uma confirmação pode ser desfeita por um reorg; quanto mais blocos por cima,
  mais segura ela é.

---

## 3. Visão geral da arquitetura

O sistema tem **dois programas** e uma camada de infraestrutura:

```
                         ┌──────────────────────────────────────────────┐
  navegador ───────────► │  CONSOLE (container, 127.0.0.1:3001)         │
                         │  Gateway ASP.NET  +  Interface React (SPA)   │
                         └───────┬───────────────────────────┬──────────┘
                                 │ HTTP (API dos nós)        │ socket do Docker
                                 ▼                           ▼ (criar/parar/remover nós)
        ┌─────────┐   ┌─────────┐   ┌─────────┐   ┌─────────┐
        │  node1  │◄─►│  node2  │◄─►│  node3  │◄─►│ node4.. │   ← NÓS (a rede blockchain)
        │ cadeia  │   │ cadeia  │   │ cadeia  │   │ cadeia  │     cada um com a sua cadeia
        │ mempool │   │ mempool │   │ mempool │   │ mempool │     e a sua mempool, em memória
        └─────────┘   └─────────┘   └─────────┘   └─────────┘
              HTTP/REST entre os nós (push, gossip, pull, anúncio)
              rede Docker "satus-net" — nós se enxergam pelo nome (http://node2:8080)

   Observabilidade (opcional): Prometheus ← /metrics   Jaeger ← traces (OTLP)   Loki ← logs (Alloy) → Grafana
```

**Ideia central:** a distribuição vem da **replicação do mesmo processo**. Um único projeto (o "nó") é executado
N vezes com configurações diferentes. Cada nó é **autônomo**: tem a própria cadeia e a própria mempool, e não
existe coordenador central, banco de dados compartilhado nem líder fixo.

**O console é um observador externo**, não faz parte da rede: ele consulta e opera os nós pela mesma API
pública que qualquer cliente (curl, Postman) usaria, e **não exigiu mudar a API dos nós**.

### Estrutura do repositório

```
SatusBlockchain/
├── src/
│   ├── SatusBlockchain.Node/       # o nó (a blockchain em si)
│   │   ├── Core/                   #   domínio: Block, Transaction, Blockchain, Mempool, Hasher, ProofOfWork
│   │   ├── Networking/             #   PeerClient, PeerRegistry, MembershipService, ChainSynchronizer
│   │   ├── Api/                    #   endpoints REST (Minimal APIs), um arquivo por recurso
│   │   ├── Lab/                    #   laboratório de falhas (partição, lentidão)
│   │   └── Observability/          #   NodeTelemetry (métricas e spans)
│   └── SatusBlockchain.Gateway/    # o console: API agregadora + hospeda a interface
├── ui/                             # interface do console (React + TypeScript + Vite)
├── tests/                          # SatusBlockchain.Node.Tests e SatusBlockchain.Gateway.Tests
├── scripts/                        # add-node.ps1 / remove-node.ps1
├── observability/                  # configs de Prometheus, Grafana, Loki, Alloy
├── postman/                        # coleção de demonstração
├── docker-compose.yml              # os 3 nós
├── docker-compose.observability.yml# overlay: Jaeger, Prometheus, Grafana, Loki, Alloy
└── docker-compose.console.yml      # overlay: o console web
```

---

## 4. O nó — como funciona cada parte

### 4.1 Dados (`Core/Block.cs`, `Core/Transaction.cs`)

- `Transaction` é um **record**: `From`, `To`, `Amount` (decimal). Como é record, **igualdade é por valor** —
  duas transações com os mesmos três campos são "a mesma". Isso é usado na deduplicação.
- `Block` tem `Index`, `Timestamp`, `Transactions`, `PreviousHash` (imutáveis depois de criados) e `Nonce` e
  `Hash` (mutáveis, porque são o resultado da mineração).

### 4.2 Hash canônico (`Core/Hasher.cs`)

O hash é o **SHA-256** de uma **serialização canônica**: JSON com os campos em ordem fixa
`{Index, Timestamp (em milissegundos Unix), Transactions, PreviousHash, Nonce}`. O campo `Hash` **nunca**
entra no próprio cálculo. A canonicalização é essencial: sem ela, dois nós poderiam serializar o mesmo bloco
de formas diferentes (ordem dos campos, formato de data) e obter hashes diferentes, quebrando o consenso.
O resultado é hexadecimal minúsculo (64 caracteres).

### 4.3 Proof of Work (`Core/ProofOfWork.cs`)

- **Dificuldade** = número de zeros hexadecimais iniciais exigidos. Padrão **4** (≈ 65.536 tentativas,
  ~1 segundo). Máximo 64 (o hash só tem 64 caracteres; mais que isso seria impossível).
- **Minerar:** incrementa o `Nonce` de 0 em diante, recalcula o hash, até satisfazer a dificuldade.
- **Verificar:** recalcula **um** hash e confere o prefixo — custo constante, independente da dificuldade.
- O genesis **também** passa pelo PoW (como no Bitcoin).

### 4.4 A cadeia (`Core/Blockchain.cs`)

A classe central. Guarda uma `List<Block>` **em memória**, protegida por `lock` (várias requisições HTTP
mexem nela ao mesmo tempo). Operações principais:

- **`AddBlock`** — aceita um bloco recebido de outro nó **somente se** passar nas 4 regras: índice certo,
  `PreviousHash` igual ao hash do topo, **hash recalculado igual ao declarado** (detecta adulteração) e PoW
  válido. Nunca confia no remetente.
- **`MineBlock`** — monta um bloco candidato sob o `lock`, **minera fora do `lock`** (porque leva segundos e
  não deve travar o nó) e depois **revalida sob o `lock`**. Se outro bloco entrou na cadeia nesse intervalo,
  a mineração é descartada (resposta 409) e a transação continua pendente.
- **`IsValid`** — revalida a cadeia inteira: índices, elos, hashes e PoW. É a mesma regra (`IsValidChain`)
  usada para julgar cadeias vindas de outros nós.
- **`TryReplaceChain`** — o **reorg**. Adota a cadeia de um peer se: tem o **mesmo genesis**, é **válida**
  (validada localmente, sem confiar no peer) e é **estritamente mais longa**. Empate mantém a local
  (regra *first seen*). Devolve as **transações órfãs** (as dos blocos descartados que não estão na cadeia
  adotada).
- **`Inspect`** — explica o que `AddBlock` faria com um bloco, **sem anexá-lo** (hash recalculado, PoW,
  se já está na cadeia, se encaixa no topo, e um veredito em português). Alimenta a demonstração de adulteração.
- **`ContainsTransaction`** — a transação já está confirmada em algum bloco?

### 4.5 Mempool (`Core/Mempool.cs`)

Fila local de transações pendentes, também com `lock`. **Deduplica por valor** (a mesma transação não entra
duas vezes). Ao aceitar um bloco, o nó remove da mempool as transações que entraram nele. **A mempool é local
a cada nó** — não existe mempool compartilhada em nenhuma blockchain real; o que existe é o *repasse* (gossip).

### 4.6 Configuração (`NodeOptions.cs`)

Variáveis de ambiente, **validadas na inicialização** (valor inválido derruba o nó na hora, com mensagem clara):

| Variável | Papel |
|---|---|
| `NODE_ID` | nome do nó (logs, respostas, métricas) |
| `DIFFICULTY` | zeros do PoW (0–64, padrão 4) |
| `PEERS` | lista de URLs dos peers **iniciais** (as "sementes") |
| `ADVERTISE_URL` | (opcional) o endereço pelo qual os outros chamam este nó; habilita entrar/sair da rede sozinho |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | (opcional) habilita export de traces para o Jaeger |

### 4.7 API REST do nó (Minimal APIs)

| Endpoint | O que faz |
|---|---|
| `GET /` | identidade, dificuldade, **genesis**, tamanho e validade da cadeia (também é o *healthcheck*) |
| `GET /chain` · `GET /chain/validate` | cadeia completa · valida encadeamento, hashes e PoW |
| `POST /transactions` | valida (From, To preenchidos, Amount > 0), põe na mempool e **propaga aos peers** |
| `GET /transactions/pending` | mempool local |
| `POST /transactions/receive` | recebe transação propagada (200 / 409 se já pendente ou já confirmada) |
| `POST /blocks/mine` | minera com a mempool e **propaga o bloco** aos peers |
| `POST /blocks/receive` | recebe bloco propagado (200 aceito / 409 se não encaixa) |
| `POST /blocks/inspect` | o nó explica o que faria com o bloco, sem anexá-lo |
| `POST /sync` | puxa a cadeia dos peers e adota a válida mais longa |
| `GET /peers` · `POST /peers` · `DELETE /peers?url=` | lista atual de peers · um nó se anuncia · um nó se despede |
| `GET/PUT/DELETE /lab…` | laboratório de falhas (partição e lentidão) |
| `GET /metrics` | métricas no formato Prometheus |

Escolha de status HTTP com significado: **409** = "bem formado, mas não encaixa no estado atual" (bloco
duplicado, órfão ou adulterado; transação repetida); **400** = entrada inválida.

---

## 5. Como os nós conversam (os mecanismos de rede)

Todo tráfego entre nós sai do **`PeerClient`** (um `HttpClient` com **timeout de 2 s**). Princípio de projeto:
**falha de peer vira resultado, nunca exceção** — um peer offline, mudo ou que recuse o payload **não derruba**
a operação local (minerar, aceitar uma transação).

### 5.1 Push — propagação de blocos

`POST /blocks/mine` minera e, **antes de responder**, envia o bloco a todos os peers **em paralelo**
(`POST /blocks/receive`). Esperar o push é uma decisão de projeto: quando a resposta chega, os peers já têm o
bloco — isso torna a demonstração e os testes determinísticos. Cada peer valida com `AddBlock`.

### 5.2 Gossip — propagação de transações

`POST /transactions` grava na mempool local **e** repassa aos peers. Resultado: você posta a transação em
**um** nó e **qualquer** nó pode minerá-la (é o fluxo de uma carteira real, que fala com um nó e conta com a
rede). Deduplicação em duas camadas: a mempool recusa repetida; e a recepção checa se a transação já está
confirmada em bloco (o bloco pode chegar antes da transação).

### 5.3 Pull — sincronização (`POST /sync`)

O push só leva blocos **novos**; quem ficou offline perdeu a **história**. O `sync` faz o nó **perguntar**:
lê `GET /chain` de cada peer em paralelo e aplica a *longest chain rule* (`TryReplaceChain`). Responde sempre
**200** com um relatório por peer (`Adopted` / `Kept` / `Invalid` / `Unreachable`) — peer ausente é situação
normal numa rede parcial. Transações órfãs voltam à mempool. **O sync é manual de propósito** (não há
processo em segundo plano consultando peers): deixa explícito quem decide sincronizar e mantém a demo
determinística. A única exceção é o nó que *entra* na rede, que sincroniza uma vez ao subir.

### 5.4 Membership dinâmica — nós entrando e saindo (etapa 10)

Antes, a lista de peers era fixa (variável `PEERS`). Agora ela é **estado vivo** de cada nó (`PeerRegistry`,
até 32 peers, com `lock`), e **local a cada nó** (como a mempool, sem consenso):

- **Sementes** = os peers iniciais de `PEERS`. **Nunca saem da lista por falha** (são o caminho de volta
  depois de uma queda ou partição).
- **Entrar:** um nó com `ADVERTISE_URL` e **uma** semente em `PEERS` faz, ao subir: (1) anuncia-se à semente
  (`POST /peers`); (2) a resposta traz os peers que ela conhece, e o nó se anuncia a esses também (busca em
  largura — basta **um** endereço para achar a rede toda); (3) **sincroniza a cadeia**.
- **Handshake (a defesa na porta):** antes de registrar um nó, quem recebe o anúncio **chama o endereço
  de volta** (`GET /`) e confere: ele responde? (senão **502**); tem o **mesmo genesis**? (senão **409** —
  outra dificuldade = outro universo); tem outro `NODE_ID`? (senão **409**); URL válida? (senão **400**);
  lista cheia? (**503**). Sem handshake, qualquer um registraria um endereço qualquer e o nó passaria a
  disparar blocos para lá.
- **Sair avisando:** no desligamento gracioso (`docker stop` → SIGTERM), o nó faz `DELETE /peers` em todos e
  sai das listas na hora.
- **Cair sem avisar** (`docker kill`): ninguém é avisado. Cada chamada sem resposta (recusa, DNS ou timeout)
  conta contra o peer; na **3ª seguida**, ele sai da lista (uma resposta qualquer — até um 409 — zera a conta).
  Sementes não saem. Não há *heartbeat*: a detecção usa as próprias chamadas de push/gossip/sync.
- **Re-propagação:** com listas que mudam, nem todo nó conhece todos. Por isso, quem **aceita** um bloco ou
  transação de um peer o **repassa** aos seus peers. Não vira laço: quem já tem responde 409 e não repassa.
  O repasse **não é aguardado** — se fosse, a lentidão de um peer distante pareceria falha do vizinho e o
  tiraria da lista por engano. (Foi uma lição aprendida com um teste que falhou.)

### 5.5 Consenso e reorg

**Longest chain rule:** uma cadeia recebida substitui a local se for **válida** e **estritamente mais longa**.
Empate → mantém a local. Ao substituir, os blocos descartados são **órfãos**; as transações deles **voltam
para a mempool**, exceto as que a cadeia adotada já contém (evita duplicidade) — como no Bitcoin.

---

## 6. Laboratório de falhas e fork (etapa 12)

Para **demonstrar** fork e convergência sem derrubar nada, cada nó aceita **falhas injetadas** que valem
**só para o tráfego entre nós**:

- **Partição simulada** (`PUT /lab/partition {"group":"B"}`): o nó passa a ter um grupo; só aceita peers do
  mesmo grupo, os outros levam **503**.
  - *Como funciona:* o `PeerClient` marca toda requisição de nó para nó com o cabeçalho `X-Satus-Peer` (e
    `X-Satus-Partition` com o grupo); na entrada, um *middleware* recusa peers de outro grupo. Requisições
    **sem** `X-Satus-Peer` (console, curl, Postman) **passam direto** — por isso o operador enxerga e opera
    os **dois lados** da partição, e dá para minerar em cada um.
  - *Por que não derrubar o nó ou cortar a rede do Docker:* derrubar apaga a memória (o nó volta no genesis);
    desconectar da rede cortaria também o console.
- **Nó lento** (`PUT /lab/delay {"ms":3000}`): o nó demora para responder aos peers. Abaixo de 2 s só atrasa;
  **acima de 2 s** (o timeout do `PeerClient`), para os peers ele é indistinguível de um nó caído — o push
  para ele se perde e ele fica atrasado até sincronizar.
- **Bloco adulterado:** troca-se o conteúdo de um bloco **mantendo o hash declarado**. `POST /blocks/inspect`
  mostra o hash **recalculado pelo próprio nó** divergindo; entregue a outro nó como um peer faria, o bloco é
  **recusado (409)**. A defesa não é confiar no remetente: é **cada nó recalcular**.

**Roteiro de fork em 6 passos** (guiado na interface): (1) isolar um nó; (2) minerar no lado isolado;
(3) minerar duas vezes no lado principal; (4) observar o fork; (5) curar a partição — nada converge sozinho;
(6) sincronizar todos — o ramo mais curto vira órfão e a transação dele volta à mempool.

---

## 7. O console web (etapa 11) e o explorador da cadeia

### 7.1 Gateway (`src/SatusBlockchain.Gateway`, ASP.NET)

- **Hospeda a interface** compilada e a API `/api/*` na **mesma origem** → sem CORS. Roda **dentro da rede do
  Docker**, então alcança `http://nodeN:8080` (nomes que o navegador não resolveria).
- **Snapshot da rede** (`GET /api/network`): a UI faz **um** pedido a cada 2 s; o gateway consulta todos os
  nós em paralelo (timeout de 1,5 s por nó) e monta o retrato. Um nó fora do ar **nunca derruba** o snapshot:
  vira `unreachable`.
- **Descoberta de nós:** (a) a plataforma (containers Docker do projeto, inclusive os parados) e (b) uma lista
  inicial + **busca em largura** pelas listas de peers.
- **Classificador de status** (função pura, `StatusClassifier`): a **cadeia líder** é a válida mais longa.
  Cada nó é: **Em dia**, **Atrasado** (o topo dele está na líder — só perdeu blocos), **Fork** (tem ramo
  próprio que não está na líder), **Inválido**, **Outra rede** (genesis diferente), **Fora do ar** ou
  **Parado**. A regra mora num só lugar; a interface só exibe.
- **Operações repassadas:** transação, minerar, sync etc. são repassadas ao nó **sem traduzir** — status e
  corpo voltam como o nó respondeu (o 409 "a cadeia mudou durante a mineração" chega à tela como está).
- **Ciclo de vida pelo Docker:** cria, para (com despedida), derruba (queda), religa e remove nós falando com
  o Docker Engine pelo **socket Unix**, sem biblioteca extra. Ao criar um nó: escolhe o próximo `nodeN`,
  usa como semente um nó **de pé e em dia**, e **copia a dificuldade** de um nó vivo (com outra, o genesis
  seria diferente e o handshake recusaria). Imagem e rede também são copiadas de um nó existente.
- **Segurança:** montar o socket do Docker equivale a poder de root. Por isso o console fica num *overlay*
  separado, publicado só em `127.0.0.1`, só age sobre containers `nodeN` do projeto, e **não aceita imagem,
  comando ou volume vindos da requisição**. Não há autenticação (escopo didático, máquina local).

### 7.2 Interface (`ui/`, React)

Três abas:

1. **Rede** — grafo de nós (SVG desenhado à mão): cada nó mostra blocos, mempool e estado em **cor + símbolo +
   texto** (acessível). Linha cheia = os dois se listam; **tracejada** = só um lista o outro (cada nó tem a
   sua visão da rede); **vermelha pontilhada** = lados diferentes de uma partição; borda tracejada = nó
   dinâmico; selo do lado da partição; ⏱ para nó lento. Um **"pulso"** aparece quando a cadeia de um nó cresce
   — a propagação passando de nó em nó. Painel do nó: transação, minerar, sincronizar, parar/derrubar/religar/
   remover. Registro de operações em português com o que cada nó respondeu.
2. **Explorador da cadeia** — blocos encadeados com os **zeros do PoW destacados** e o elo conferido; clicar
   num bloco mostra as transações e pede ao **nó** que o verifique. **Comparar dois nós** alinha as cadeias
   por índice e marca o **ponto de fork** (ou diz que um é só prefixo do outro, ou seja, só atraso).
3. **Laboratório de falhas** — o roteiro de fork guiado, partição livre (lados A/B), nó lento e adulteração.

---

## 8. Observabilidade (etapa 9)

Os **três pilares**, cada um com seu caminho (o OpenTelemetry só instrumenta; os destinos são trocáveis):

| Pilar | Instrumentação | Caminho | Destino |
|---|---|---|---|
| **Logs** | `ILogger` | console do container → Docker → coletor | **Alloy → Loki → Grafana** |
| **Métricas** | `Meter` (BCL) + instrumentação ASP.NET/HttpClient | `GET /metrics` (pull) | **Prometheus → Grafana** |
| **Traces** | `ActivitySource` + spans automáticos | OTLP (push), **só** se `OTEL_EXPORTER_OTLP_ENDPOINT` existir | **Jaeger** |

- **Métricas próprias:** `satus.blocks.mined`, `satus.blocks.received{outcome}`, `satus.peer.push{kind,outcome}`,
  `satus.peer.pull{outcome}`, `satus.peer.membership{change}`, `satus.sync{outcome}`, `satus.txs.received`,
  e os gauges `satus.chain.length`, `satus.mempool.size`, `satus.peers.count`.
- **Regra de cardinalidade:** tags só de **domínio fechado** (`outcome`, `kind`, `change`) — **nunca** hash,
  índice, timestamp ou URL de peer, porque cada valor distinto viraria uma série permanente no Prometheus.
- **Trace que atravessa nós:** o `HttpClient` injeta o `traceparent` e o ASP.NET o relê; assim um
  `POST /blocks/mine` no node1 vira a **raiz de um trace** cujos filhos são os `POST /blocks/receive` do node2
  e do node3, com o span `mine` (o PoW) no meio. É a "prova visual" de sistemas distribuídos.
- **Descoberta dinâmica:** o Prometheus descobre os nós **pelo Docker**, então um nó criado em execução passa
  a ser raspado sozinho em até 5 s. O dashboard do Grafana mostra cadeia, mempool, push por desfecho, latência
  HTTP p95, **peers por nó** e **entradas/saídas da rede**.

---

## 9. Infraestrutura e execução

- **Docker:** uma única imagem do nó, em **Dockerfile multi-stage** (`sdk:10.0` compila e publica;
  `aspnet:10.0` só executa; usuário não-root; `HEALTHCHECK` em `GET /`, que só responde quando o nó está
  de pé e já recalcula a validade da cadeia — então `docker compose ps` mostra "healthy" quando o nó está
  realmente pronto, e não apenas com a porta aberta).
- **docker-compose.yml:** `node1`, `node2`, `node3` (portas do host 8080, 8081, 8082), rede `satus-net`
  (nós se enxergam pelo nome). Dificuldade pela variável `NODE_DIFFICULTY` (padrão 4 ≈ 1 s por bloco; use 2
  para mineração instantânea). **Os três precisam da mesma dificuldade** — senão os genesis diferem e nada
  se propaga. O compose também traz um **molde `node-extra`** (profile `extra`, não sobe sozinho) usado para
  criar nós em execução.
- **Overlays** (`-f` múltiplo, o compose base não muda): `docker-compose.observability.yml` (Jaeger,
  Prometheus, Grafana, Loki, Alloy) e `docker-compose.console.yml` (o console, com o socket do Docker).
- **Scripts:** `add-node.ps1` e `remove-node.ps1` fazem pela linha de comando o que o console faz com cliques
  (`-Crash` simula queda).
- **Sem Docker:** dá para rodar nós com `dotnet run` (portas diferentes, `PEERS` apontando uns para os outros)
  e o gateway com `GATEWAY_NODES`.

---

## 10. Tecnologias — o que é usado em cada parte

| Parte | Tecnologia | Por quê |
|---|---|---|
| Nó (backend) | **C# / .NET 10**, **ASP.NET Core Minimal APIs** | pouca cerimônia, código explicável no quadro |
| Hash | **SHA-256** (`System.Security.Cryptography`) | padrão da indústria, na biblioteca base |
| Serialização | **System.Text.Json** (camelCase) | biblioteca base; mesmo formato na API e entre nós |
| Comunicação entre nós | **HttpClient / REST** | sem broker nem gRPC: menos dependências para explicar |
| Concorrência | `lock` (`System.Threading.Lock`) | simples e suficiente no volume didático |
| Estado | **em memória** | sem banco; reiniciar um nó apaga a cadeia — ajuda a demonstrar recuperação |
| Empacotamento | **Docker**, Dockerfile multi-stage, **Docker Compose** (com overlays) | rede de nós reproduzível |
| Métricas | **OpenTelemetry** (.NET) → **Prometheus** → **Grafana** | pull, formato padrão |
| Traces | OpenTelemetry → **OTLP** → **Jaeger v2** | trace distribuído entre nós |
| Logs | `ILogger` → **Alloy** → **Loki** → **Grafana** | coleta pelo socket do Docker |
| Gateway do console | **ASP.NET Core** (.NET 10) | mesma linguagem; fala com o Docker pelo socket Unix, sem pacote extra |
| Interface | **React 19**, **TypeScript**, **Vite**, **TanStack Query** | polling de 2 s com cache; grafo em SVG próprio |
| Testes do .NET | **xUnit**; stubs de `HttpMessageHandler` escritos à mão (sem biblioteca de mock) | simplicidade |
| Testes da interface | **Vitest** + Testing Library; ESLint e TypeScript estrito | |
| Teste manual | **Postman** (coleção com testes automáticos em cada request) | roteiro de demonstração |

---

## 11. Regras e decisões de projeto (o "porquê" de cada escolha)

**Princípios gerais**
1. **Simplicidade didática:** código explícito e explicável. Sem Clean Architecture, CQRS, MediatR, message
   broker ou microsserviços adicionais.
2. **Estado em memória:** sem banco de dados. É uma escolha, não uma falta.
3. **Nunca confiar no remetente:** todo bloco e toda cadeia recebidos são **revalidados localmente**
   (hash recalculado + PoW + encaixe). Não existe autenticação entre nós; a defesa é a validação.
4. **Falha de peer é resultado, não exceção:** a operação local nunca depende de um peer estar no ar.
5. **Nada roda sozinho em segundo plano:** a mineração é sob demanda (`POST /blocks/mine`), o sync é manual.
   Isso dá controle didático e permite provocar forks de forma determinística.
6. **Configuração validada na subida:** erro de configuração aparece na inicialização, não como um comportamento
   estranho depois.

**Regras de negócio da blockchain**
- Transação válida: `From` e `To` preenchidos e `Amount > 0`. Sem assinatura, sem saldo.
- Bloco aceito somente se: índice correto, elo (`PreviousHash`) correto, hash recalculado igual ao declarado e
  PoW válido para a dificuldade do nó.
- Cadeia substituída somente se: mesmo genesis, válida e **estritamente** mais longa. Empate → fica a local.
- Órfãs voltam para a mempool, exceto as já presentes na cadeia adotada.
- **Genesis determinístico** (timestamp fixo) para que nós com a mesma dificuldade sejam compatíveis.
- A mempool é **local**; a replicação dela é por gossip, sem consenso.

**Decisões que valem destacar (e as alternativas descartadas)**
- *Partição simulada na aplicação* em vez de derrubar nó ou desconectar da rede Docker (que cortaria o console).
- *Verificação de bloco feita pelo nó* (`/blocks/inspect`), e não no navegador — uma segunda implementação do hash
  canônico seria uma segunda "verdade" sobre o que é um bloco válido.
- *Console com gateway próprio e polling* em vez de chamar os nós direto do navegador (exigiria CORS em cada nó e
  não funcionaria no Docker) ou de eventos em tempo real (mudaria a API dos nós).
- *Sem registro central de nós* (Consul/etcd) nem descoberta pelo DNS do Docker: seria ponto único de falha ou
  amarraria o projeto à plataforma. A semente só é usada para **entrar**.
- *Sem heartbeat periódico:* a detecção de falha reaproveita as chamadas que já existem.

---

## 12. Testes

Total de **291 testes automatizados** (conforme a última execução), em três suítes:

| Suíte | Testes | O que cobre |
|---|---|---|
| Nó (xUnit) | **184** | domínio (bloco, hash, PoW, cadeia, mempool), configuração, `PeerClient` contra servidor de mentira, `PeerRegistry`, `MembershipService`, telemetria, laboratório, inspeção de bloco; **integração subindo processos reais do nó** em portas livres: propagação, gossip, sync, fork, membership, partição, lentidão, adulteração, métricas |
| Gateway (xUnit) | **66** | classificação de status, snapshot, descoberta, regras de criar/remover nós, lado Docker contra uma Engine API de mentira; integração com **gateway e nós reais** em processos |
| Interface (Vitest) | **41** | derivação de status e arestas, comparação de cadeias, mensagens, formulários, roteiro de fork, explorador |

Detalhe metodológico: os testes de integração sobem o **executável real** do nó (um processo por teste), com o
ambiente definido explicitamente — testam o sistema como ele roda em produção, não uma simulação.

---

## 13. Sugestão de roteiro de apresentação (≈ 12–15 min)

1. **Problema e tese (1 min):** "como várias máquinas, sem coordenador, concordam sobre um estado?" — a
   blockchain como laboratório de Sistemas Distribuídos. Não é criptomoeda.
2. **O bloco e a corrente (2 min):** hash, `PreviousHash`, PoW. *Demo:* minerar um bloco (~1 s) e mostrar os
   zeros no hash no explorador.
3. **Três nós, uma rede (2 min):** grafo na aba Rede. Postar uma transação num nó → aparece nos três
   (gossip) → minerar → o "pulso" percorre os nós (push).
4. **Tolerância a falha (2 min):** derrubar/parar o node3, minerar, religar → ele volta **atrasado** →
   Sincronizar → recupera (pull).
5. **O grande momento: fork e convergência (3–4 min):** laboratório, roteiro em 6 passos. Isolar, minerar dos
   dois lados, **ver o fork** no grafo e no explorador (ponto de fork), curar, sincronizar: o ramo perdedor vira
   órfão e a transação volta para a mempool. Explicar **consistência eventual** e **finalidade probabilística**.
6. **Segurança sem confiança (1 min):** adulterar um bloco → o nó mostra o hash recalculado diferente → 409.
7. **Rede elástica (1–2 min):** "+ Adicionar nó" → entra, descobre os outros, sincroniza sozinho; derrubar
   sem avisar → sai das listas após 3 falhas.
8. **Observabilidade (1 min):** Grafana (peers por nó, push por desfecho) e **um trace no Jaeger** cruzando os
   três nós.
9. **Fechamento (30 s):** o que cada conceito de SD aparece onde (tabela da seção 1) e limitações assumidas.

*Dicas para os slides:* usar dificuldade 2 para as demos rápidas; um diagrama por mecanismo (push, gossip,
pull, membership, partição); mostrar sempre o **estado antes/depois**.

---

## 14. Perguntas prováveis da banca (com respostas)

- **"Isso é uma criptomoeda?"** Não. Sem carteiras, assinaturas ou saldos — de propósito. O foco é replicação,
  consenso e tolerância a falhas.
- **"Como vocês evitam que um nó mal-intencionado injete um bloco falso?"** Cada nó **recalcula o hash e
  confere o PoW** de tudo que recebe; não confia no remetente. Demonstrável no laboratório (bloco adulterado → 409).
- **"E se dois nós minerarem ao mesmo tempo?"** Vira um fork. Resolve-se pela cadeia **válida mais longa**; no
  empate, cada nó mantém a sua até alguém minerar o próximo bloco. Os blocos perdedores viram órfãos.
- **"Por que o sync é manual?"** Decisão didática: deixa claro quem decide, e torna demos e testes
  determinísticos. Um `BackgroundService` seria o próximo passo natural.
- **"O que acontece com as transações do bloco órfão?"** Voltam para a mempool (exceto as que a cadeia vencedora
  já contém) — como no Bitcoin.
- **"Como um nó novo descobre os outros?"** Conhece uma **semente**, anuncia-se a ela, recebe a lista de peers
  dela e se anuncia a eles (busca em largura). Antes de aceitar, cada nó faz um **handshake** (chama o endereço
  de volta e confere o genesis).
- **"Como vocês detectam que um nó caiu?"** Sem heartbeat: cada chamada sem resposta conta; na 3ª seguida, o nó
  dinâmico sai da lista. Sementes nunca saem por falha.
- **"Por que PoW com dificuldade tão baixa?"** Para a demonstração: ~1 s por bloco permite acompanhar a
  propagação. É configurável (0–64).
- **"Há ponto único de falha?"** Não na rede: cada nó é autônomo. O **console** é uma conveniência de operação e
  não participa do consenso.
- **"Por que estado em memória?"** Simplicidade e porque ajuda a demonstrar recuperação: reiniciar um nó apaga
  a cadeia dele, e o `sync` a restaura.
- **"Escala?"** Não é o objetivo. Cada nó aceita até 32 peers; o repasse é "inunda quem aceita" (*flooding*
  com deduplicação). Para escala real, seria preciso gossip com *fanout* e persistência.

---

## 15. Limitações conhecidas (seja transparente na apresentação)

- **Sem assinaturas, saldos ou prevenção de gasto duplo; sem persistência; sem TLS/autenticação** entre nós ou
  no console (o console fica restrito a `127.0.0.1`). Tudo isso está fora de escopo, deliberadamente.
- **Identidade de transação = valor** (`From`/`To`/`Amount`): não existe id nem nonce de transação, então duas
  transferências idênticas são tratadas como a mesma na mempool.
- **Lacunas pequenas conhecidas:** `POST /transactions` (cliente) não consulta se a transação já está confirmada
  (o recebimento por gossip consulta); e o `/sync` repõe órfãs na mempool, mas **não remove** da mempool as
  transações que a cadeia adotada já confirmou. Em cenários raros isso pode levar a uma transação minerada duas vezes.
- **PoW síncrono sem cancelamento** (uma dificuldade muito alta trava o nó) e `Hash`/`Nonce` mutáveis em memória.
- **Detecção de falha sem heartbeat:** um nó dinâmico morto só é percebido quando alguém tenta falar com ele.
- **A validação do fluxo com Docker** (criar/parar/derrubar nós pelo console, descoberta no Prometheus) deve ser
  conferida na máquina da apresentação antes do dia; os testes automatizados cobrem esses fluxos com Docker
  simulado e com processos reais, mas não com o Docker de verdade.

---

## 16. Glossário rápido

- **Nó:** um processo/container que roda o programa e guarda uma cópia da cadeia.
- **Peer:** outro nó com quem um nó conversa. **Semente:** peer inicial conhecido de antemão.
- **Genesis:** primeiro bloco; igual em toda a rede (mesma dificuldade).
- **Dificuldade:** zeros hexadecimais exigidos no hash. **Nonce:** número variado até o hash servir.
- **Mempool:** fila local de transações pendentes.
- **Push / Gossip / Pull:** enviar blocos / repassar transações / buscar a cadeia dos peers.
- **Fork:** duas versões concorrentes da cadeia. **Órfão:** bloco descartado numa troca de cadeia.
- **Reorg:** substituição da cadeia local por uma mais longa.
- **Partição:** divisão da rede em grupos que não se comunicam. **Handshake:** verificação ao aceitar um nó novo.
- **Consistência eventual:** os nós podem divergir por um tempo, mas convergem se houver tempo e comunicação.
- **Snapshot:** o retrato da rede inteira que o gateway monta a cada 2 s para a interface.

---

## 17. Números úteis para os slides

- Dificuldade padrão **4** → ≈ **65.536** tentativas → ≈ **1 s** por bloco (dificuldade 2: instantâneo).
- Timeout entre nós: **2 s**. Falhas seguidas para sair da lista: **3**. Máximo de peers por nó: **32**.
- Probe do console: **1,5 s** por nó; atualização da interface: **2 s**.
- Portas: nós **8080 / 8081 / 8082** (+ **8083** para o primeiro nó extra); console **3001**; Grafana **3000**;
  Prometheus **9090**; Jaeger **16686**; Loki **3100**.
- Desenvolvimento em **etapas incrementais (1 a 12)**; **291** testes automatizados; **10** métricas próprias
  `satus.*` (7 contadores e 3 gauges).
