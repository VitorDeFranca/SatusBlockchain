# Demo script — roteiro de demonstração (etapa 10)

Este documento é o **roteiro de apresentação** da SatusBlockchain: o que executar, o que
falar e o que mostrar em cada passo. Ele cobre os três conceitos que a etapa 10 fecha no
roadmap — **partição de rede, forks e convergência** — e amarra as etapas 6–9 em uma
história única que dá para contar em sala com uma linha de comando.

## 1. O que a demonstração prova

| Conceito | Como aparece na demo |
|---|---|
| Partição de rede | um nó sai da rede Docker e o resto do sistema **continua funcionando** (push falha como aviso, não como exceção) |
| Fork | o nó particionado minera antes de sincronizar e cria um bloco no **mesmo índice** com hash diferente — duas versões da história, ambas válidas |
| Longest chain rule | o `POST /sync` faz o nó adotar a cadeia **estritamente** mais longa (empate não adota: vale first-seen) |
| Reorg + mempool | o bloco perdedor vira órfão e suas transações **voltam à mempool** — nada se perde na troca de versão |
| Convergência | os 3 nós terminam com o **mesmo último hash**, cadeias válidas, mempools coerentes — sem coordenador nenhum |
| Recuperação (etapa 8) | companion script: um nó que caiu volta ao **genesis** (memória volátil) e um único `/sync` reconstrói a história inteira |

## 2. Pré-requisitos

- **Docker Desktop ligado** e a stack no ar: `docker compose up -d` (os 3 nós, porta
  8080/8081/8082). Sem a stack, os scripts abortam com mensagem clara (exit 1).
- **PowerShell 5.1 ou superior** no Windows — os scripts usam só Docker CLI e
  `Invoke-RestMethod` (nada de apps externos).
- Stack de observabilidade **opcional** (Etapa 9):
  `docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d`.
  Sem ela a demo roda igual; com ela, use `-Hints` e a seção 6.
- Base limpa, se quiser determinismo visual: `-Fresh` reinicia os 3 containers e todos
  voltam ao genesis (o estado é em memória — isso é *feature*, não acidente).

## 3. Os dois scripts

| Script | Prova | Duração típica | Verificações |
|---|---|---|---|
| `.\scripts\demo-fork.ps1` | fork forçado + resolução + convergência | ~30 s (dificuldade 4) | 30 |
| `.\scripts\demo-recuperacao.ps1` | queda, retorno ao genesis, recuperação por pull | ~15 s | 17 |

Flags dos dois: `-Fresh` (reinicia os containers e começa do genesis) e `-Hints`
(imprime, nos passos críticos, o que olhar no Grafana/Jaeger/Loki).

Saída: passos numerados com `[OK]`/`[FALHA]` por expectativa e resumo final —
**exit 0** se tudo passou, **exit 1** com o passo que falhou. As asserções são
**relativas a N** (a altura atual da cadeia): os scripts funcionam em qualquer momento
da vida da stack, com ou sem histórico.

Se uma execução for interrompida no meio, o `finally` religa o node2 à rede; e mesmo
que você feche o terminal em cima, a próxima execução detecta e **se autocura** no passo 0.

## 4. Roteiro — `demo-fork.ps1` (fork e convergência)

| Passo | Ação | Expectativa verificada | Conceito na tela |
|---|---|---|---|
| 0 | pré-condições (Docker, rede, 3 nós respondendo) | stack viva; autocura se o node2 estiver órfão de uma execução anterior | pré-requisito de sistemas distribuídos: saber quem está no ar |
| 1 | snapshot dos 3 nós (`GET /` + `/chain/validate`) | mesmo comprimento, **mesmo último hash**, cadeias válidas | réplicas em sincronia |
| 2 | `docker network disconnect` do node2 | node2 fora da rede; o push ao node2 passa a estourar o timeout de 2 s | **partição de rede**; tolerância a falha isolada (aviso, nunca exceção) |
| 3 | node1 minera 2 blocos | node1 e node3 em N+2 (push confirmado antes do mine responder) | o sistema **prossegue** durante a falha de um degrau |
| 4 | `docker network connect` (reconexão) | node2 volta em N, defasado, com outro topo | estado divergente sem corrupção: cadeia válida, só velha |
| 5 | transação **T2** nasce no node2 | T2 pendente na mempool local do node2 | mempool é estado local; o gossip repassa, mas ninguém minera do outro lado |
| 6 | node2 **minera sem sincronizar** | bloco criado no **mesmo índice N** com **hash diferente** do node1; node1 segue N+2, node2 N+1 | **FORK** — duas versões da história, ambas PoW-válidas; o push do perdedor leva 409 |
| 7 | node2 `POST /sync` | `adopted` do node1, altura N+2, **1 órfã** de volta à mempool, T2 pendente de novo | **longest chain rule** + reorg sem perda de transação |
| 8 | node2 minera de novo | T2 dentro do novo bloco; 3 nós em N+3, mesmo hash, válidos, mempools limpas | **convergência** + fidelidade à origem (a órfã sobrevive no topo) |
| 9 | resumo | — | a lição: regra local + cadeia validável, sem coordenador |

## 5. Roteiro — `demo-recuperacao.ps1` (falha e recuperação)

| Passo | Ação | Expectativa verificada | Conceito na tela |
|---|---|---|---|
| 0–1 | pré-condições + snapshot | baseline em harmonia | réplicas em sincronia |
| 2 | `docker compose stop node3` | node3 inacessível | falha real de um nó |
| 3 | node1 minera 2 blocos | node1 e node2 em N+2; o push ao node3 morto é recusado **na hora** (conexão recusada) | a rede **não espera** o nó caído; push não reenvia o passado |
| 4 | `docker compose start node3` | node3 volta com **1 bloco** (o genesis) e válido | memória volátil: ele perdeu a história inteira, sem corromper nada |
| 5 | node3 `POST /sync` | `adopted`, altura N+2, mesmo hash do node1 | **pull**: quem decide se atualizar é o próprio nó |
| 6 | snapshot final | 3/3 iguais e válidos | convergência após falha |

## 6. Versão manual, passo a passo (sem script)

Para quem prefere conduzir a apresentação comando a comando (ou provar que o script
não esconde mágica). Os "átomos" são os mesmos da coleção Postman — ver também
[postman/README.md](../postman/README.md).

```powershell
$N1 = 'http://localhost:8080'; $N2 = 'http://localhost:8081'; $N3 = 'http://localhost:8082'

# 1. baseline: os 3 nós em harmonia (altura e último hash)
foreach ($p in 8080,8081,8082) { Invoke-RestMethod "http://localhost:$p/chain/validate" }

# 2. PARTIÇÃO: o node2 sai da rede do compose (nome derivado do projeto)
$net = docker inspect node1 --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}'
docker network disconnect $net node2

# 3. o lado isolado segue em frente (cada mine espera o push — o peer mudo custa ~2s)
Invoke-RestMethod -Method Post "$N1/blocks/mine"
Invoke-RestMethod -Method Post "$N1/blocks/mine"

# 4. reconexão: o node2 volta defasado
docker network connect $net node2
Invoke-RestMethod "$N2/chain/validate"          # altura menor e hash diferente do node1

# 5. transação T2 nasce no node2 (o gossip leva aos demais)
Invoke-RestMethod -Method Post "$N2/transactions/" -ContentType 'application/json' `
  -Body '{"from":"carol","to":"dave","amount":7}'

# 6. FORK: o node2 minera ANTES de sincronizar
Invoke-RestMethod -Method Post "$N2/blocks/mine"
Invoke-RestMethod "$N2/chain"                   # bloco no mesmo índice N, outro hash
Invoke-RestMethod "$N1/chain"

# 7. RESOLUÇÃO: longest chain rule
Invoke-RestMethod -Method Post "$N2/sync"       # adopted + orphansBackToMempool
Invoke-RestMethod "$N2/transactions/pending"    # a T2 voltou para a fila

# 8. a órfã sobrevive no topo e tudo converge
Invoke-RestMethod -Method Post "$N2/blocks/mine"
foreach ($p in 8080,8081,8082) { Invoke-RestMethod "http://localhost:$p/chain/validate" }
```

Para a recuperação (etapa 8), o mesmo molde: `docker compose stop node3` → mine 2× no
node1 → `docker compose start node3` → `POST /sync` no node3 → snapshot final.

## 7. Narrativa sugerida (o que falar em cada momento)

- **Passo 1** — "Três processos independentes, três cópias da mesma história. A prova de
  que estão em sincronia é a impressão digital do último bloco: mesmo hash."
- **Passo 2** — "Agora eu vou **mentir para o node2**: tirá-lo da rede não é apagar ele —
  ele continua vivo, com a cadeia dele. O que muda é que não existe caminho até os
  vizinhos. Reparem que o resto do sistema não trava: o push para o node2 vira um log de
  aviso ('inacessível') e a mineração segue. Tolerância a falha é essa: isolar o problema."
- **Passo 3** — "O node1 avançou dois blocos sem ninguém. Ninguém guarda o histórico do
  node2 para reenviar depois — protocolo de push carrega só o **próximo** bloco. Esse é
  o desenho, não um bug."
- **Passo 4** — "Ele voltou. E está 'errado'? Não: a cadeia dele é **válida**, só está
  **velha**. Em sistemas distribuídos isso se chama divergência — e é o estado natural
  depois de uma partição."
- **Passo 5** — "Uma transação nasce no nó que está atrás. Ela entra na mempool **dele** e
  é gossipada — mas atenção: ninguém do outro lado vai minerar agora. É isso que segura
  a transação vencedora fora do lado perdedor."
- **Passo 6** — "Aqui está o fork, de propósito: ele **mina sem perguntar**. O resultado é
  um bloco no mesmo índice N do bloco do node1, com PoW válido e hash diferente. Duas
  versões da história existem agora — e o push do bloco dele levou **409** nos vizinhos
  (índice fora de sequência). Ninguém brigou, ninguém escolheu um líder: o empate é a
  condição de fork clássica."
- **Passo 7** — "A regra é simples e local: a cadeia **mais longa** vence, e só se for
  estritamente maior (empate fica como está — first-seen). O node2 pergunta, adota a de
  N+2, descarta o bloco perdedor… e repara na parte bonita: a transação do bloco descartado
  **voltou para a mempool**. Nada se perdeu na troca de versão — reorg sem corrupção."
- **Passo 8** — "E ele fecha o ciclo: minera em cima da cadeia que adotou, e a T2 — a
  órfã — é confirmada no topo. Os três nós, o mesmo hash, cadeias válidas, mempools
  limpas. Todo mundo convergiu sem coordenação nenhuma."
- **Encerramento** — "O que vocês viram foi blockchain funcionando como sistema
  distribuído: sem relógio global, sem coordenador, com falhas explícitas — e invariantes
  (encadeamento, PoW, cadeia mais longa) que restauram o consenso quando a rede volta."

## 8. Observabilidade da demo (Etapa 9 em cena)

Com o overlay ativo (`-f` duplo do compose), cada passo tem uma janela correspondente:

| Momento | Onde olhar | O que se espera ver |
|---|---|---|
| Passo 2–3 (partição) | Grafana → **SatusBlockchain — Visão dos nós**; Loki → `{compose_project="satusblockchain", container="node1"} \|= "inacessível"` | `sum by(outcome)(satus_peer_push_total)` com picos de `refused`/erro no push; a linha de aviso `Peer http://node2:8080 inacessível…` (timeout de 2 s) |
| Passo 6 (fork) | Loki → `{compose_project="satusblockchain", container=~"node1\|node3"} \|= "409"`; Jaeger (serviço **node2**) | os dois peers recusando o bloco do perdedor; o trace do `POST /blocks/mine` do node2 com spans de `/blocks/receive` respondendo conflito |
| Passo 7 (sync) | Jaeger → trace `sync` do **node2**; Loki → `{container="node2"} \|= "Cadeia adotada"` | tags `satus.sync.outcome=adopted`, `satus.sync.orphans=1`; log `Cadeia adotada do peer http://node1:8080 …` |
| Passo 8 (convergência) | Grafana → painel `satus_chain_length`; Prometheus → `satus_chain_length` | as 3 curvas se reencontrando no mesmo valor (a divergência do passo 6 e o reencontro do 8) |
| Demo de recuperação | Prometheus → `/targets` (ou `up`); Grafana → `satus_chain_length` | alvo do node3 `down` durante a parada; ao voltar, uma curva "sozinha" em 1 bloco que salta para o patamar dos outros após o `/sync` |

Endereços: Grafana `http://localhost:3000/d/satus-nos` · Jaeger `http://localhost:16686`
· Prometheus `http://localhost:9090` · Loki via Grafana **Explore**.

> Os scripts não ficam consultando métricas: leitura de `/metrics` tem janela assíncrona
> de ~2 s e o scrape do Prometheus é de 5 s. Os `-Hints` apenas dizem **o que procurar** —
> a verificação dura é feita pela API dos nós, que é determinística.

## 9. Plano B — erros comuns

| Sintoma | Causa | Correção |
|---|---|---|
| `Docker daemon indisponível` | Docker Desktop fechado | abrir o Docker Desktop e rodar de novo |
| `a stack parece estar fora do ar` | nós não subiram | `docker compose up -d` (ou com o overlay, `-f` duplo) |
| `Baseline inconsistente` | uma execução anterior parou no meio e deixou os nós divergentes | `.\scripts\demo-fork.ps1 -Fresh` — ou deixar o próprio autocurar do passo 0 tentar (ele religa o node2; divergência de cadeia mesmo só o `-Fresh` resolve) |
| Demo lenta / mineração demorada | dificuldade alta do ambiente | `$env:NODE_DIFFICULTY = "2"; docker compose up -d --force-recreate` e subir de novo (os **3 nós precisam da mesma** dificuldade — o genesis muda com ela) |
| `node2 não voltou a responder após reconectar` | Docker Desktop engasgou com a rede | `docker network connect <rede> node2` manualmente; se persistir, `docker compose restart node2` (aí ele volta ao genesis — `-Fresh` no próximo rodada) |
| Acentos quebrados ao editar os `.ps1` | PowerShell 5.1 lê sem BOM como ANSI | salvar em **UTF-8 com BOM** (é o formato dos scripts do repositório) |
| Terminal "travado" ~2 s por mine no passo 3 | não é travamento: é o push esperando o peer mudo estourar o timeout | é o comportamento correto — e é explicação pronta de tolerância a falhas |

## 10. Por que a partição é `docker network disconnect` (decisão técnica)

Foram considerados quatro mecanismos para provocar a partição:

| Mecanismo | Por que não (ou por que sim) |
|---|---|
| **`docker network disconnect`** ✅ | derruba o endpoint de verdade (DNS + rota): a conexão nem é estabelecida, então **não sobra requisição nenhuma enfileirada** para ser entregue depois. O processo do nó continua vivo e com o estado intacto, a porta do host volta ao normal na reconexão e o push falha em ~2 s (timeout do `PeerClient`) como log de aviso. |
| `docker pause` ❌ | parece a escolha óbvia (pausa sem perder estado), mas **foi validado ao vivo e não cria fork**: o nó pausado mantém o socket de escuta no kernel — o `POST /blocks/receive` fica na fila de aceitação e é processado quando o processo descongela. Resultado: o nó "cego" voltava já em dia e o fork nunca acontecia. |
| Override de compose com `PEERS: ""` ❌ | partição limpa de verdade (bidirecional), mas trocar `env` exige **recriar** o container — e o estado em memória zera no genesis, embaralhando a narrativa no meio da demo. |
| Mineração simultânea (corrida) ❌ | é como forks nascem "de verdade" (latência de propagação), mas é **não-determinística** — péssima para apresentação. Já é coberta pelos testes de integração (`SyncTests`). |

Aritmética do roteiro: o node1 minera **2** blocos no lado isolado e o node2 volta
minerando apenas **1** — porque empate de altura não adota (first-seen wins); a regra da
cadeia mais longa exige diferença **estritamente** maior. E a T2 nasce **depois** da
reconexão, para que o gossip não a leve ao lado vencedor: assim o bloco perdedor é órfão
de verdade, a transação volta à mempool (`orphansBackToMempool = 1`) e a re-mineração
fecha a demonstração de fidelidade à origem escolhida na etapa 8.


