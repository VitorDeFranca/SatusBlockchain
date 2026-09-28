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
| 6 | Propagação (push) | `PeerClient`, broadcast no mine, `/blocks/receive` | Comunicação nó-a-nó, replicação de estado, tolerância a peer offline | ⬜ |
| 7 | Consenso e sync (pull) | Longest chain rule, `/sync`, substituição de cadeia (reorg) | Consenso, consistência eventual, recuperação após falha | ⬜ |
| 8 | Fork e convergência | Roteiro e scripts de demo: fork forçado e resolução | Partição de rede, forks, convergência | ⬜ |
| 9 | Fechamento | README final, docs atualizados, roteiro de apresentação | — | ⬜ |

## Notas de ordenação

- Testes acompanham as etapas 1–4 (não são etapa separada): **unidade** para o domínio
  (etapas 1–3) e **integração HTTP** para a API (etapa 4), subindo o nó real em um
  processo isolado, em porta livre.
- Os testes ficam em **um projeto só** (`tests/SatusBlockchain.Node.Tests`), com uma pasta
  por tipo: `Unit/` (domínio isolado, sem rede) e `Integration/` (sobe o nó e conversa por
  HTTP). Os namespaces `…Tests.Unit` / `…Tests.Integration` permitem rodar só um conjunto:
  `dotnet test --filter "FullyQualifiedName~Tests.Unit"` (segundos, sem rede) ou
  `…~Tests.Integration` (~20s). Um projeto só mantém a árvore da solução curta.
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
- O desacoplamento push (etapa 6) / pull (etapa 7) permite demonstrar primeiro a
  propagação "feliz" e só depois falhas, recuperação e consenso.

## Cenário de demonstração final (referência para as etapas)

1. `docker compose up` com 3 nós;
2. criar transações e minerar no node1 → bloco propaga para node2 e node3;
3. validar a cadeia em todos os nós;
4. derrubar o node3, minerar mais blocos nos demais;
5. religar o node3 e sincronizá-lo (`POST /sync`);
6. provocar um fork (isolar node3, minerar em ambos os lados);
7. religar e observar a convergência pela longest chain rule.
