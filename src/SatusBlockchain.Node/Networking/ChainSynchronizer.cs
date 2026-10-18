using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Networking;

/// <summary>
/// O PULL do sistema (etapa 8): pergunta a cadeia a cada peer e adota a válida mais longa.
///
/// Até a etapa 9 este código morava dentro do handler do <c>POST /sync</c>. A etapa 10 o
/// trouxe para cá porque ele ganhou um segundo chamador: o nó que ENTRA na rede sincroniza
/// sozinho (o push só leva blocos novos; a história, ele precisa perguntar). Um código só,
/// para o sync manual e o automático não divergirem.
/// </summary>
public class ChainSynchronizer(Blockchain blockchain, Mempool mempool, PeerClient peerClient,
    NodeTelemetry telemetry, ILoggerFactory loggerFactory)
{
    // Mesma categoria de log de sempre ("Sync"): consultas no Loki continuam valendo.
    private readonly ILogger _logger = loggerFactory.CreateLogger("Sync");

    public async Task<SyncReport> SyncAsync(CancellationToken cancellationToken = default)
    {
        var previousLength = blockchain.Length;

        using var activity = telemetry.StartActivity("sync");
        activity?.SetTag("satus.sync.length_before", previousLength);

        // 1. Puxa a cadeia de cada peer em paralelo (quem não responde vira resultado).
        var chains = await peerClient.GetChainsAsync(cancellationToken);

        // 2. Julga cada uma. TryReplaceChain valida localmente e só troca se for
        //    estritamente mais longa; percorrer todos os peers termina com a cadeia
        //    válida mais longa entre {local, peers} — o último "Adopted" é o dono
        //    da cadeia final, então é ele quem aparece no relatório.
        var report = new List<PeerSyncResult>();
        var orphans = new List<Transaction>();
        string? adoptedFrom = null;

        foreach (var chain in chains)
        {
            if (chain.Chain is null)
            {
                report.Add(new PeerSyncResult(chain.Peer, ChainUpdate.Unreachable, chain.Detail));
                continue;
            }

            var result = blockchain.TryReplaceChain(chain.Chain);
            if (result.Outcome == ChainUpdate.Adopted)
            {
                adoptedFrom = chain.Peer;
                orphans.AddRange(result.OrphanTransactions);
            }

            report.Add(new PeerSyncResult(chain.Peer, result.Outcome, Describe(result)));
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
            _logger.LogInformation(
                "Cadeia adotada do peer {Peer}: {Length} blocos (antes {Previous}); " +
                "{Orphans} transações voltaram para a mempool.",
                adoptedFrom, blockchain.Length, previousLength, requeued);
        }
        else
        {
            _logger.LogInformation(
                "Sync sem adoção: a cadeia local ({Length} blocos) continua a mais longa entre as consultadas.",
                blockchain.Length);
        }

        return new SyncReport(previousLength, blockchain.Length, adoptedFrom, requeued, report);
    }

    /// <summary>Texto do relatório por peer — o "porquê" legível de cada desfecho.</summary>
    private static string Describe(ChainUpdateResult result) => result.Outcome switch
    {
        ChainUpdate.Adopted => $"cadeia mais longa ({result.Length} blocos) adotada",
        ChainUpdate.Kept => $"local já tem {result.Length} blocos (empate ou peer atrás)",
        _ => "cadeia recusada (elo/hash/PoW quebrados ou genesis diferente)"
    };
}

/// <summary>Resultado de um sync: o antes/depois da cadeia local e o desfecho por peer.</summary>
/// <param name="AdoptedFrom">Peer de quem veio a cadeia final, ou <c>null</c> se a local foi mantida.</param>
/// <param name="OrphansBackToMempool">Transações dos blocos descartados que voltaram para a fila.</param>
public sealed record SyncReport(
    int PreviousLength,
    int Length,
    string? AdoptedFrom,
    int OrphansBackToMempool,
    IReadOnlyList<PeerSyncResult> Peers);

/// <summary>O que aconteceu com a cadeia de UM peer no sync.</summary>
public sealed record PeerSyncResult(string Peer, ChainUpdate Outcome, string Detail);
