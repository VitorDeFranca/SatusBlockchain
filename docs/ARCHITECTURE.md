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
│       └── Dockerfile         # (etapa 5)
├── tests/
│   └── SatusBlockchain.Node.Tests/
├── docker-compose.yml         # (etapa 5)
├── SatusBlockchain.slnx
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
| `PeerClient` | HTTP de saída: broadcast de bloco, consulta de cadeia dos peers | regras de consenso |
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

- **Push (propagação)**: ao minerar, o nó envia o novo bloco a cada peer
  (`POST /blocks/receive`). Falhas de envio a um peer são toleradas (log + segue) —
  um peer offline não pode derrubar a mineração local.
- **Pull (sincronização)**: `POST /sync` consulta `GET /chain` de cada peer e aplica
  a regra de consenso. Usado na inicialização e na recuperação após falha.
- **Peers estáticos**: lidos no startup da variável `PEERS` (URLs separadas por vírgula).

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
| `PEERS` | `http://node2:8080,http://node3:8080` | Lista estática de peers |
| `DIFFICULTY` | `4` | Zeros hexadecimais exigidos no PoW |
| `ASPNETCORE_URLS` | `http://+:8080` | Porta HTTP do nó |

## 10. Alternativas consideradas e descartadas

| Alternativa | Motivo do descarte |
|-------------|--------------------|
| Controllers MVC | Minimal APIs têm menos cerimônia com a mesma clareza |
| Mineração em background | Remove o controle didático; dificulta reproduzir forks |
| Descoberta dinâmica de peers / gossip | Complexidade sem ganho proporcional para 3 nós |
| Assinatura digital de transações | Escopo de criptomoeda, não de sistemas distribuídos |
| Persistência (SQLite/arquivo) | Estado em memória já atende; a falta de persistência facilita a demo de recuperação |
| gRPC / message broker | REST + BCL resolvem; menos dependências para explicar |
