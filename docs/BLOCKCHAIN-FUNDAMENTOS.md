# Fundamentos de Blockchain — o necessário para entender o SatusBlockchain

> **Público-alvo:** pessoas da área de tecnologia que **nunca estudaram blockchain**.
> **Objetivo:** explicar apenas os conceitos que este projeto usa, na ordem em que eles
> aparecem no código. Não é um guia sobre criptomoedas, investimento ou ecossistemas
> como Ethereum — é o mínimo teórico para ler o SatusBlockchain e dizer "entendi".

---

## 1. O problema que uma blockchain resolve

Imagine que várias pessoas (ou computadores) querem manter **um mesmo histórico de
registros** — por exemplo, quem transferiu o quê para quem — mas:

- **não existe um servidor central** em quem todos confiem;
- qualquer participante pode tentar **trapacear** (alterar o histórico a seu favor);
- participantes podem **ficar offline** e voltar depois.

Como fazer todos concordarem sobre o mesmo histórico, nessas condições?

A resposta da blockchain combina quatro ideias simples, que são a espinha dorsal
deste projeto:

| Ideia | Papel |
|-------|-------|
| **Hash criptográfico** | Cria uma "impressão digital" de qualquer dado |
| **Encadeamento de blocos** | Torna o histórico inviolável: mexer no passado quebra o presente |
| **Proof of Work** | Torna caro (demorado) escrever no histórico, e barato verificar |
| **Consenso** | Regra para todos os nós escolherem o mesmo histórico |

Nas próximas seções, cada ideia é explicada e ligada ao código do projeto.

---

## 2. Hash criptográfico (SHA-256)

Um **hash** é uma função que transforma qualquer dado (um texto, um arquivo, um bloco)
em uma sequência fixa de caracteres. O SatusBlockchain usa o **SHA-256**, que produz
64 caracteres hexadecimais (256 bits). Exemplo real:

```
SHA-256("SatusBlockchain") = 5f4dcc3b5aa765d61d8327deb882cf99... (sempre 64 chars)
```

Quatro propriedades tornam o SHA-256 útil para blockchain:

1. **Determinístico** — o mesmo dado gera sempre o mesmo hash. Qualquer nó pode
   recalcular o hash de um bloco e conferir se bate.
2. **Efeito avalanche** — mudar **um único caractere** do dado muda o hash inteiro,
   de forma imprevisível. `"abc"` e `"abd"` produzem hashes completamente diferentes.
3. **Unidirecional** — dado o hash, é computacionalmente inviável descobrir o dado
   original. Não existe "função inversa".
4. **Resistente a colisões** — é inviável encontrar dois dados diferentes com o
   mesmo hash. O hash funciona como **impressão digital** do dado.

> **No SatusBlockchain:** a classe `Hasher` (pasta `Core/`) calcula o SHA-256 de um
> bloco. Um detalhe importante: antes de hashear, o bloco é serializado em JSON com
> **campos em ordem fixa** (serialização canônica) — caso contrário, dois nós poderiam
> serializar o mesmo bloco em ordens diferentes e obter hashes diferentes.

---

## 3. Bloco e encadeamento

Uma blockchain é uma **lista de blocos**, onde cada bloco guarda um lote de registros
(no nosso caso, transações) e alguns metadados:

```
Block {
  Index:         posição na cadeia (0, 1, 2, ...)
  Timestamp:     quando foi criado
  Transactions:  os registros (ex.: "Alice pagou 10 para Bob")
  PreviousHash:  o hash do BLOCO ANTERIOR   ← a "cola" da cadeia
  Nonce:         número da solução do Proof of Work (seção 5)
  Hash:          o SHA-256 de tudo isso acima
}
```

O campo `PreviousHash` é o coração da ideia. Visualmente:

```
┌───────────────┐      ┌───────────────┐      ┌───────────────┐
│  Bloco 0      │      │  Bloco 1      │      │  Bloco 2      │
│  (genesis)    │      │               │      │               │
│  Hash: a1b2.. │◀─────│ Prev: a1b2..  │◀─────│ Prev: c3d4..  │
│               │      │ Hash: c3d4..  │      │ Hash: e5f6..  │
└───────────────┘      └───────────────┘      └───────────────┘
```

### Por que isso torna o histórico inviolável?

Suponha que alguém altere uma transação dentro do **Bloco 1**. Pelo efeito avalanche:

1. o hash do Bloco 1 muda → `c3d4..` vira outra coisa;
2. mas o Bloco 2 continua apontando para o hash antigo → **o elo quebra**;
3. para "consertar", o atacante teria que recalcular o hash do Bloco 2, e depois do
   Bloco 3, e assim por diante — **todos os blocos seguintes**.

Por isso se diz que a cadeia é **tamper-evident**: não impede a adulteração, mas
torna qualquer adulteração **imediatamente detectável** — basta recalcular os hashes
e conferir os elos. É exatamente isso que o método `IsValid()` faz.

> **No SatusBlockchain:** `Core/Block.cs` define essa estrutura e
> `Core/Blockchain.cs` mantém a lista encadeada e a valida.

### O bloco genesis

Toda cadeia precisa começar em algum lugar. O **bloco genesis** é o bloco de índice 0,
criado automaticamente quando um nó inicia. Ele é o único bloco sem um "anterior de
verdade" (por convenção, seu `PreviousHash` é uma constante, como `"0"`). Todos os nós
do SatusBlockchain constroem o mesmo genesis, garantindo que partam do mesmo estado.

---

## 4. Proof of Work (PoW) — "mineração"

A seção anterior mostrou que adulterar um bloco quebra os elos seguintes. Mas note:
recalcular hashes é **barato** — um computador recalcula milhares de blocos por segundo.
Falta um ingrediente: tornar a **escrita** na cadeia custosa.

O **Proof of Work** resolve isso com um quebra-cabeça simples:

> Encontre um número (`Nonce`) tal que o hash do bloco comece com N zeros.

Como o hash é imprevisível (seção 2), **não existe atalho**: a única estratégia é
tentar nonce 0, 1, 2, 3... até aparecer um hash com o prefixo exigido. Com 4 zeros
hexadecimais, são necessárias em média **65.536 tentativas** (16⁴). Cada zero a mais
multiplica o custo por 16.

```
Tentativa 1: nonce=0     → hash = 8a3f...  ✗ (não começa com 0000)
Tentativa 2: nonce=1     → hash = 1b92...  ✗
...
Tentativa 70.213: nonce=70212 → hash = 0000c7...  ✓  BLOCO MINERADO
```

As consequências desse mecanismo:

- **Minerar é caro, verificar é barato.** Qualquer nó confere a solução com **um único
  cálculo de hash**. É assimétrico por construção.
- **Reescrever o passado fica inviável.** Alterar o Bloco 1 exige re-minerar o Bloco 1,
  depois re-minerar todos os blocos seguintes — enquanto a rede honesta continua
  minerando blocos novos. O atacante nunca alcança.
- **Escrita desacelerada e disputada.** Como minerar leva tempo, blocos novos chegam
  a um ritmo controlado — o que dá tempo de a rede propagar cada bloco antes do
  próximo (importante para o consenso, seção 6).

> **No SatusBlockchain:** `Core/ProofOfWork.cs` implementa esse loop. A dificuldade N
> é configurável pela variável `DIFFICULTY` (padrão 4, ≈1 segundo por bloco). A
> mineração é disparada sob demanda por `POST /blocks/mine` — proposital, para você
> controlar exatamente quando cada nó minera durante as demonstrações.

---

## 5. Transações e mempool

Em blockchains reais, os registros dentro dos blocos são **transações** ("A pagou X
para B"), assinadas criptograficamente pelo pagador. No SatusBlockchain, a transação
é deliberadamente mínima:

```
Transaction { From, To, Amount }    // sem assinatura, sem saldo
```

Quando você cria uma transação (`POST /transactions`), ela **não entra direto na
cadeia**. Ela vai para a **mempool**: uma fila de transações pendentes, aguardando a
próxima mineração. Quando alguém chama `POST /blocks/mine`, o nó pega as transações
pendentes, monta um bloco, resolve o PoW e adiciona à cadeia.

Esse fluxo espelha o mundo real: transações circulam pela rede o tempo todo, e os
blocos são "lotes" que as confirmam em intervalos regulares.

```
 POST /transactions          POST /blocks/mine
        │                          │
        ▼                          ▼
   ┌─────────┐   aguardando   ┌─────────┐    PoW     ┌──────────┐
   │ mempool │ ─────────────▶ │ bloco   │ ─────────▶ │ cadeia   │
   │ (fila)  │                │ novo    │            │ do nó    │
   └─────────┘                └─────────┘            └──────────┘
```

---

## 6. Rede distribuída, propagação e consenso

Até aqui, tudo acontece em **um** computador. A parte "sistemas distribuídos" começa
quando **vários nós independentes** mantêm, cada um, **sua própria cópia completa da
cadeia** — não existe servidor central nem cópia "oficial".

```
        Node 1                 Node 2                 Node 3
   ┌──────────────┐      ┌──────────────┐      ┌──────────────┐
   │ cadeia local │◀────▶│ cadeia local │◀────▶│ cadeia local │
   └──────────────┘      └──────────────┘      └──────────────┘
```

Isso levanta três perguntas — e cada uma vira uma etapa do roadmap:

### 6.1 Como um bloco chega aos outros nós? → Propagação (push)

Quando o Node 1 minera um bloco, ele o **envia ativamente** a cada peer que conhece
(`POST /blocks/receive`). Cada peer valida o bloco (hash correto? elo correto? PoW
válido?) e o anexa à própria cópia. Se um peer estiver offline, o envio falha e a
mineração local **não é afetada** — o nó ausente se recupera depois via sincronização.

### 6.2 E se dois nós minerarem ao mesmo tempo? → Fork

Dois nós podem minerar blocos diferentes "ao mesmo tempo" sobre o mesmo bloco anterior.
A rede fica temporariamente dividida em **duas versões da verdade** — um **fork**:

```
                 ┌── Bloco 2a  (mined pelo Node 1)
Bloco 0 ← Bloco 1
                 └── Bloco 2b  (mined pelo Node 3)
```

Isso **não é um erro** — é uma consequência natural de não haver coordenador central.
Forks também ocorrem quando um nó fica isolado (ex.: `docker pause node3`) e continua
minerando a própria versão.

### 6.3 Como a rede converge para uma única cadeia? → Longest chain rule

A regra de consenso mais famosa (a mesma do Bitcoin, simplificada):

> **A cadeia válida mais longa vence.**

Cada nó, ao conhecer uma cadeia **válida** e **mais longa** que a sua, **descarta a
própria cópia e adota a nova** (isso se chama *reorg*). Blocos da cadeia perdedora são
abandonados. Em caso de empate, cada nó fica com o que já tem — a disputa se resolve
quando um dos lados minerar o próximo bloco.

Note o detalhe mais importante para a disciplina: a convergência **não é instantânea**.
Durante algum tempo, nós diferentes veem cadeias diferentes — e tudo bem. Isso é
**consistência eventual**: o sistema não garante acordo a todo momento, apenas que,
dado tempo e comunicação, **todos convergem para o mesmo estado**.

### 6.4 E um nó que ficou offline? → Sincronização (pull)

Um nó que religa não recebeu os blocos propagados enquanto estava fora. Ele chama
`POST /sync`: consulta a cadeia de cada peer (`GET /chain`) e aplica a longest chain
rule. Como a cadeia dos colegas é mais longa e válida, ele a adota inteira — e volta
a estar em sincronia. Esse é o mecanismo de **recuperação após falha**.

---

## 7. O que este projeto omite (de propósito)

Blockchains reais têm camadas que **não** são necessárias para aprender os conceitos
distribuídos. O SatusBlockchain omite deliberadamente:

| Omitido | O que faria | Por que fica de fora |
|---------|-------------|----------------------|
| Assinaturas digitais | Provar que "Alice" autorizou a transação (chaves pública/privada) | Criptografia de identidade, não de distribuição |
| Saldos e double-spending | Impedir gastar duas vezes o mesmo valor | Exigiria rastrear estado de contas entre nós |
| Descoberta de peers | Nós encontrarem uns aos outros dinamicamente | Peers estáticos (3 nós) demonstram o mesmo conceito |
| Persistência | Sobreviver a reinicialização | Perder tudo ao reiniciar *ajuda* a demo de recuperação |
| Recompensa de mineração | Incentivo econômico (moeda nova por bloco) | Economia, não sistemas distribuídos |

---

## 8. Mapa: conceito → onde está no projeto

| Conceito deste documento | Onde aparece no SatusBlockchain | Etapa do roadmap |
|--------------------------|----------------------------------|------------------|
| Hash SHA-256 / serialização canônica | `Core/Hasher.cs` | 1 |
| Bloco e transação | `Core/Block.cs`, `Core/Transaction.cs` | 1 |
| Encadeamento e genesis | `Core/Blockchain.cs` | 2 |
| Validação da cadeia (tamper-evidence) | `Blockchain.IsValid()` | 2 |
| Proof of Work / dificuldade / nonce | `Core/ProofOfWork.cs` | 3 |
| Mempool | `Core/Mempool.cs` | 4 |
| Nó como serviço (REST) | `Api/*.cs` | 4 |
| Nós independentes | `docker-compose.yml` (3 serviços) | 5 |
| Propagação (push) | `Networking/PeerClient.cs`, `POST /blocks/receive` | 6 |
| Consenso (longest chain) e reorg | `Blockchain.ReplaceChain()`, `POST /sync` | 7 |
| Fork e convergência | roteiro de demonstração | 8 |
| Consistência eventual e recuperação | demos das etapas 7–8 | 7–8 |

---

## 9. Glossário rápido

- **Bloco** — lote de registros + metadados, identificado por seu hash.
- **Nonce** — número variado durante a mineração até o hash satisfazer a dificuldade.
- **Minerar** — resolver o Proof of Work e propor um novo bloco.
- **Mempool** — fila de transações pendentes, aguardando entrar em um bloco.
- **Peer** — outro nó da rede com quem um nó se comunica.
- **Fork** — divisão temporária da cadeia em duas versões concorrentes.
- **Reorg** — substituição da cadeia local por uma cadeia mais longa recebida.
- **Consistência eventual** — garantia de que os nós convergem, não de que estão
  sincronizados a todo instante.
- **Genesis** — primeiro bloco da cadeia, criado por convenção no startup.

## 10. Para ir além (opcional)

- **Bitcoin Whitepaper** (Satoshi Nakamoto, 2008) — 9 páginas; as seções 1–6 cobrem
  exatamente os conceitos deste documento, na fonte original.
- **"But how does bitcoin actually work?"** — 3Blue1Brown (YouTube) — a melhor
  visualização animada de hash, PoW e consenso.
- **Anders Brownworth's Blockchain Demo** (andersbrownworth.com/blockchain) — playground
  interativo para adulterar blocos e ver os elos quebrando.


