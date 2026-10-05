# Requisitos — SatusBlockchain

## 1. Visão geral

SatusBlockchain é uma **blockchain distribuída educacional e simplificada**, desenvolvida
para a disciplina de **Sistemas Distribuídos**. O sistema executa localmente com múltiplos
nós independentes via Docker/Docker Compose.

O objetivo **não** é construir uma criptomoeda nem uma blockchain pronta para produção,
mas demonstrar, de forma simples e explicável, conceitos de sistemas distribuídos e blockchain.

## 2. Objetivos de aprendizado

O projeto deve permitir demonstrar:

- múltiplos nós independentes;
- comunicação entre nós via API REST;
- replicação de estado (cada nó mantém sua cópia da cadeia);
- hashing SHA-256 e encadeamento de blocos;
- validação da integridade da blockchain;
- Proof of Work simplificado;
- propagação de blocos (push);
- consenso entre nós (longest chain rule);
- consistência eventual;
- recuperação de um nó após ficar offline (pull/sync);
- resolução simples de forks.

## 3. Stack

- .NET 10 / C#
- ASP.NET Core (Minimal APIs)
- SHA-256 (System.Security.Cryptography — BCL)
- Docker e Docker Compose
- xUnit (testes unitários)
- System.Net.Http / System.Text.Json (BCL) — sem bibliotecas externas de blockchain
- OpenTelemetry (.NET), Prometheus, Grafana, Jaeger, Loki e Alloy (etapa 9 — observabilidade dos três pilares)

## 4. Requisitos funcionais

| ID | Requisito |
|----|-----------|
| RF-01 | O bloco deve conter: índice, timestamp, lista de transações, hash do bloco anterior, nonce e hash próprio. |
| RF-02 | A transação deve conter: `From`, `To`, `Amount` (sem assinatura digital). |
| RF-03 | O hash do bloco deve ser SHA-256 calculado sobre uma serialização canônica (JSON com campos ordenados) do conteúdo do bloco. |
| RF-04 | O primeiro bloco (genesis) deve ser criado automaticamente na inicialização do nó. |
| RF-05 | O nó deve validar a integridade da cadeia (encadeamento de hashes + Proof of Work de cada bloco). |
| RF-06 | A mineração deve ocorrer **sob demanda** via endpoint (`POST /blocks/mine`), executando Proof of Work sobre as transações pendentes (mempool). |
| RF-07 | O Proof of Work deve exigir hash com N zeros hexadecimais iniciais; N configurável por variável de ambiente (`DIFFICULTY`, padrão 4). |
| RF-08 | Ao minerar um bloco, o nó deve propagá-lo aos peers configurados (push: `POST /blocks/receive`). |
| RF-09 | Um nó deve aceitar um bloco recebido somente se ele for válido e estender a cadeia local. |
| RF-10 | O nó deve expor endpoint de sincronização (`POST /sync`) que consulta os peers e adota a cadeia válida mais longa (pull). |
| RF-11 | Em caso de empate de comprimento entre cadeias válidas, o nó mantém a cadeia local (regra "first seen"). |
| RF-12 | Deve ser possível subir pelo menos 3 nós independentes com `docker compose up`. |
| RF-13 | Peers são configurados estaticamente por variável de ambiente (`PEERS=http://node2:8080,...`). |
| RF-14 | Ao aceitar uma transação, o nó deve propagá-la aos peers (gossip: `POST /transactions/receive`), para que **qualquer** nó possa incluí-la em um bloco. |
| RF-15 | Um nó deve aceitar uma transação recebida somente se for válida, não estiver já pendente na mempool local e não estiver já confirmada na cadeia local. |
| RF-16 | O nó deve expor suas métricas no formato Prometheus (`GET /metrics`) e, quando `OTEL_EXPORTER_OTLP_ENDPOINT` estiver definido, exportar traces via OTLP. |

### Endpoints previstos

```
GET  /chain                  → cadeia completa do nó
GET  /chain/validate         → resultado da validação da cadeia local
POST /transactions           → adiciona transação à mempool
GET  /transactions/pending   → lista transações pendentes
POST /blocks/mine            → minera bloco com a mempool e propaga aos peers
POST /blocks/receive         → recebe bloco propagado por outro nó
POST /transactions/receive   → recebe transação propagada por outro nó (gossip)
POST /sync                   → sincroniza com peers (longest chain rule)
GET  /peers                  → lista peers configurados
GET  /metrics                → métricas do nó no formato Prometheus (etapa 9)
```

## 5. Requisitos não funcionais

- **Simplicidade didática**: código explícito, fácil de explicar; sem Clean Architecture,
  CQRS, MediatR, Event Sourcing, message brokers ou microsserviços adicionais.
- **Estado em memória**: sem banco de dados. Reiniciar um nó implica perder a cadeia local
  e ressincronizar com os peers (comportamento desejado para a demonstração de recuperação).
- **Concorrência**: thread-safety via `lock` simples nas estruturas mutáveis (cadeia e mempool).
- **Configuração por ambiente**: `NODE_ID`, `PEERS`, `DIFFICULTY`, porta HTTP e
  `OTEL_EXPORTER_OTLP_ENDPOINT` (opcional — habilita o export de traces, etapa 9).

## 6. Fora de escopo (deliberadamente)

- Assinaturas digitais, wallets, chaves pública/privada;
- saldos de contas e prevenção de double-spending;
- descoberta dinâmica de peers / gossip;
- persistência em disco ou banco de dados;
- validação de timestamps;
- mineração automática em background;
- TLS, autenticação ou autorização entre nós;
- rate limiting, alertas ou APM (a etapa 9 **observa** o sistema — logs, métricas e traces —;
  ela não vigia, não bloqueia e não decide nada).

## 7. Decisões registradas

| Decisão | Escolha | Justificativa |
|---------|---------|---------------|
| Conteúdo da transação | `From`/`To`/`Amount` sem assinatura | Permite demonstrar mempool sem criptografia assimétrica |
| Modelo de mineração | Sob demanda via API | Controle didático; permite provocar forks deterministicamente |
| Comunicação entre nós | Híbrida: push no mine + pull no /sync | Push demonstra propagação; pull demonstra recuperação e consistência eventual |
| Descoberta de peers | Estática via variável de ambiente | Suficiente para 3 nós; sem complexidade de gossip |
| Consenso | Longest chain rule; empate = mantém local (first seen) | Análogo simplificado ao consenso de Nakamoto |
| Reorg | Transações de blocos órfãos retornam à mempool (exceto as já presentes na cadeia adotada) | Fiel ao Bitcoin; permite demonstrar finalidade probabilística |
| Estilo de API | Minimal APIs organizadas por arquivos de extensão | Menos cerimônia que Controllers, mesma clareza |
| Mempool compartilhada? | **Não** — cada nó tem a sua, e o que existe é o repasse (gossip, etapa 7) | Espelha Bitcoin/Ethereum: mempool é estado local, não consenso; sem gossip, a transação ficaria presa no nó onde foi postada |
