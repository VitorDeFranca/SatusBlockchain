# Roadmap — SatusBlockchain

Desenvolvimento incremental: cada etapa é pequena, compilável e testável.
Ao final de cada etapa: build → testes → resumo → teste manual → **pausa para aprovação**.

Legenda de status: ✅ concluída · 🚧 em andamento · ⬜ pendente

| # | Etapa | Entrega | Conceitos de Sist. Distribuídos | Status |
|---|-------|---------|----------------------------------|--------|
| 0 | Fundação | Solution, projetos (nó + testes), `docs/*.md` v1 | — | ✅ |
| 1 | Bloco e hash | `Block`, `Transaction`, `Hasher` (SHA-256), testes | Imutabilidade, integridade, fingerprint criptográfico | ✅ |
| 2 | Cadeia e genesis | `Blockchain`, genesis block, `IsValid()`, testes | Encadeamento, detecção de adulteração, base do estado replicado | ⬜ |
| 3 | Proof of Work | `ProofOfWork`, dificuldade configurável, testes | Custo computacional, resistência à reescrita do histórico | ⬜ |
| 4 | API REST (nó único) | Endpoints: chain, transactions, mine (sem peers) | Nó como serviço autônomo, contrato de comunicação | ⬜ |
| 5 | Docker + Compose | Dockerfile, compose com 3 nós isolados | Processos independentes, redes, configuração por ambiente | ⬜ |
| 6 | Propagação (push) | `PeerClient`, broadcast no mine, `/blocks/receive` | Comunicação nó-a-nó, replicação de estado, tolerância a peer offline | ⬜ |
| 7 | Consenso e sync (pull) | Longest chain rule, `/sync`, substituição de cadeia (reorg) | Consenso, consistência eventual, recuperação após falha | ⬜ |
| 8 | Fork e convergência | Roteiro e scripts de demo: fork forçado e resolução | Partição de rede, forks, convergência | ⬜ |
| 9 | Fechamento | README final, docs atualizados, roteiro de apresentação | — | ⬜ |

## Notas de ordenação

- Testes unitários acompanham as etapas 1–3 (não são uma etapa separada).
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
