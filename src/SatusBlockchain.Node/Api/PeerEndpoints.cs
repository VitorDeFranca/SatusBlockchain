namespace SatusBlockchain.Node.Api;

/// <summary>Configuração de rede do nó: quem são os peers e para onde o push vai.</summary>
public static class PeerEndpoints
{
    public static void MapPeerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/peers").WithTags("Peers");

        // Lista estática lida de PEERS na inicialização (etapa 6).
        group.MapGet("/", (NodeOptions node) => Results.Ok(new
        {
            node = node.NodeId,
            peers = node.Peers
        }));
    }
}
