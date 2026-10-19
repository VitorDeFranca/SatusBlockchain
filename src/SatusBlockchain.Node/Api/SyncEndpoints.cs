using SatusBlockchain.Node.Networking;

namespace SatusBlockchain.Node.Api;

/// <summary>
/// Sincronização (etapa 8): o lado do PULL. O push (etapa 6) leva o bloco novo para os
/// peers, mas um nó que ficou fora do ar perdeu a HISTÓRIA — e nenhum push reenvia o
/// passado. Este endpoint faz o nó perguntar.
///
/// É **manual** (nada roda sozinho em segundo plano): deixa explícito quem decide
/// sincronizar e deixa a etapa determinística. A exceção, desde a etapa 10, é o nó que
/// ENTRA na rede, que sincroniza uma vez ao subir (<see cref="MembershipService"/>) — com
/// o mesmo <see cref="ChainSynchronizer"/> usado aqui.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder routes)
    {
        // Rota na RAIZ (/sync), não sob /chain: é uma AÇÃO do nó, não uma leitura de cadeia.
        routes.MapPost("/sync", async (ChainSynchronizer synchronizer, MembershipService membership,
            NodeOptions node) =>
        {
            // Etapa 10: quem volta de uma queda ou partição pode ter sido removido da lista
            // dos outros. Sincronizar é voltar à CADEIA e às LISTAS: um nó com ADVERTISE_URL
            // se reanuncia antes de puxar (sem ele, a chamada é um no-op).
            await membership.AnnounceAsync();

            var report = await synchronizer.SyncAsync();

            // Sempre 200: peer ausente é situação normal numa rede parcial — o relatório
            // é que diz o que aconteceu com cada um.
            return Results.Ok(new
            {
                node = node.NodeId,
                previousLength = report.PreviousLength,
                length = report.Length,
                adopted = report.AdoptedFrom is null ? null : new { from = report.AdoptedFrom, length = report.Length },
                orphansBackToMempool = report.OrphansBackToMempool,
                peers = report.Peers.Select(peer => new
                {
                    peer = peer.Peer,
                    outcome = peer.Outcome.ToString(),
                    detail = peer.Detail
                })
            });
        });
    }
}
