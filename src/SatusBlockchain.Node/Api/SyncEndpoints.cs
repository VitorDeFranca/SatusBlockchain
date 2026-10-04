using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Api;

/// <summary>
/// Sincronização (etapa 8): o lado do PULL. O push (etapa 6) leva o bloco novo para os
/// peers, mas um nó que ficou fora do ar perdeu a HISTÓRIA — e nenhum push reenvia o
/// passado. Este endpoint faz o nó perguntar.
///
/// É **manual** (nada roda sozinho na subida): deixa explícito quem decide sincronizar,
/// deixa a etapa determinística e evita um BackgroundService rodando em segundo plano.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder routes)
    {
        // Rota na RAIZ (/sync), não sob /chain: é uma AÇÃO do nó, não uma leitura de cadeia.
        routes.MapPost("/sync", async (Blockchain blockchain, Mempool mempool, PeerClient peerClient,
            NodeOptions node, NodeTelemetry telemetry, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Sync");
            var previousLength = blockchain.Length;

            using var activity = telemetry.StartActivity("sync");
            activity?.SetTag("satus.sync.length_before", previousLength);

            // 1. Puxa a cadeia de cada peer em paralelo (quem não responde vira resultado).
            var chains = await peerClient.GetChainsAsync();

            // 2. Judge cada uma. TryReplaceChain valida localmente e só troca se for
            //    estritamente mais longa; percorrer todos os peers termina com a cadeia
            //    válida mais longa entre {local, peers} — o último "Adopted" é o dono
            //    da cadeia final, então é ele quem aparece no relatório.
            var report = new List<object>();
            var orphans = new List<Transaction>();
            string? adoptedFrom = null;

            foreach (var chain in chains)
            {
                if (chain.Chain is null)
                {
                    report.Add(new
                    {
                        peer = chain.Peer,
                        outcome = ChainUpdate.Unreachable.ToString(),
                        detail = chain.Detail
                    });
                    continue;
                }

                var result = blockchain.TryReplaceChain(chain.Chain);
                if (result.Outcome == ChainUpdate.Adopted)
                {
                    adoptedFrom = chain.Peer;
                    orphans.AddRange(result.OrphanTransactions);
                }

                report.Add(new
                {
                    peer = chain.Peer,
                    outcome = result.Outcome.ToString(),
                    detail = Describe(result)
                });
            }

            // 3. Transações órfãs voltam à mempool (como no Bitcoin), MENOS as que a
            //    cadeia final acabou confirmando: a mesma transação pode ter sido minerada
            //    nos dois lados do fork, e voltar à fila a mineraria de novo.
            var backToMempool = orphans
                .Where(transaction => !blockchain.ContainsTransaction(transaction))
                .ToList();
            var requeued = mempool.AddRange(backToMempool);

            // Métrica + tags do span com o RESULTADO do sync — domínio fechado:
            // outcome é adopted|kept, nunca a URL do peer como valor livre.
            telemetry.Sync(adoptedFrom is not null ? "adopted" : "kept");
            activity?.SetTag("satus.sync.outcome", adoptedFrom is not null ? "adopted" : "kept");
            activity?.SetTag("satus.sync.length_after", blockchain.Length);
            activity?.SetTag("satus.sync.orphans", requeued);
            if (adoptedFrom is not null)
                activity?.SetTag("satus.sync.adopted_from", adoptedFrom);

            if (adoptedFrom is not null)
            {
                logger.LogInformation(
                    "Cadeia adotada do peer {Peer}: {Length} blocos (antes {Previous}); " +
                    "{Orphans} transações voltaram para a mempool.",
                    adoptedFrom, blockchain.Length, previousLength, requeued);
            }
            else
            {
                logger.LogInformation(
                    "Sync sem adoção: a cadeia local ({Length} blocos) continua a mais longa entre as consultadas.",
                    blockchain.Length);
            }

            // Sempre 200: peer ausente é situação normal numa rede parcial — o relatório
            // é que diz o que aconteceu com cada um.
            return Results.Ok(new
            {
                node = node.NodeId,
                previousLength,
                length = blockchain.Length,
                adopted = adoptedFrom is null ? null : new { from = adoptedFrom, length = blockchain.Length },
                orphansBackToMempool = requeued,
                peers = report
            });
        });
    }

    /// <summary>Texto do relatório por peer — o "porquê" legível de cada desfecho.</summary>
    private static string Describe(ChainUpdateResult result) => result.Outcome switch
    {
        ChainUpdate.Adopted => $"cadeia mais longa ({result.Length} blocos) adotada",
        ChainUpdate.Kept => $"local já tem {result.Length} blocos (empate ou peer atrás)",
        _ => "cadeia recusada (elo/hash/PoW quebrados ou genesis diferente)"
    };
}