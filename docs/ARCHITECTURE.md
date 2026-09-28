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
│                  └────────────────────────┘   │
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
Docker): é esse endereço que as etapas 6 (push) e 7 (pull/`/sync`) usam para falar nó a nó.
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
│       ├── Networking/        # PeerClient (comunicação de saída com outros nós)
│       ├── Api/               # endpoints Minimal API agrupados por recurso
│       ├── Program.cs         # composição da aplicação (DI, mapeamento de endpoints)
│       └── Dockerfile         # imagem do nó (build multi-stage)
├── tests/
│   └── SatusBlockchain.Node.Tests/
│       ├── Unit/               # domínio isolado (sem rede, sem processo)
│       └── Integration/        # sobe o nó e conversa por HTTP
├── .dockerignore              # contexto de build enxuto (sem bin/obj/docs/tests)
├── docker-compose.yml         # os 3 nós (node1, node2, node3)
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
| `Mempool` | Fila FIFO de transações pendentes | priorização por taxa |
| `PeerClient` | HTTP de saída: broadcast de bloco tolerante a peer offline (push, etapa 6) | consultar a cadeia dos peers (pull, etapa 7); regras de consenso |
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
- O receptor **não** re-propaga (sem cascata): com listas de peers completas, um hop
  alcança os 3 nós.
- Não há emissor autenticado: quem recusa bloco ruim é a validação do domínio
  (hash recalculado + PoW), não a boa vontade do remetente.

### Pull (sincronização) — etapa 7

- `POST /sync` consulta `GET /chain` de cada peer e aplica a **longest chain rule**. É o
  mecanismo que recupera o nó que ficou offline: no push puro ele apenas recusa os blocos
  com 409, porque não encaixam na cadeia dele.

### Peers estáticos

- Lidos no startup da variável `PEERS` (URLs separadas por vírgula) e **validados na
  subida**: entrada que não seja URL http(s) absoluta derruba o nó com mensagem clara.
  Espaços e barra final são normalizados e repetições descartadas
  (`NodeOptions.ParsePeers`).
- Pré-requisito do push: **mesma dificuldade** em todos os nós — o genesis é minerado na
  dificuldade do nó, então dificuldades diferentes geram cadeias incompatíveis.

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

`Blockchain` e `Mempool` são protegidos por `lock`. A API é inerentemente concorrente
(ASP.NET), e blocos podem chegar por dois caminhos simultâneos (mineração local e
recebimento via push). Locks simples são suficientes no volume didático do projeto.

## 9. Configuração por nó

| Variável | Exemplo | Descrição |
|----------|---------|-----------|
| `NODE_ID` | `node1` | Identificador amigável (logs e respostas) |
| `PEERS` | `http://node2:8080,http://node3:8080` | Lista estática de peers (destino do push), validada na subida |
| `DIFFICULTY` | `4` | Zeros hexadecimais exigidos no PoW |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Porta HTTP do nó (definida no `Dockerfile`) |

No `docker-compose.yml`, a dificuldade dos três containers vem de `NODE_DIFFICULTY`
(padrão 2 = mineração em milissegundos; use 4 para mostrar o custo do PoW). O nome é
exclusivo do compose de propósito: um `DIFFICULTY` deixado no shell (usado pelo
`dotnet run`) não pode mais alterar os containers sem querer.

## 10. Alternativas consideradas e descartadas

| Alternativa | Motivo do descarte |
|-------------|--------------------|
| Controllers MVC | Minimal APIs têm menos cerimônia com a mesma clareza |
| Mineração em background | Remove o controle didático; dificulta reproduzir forks |
| Descoberta dinâmica de peers / gossip | Complexidade sem ganho proporcional para 3 nós |
| Assinatura digital de transações | Escopo de criptomoeda, não de sistemas distribuídos |
| Persistência (SQLite/arquivo) | Estado em memória já atende; a falta de persistência facilita a demo de recuperação |
| gRPC / message broker | REST + BCL resolvem; menos dependências para explicar |
