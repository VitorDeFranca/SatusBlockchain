using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Api;

/// <summary>
/// A lista de peers do nó (etapa 10): quem está nela, quem entra e quem sai. Até a etapa 9
/// a lista era só leitura (o <c>PEERS</c> da subida); agora nós entram e saem com o sistema
/// rodando — a regra de quem pode entrar (o handshake) fica aqui, a lista em si no
/// <see cref="PeerRegistry"/>.
/// </summary>
public static class PeerEndpoints
{
    public static void MapPeerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/peers").WithTags("Peers");

        // A lista VIVA: nasce de PEERS e muda com anúncios, despedidas e falhas de conexão.
        // url = o ADVERTISE_URL deste nó (null = nó que não se anuncia).
        group.MapGet("/", (NodeOptions node, PeerRegistry registry) => Results.Ok(new
        {
            node = node.NodeId,
            url = node.AdvertiseUrl,
            peers = registry.GetPeers()
        }));

        // Um nó se anuncia: "existo, me chame em {url}". Antes de registrar, HANDSHAKE — este
        // nó chama o endereço de volta (GET /) e confere duas coisas:
        //   1. ele responde ali?                → senão 502 (anunciou um endereço que não alcançamos);
        //   2. ele nasceu do MESMO genesis?     → senão 409 (outra dificuldade = outro universo).
        // Sem o handshake, qualquer um registraria um endereço qualquer e este nó passaria a
        // disparar blocos e transações para lá.
        group.MapPost("/", async (PeerAnnouncement announcement, NodeOptions node, Blockchain blockchain,
            PeerRegistry registry, PeerClient peerClient, NodeTelemetry telemetry, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Peers");

            if (!NodeOptions.TryNormalizeUrl(announcement.Url, out var url))
            {
                return Results.BadRequest(new
                {
                    added = false,
                    error = "url inválida: informe a URL absoluta http(s) pela qual o nó é chamado."
                });
            }

            if (registry.IsSelf(url))
                return Results.BadRequest(new { added = false, error = "Esse é o endereço deste próprio nó." });

            var reply = await peerClient.GetIdentityAsync(url);
            var identity = reply.Value;

            if (identity is null)
            {
                logger.LogWarning("Anúncio de {Peer} recusado: o handshake não teve resposta ({Detail}).",
                    url, reply.Detail);
                return Results.Json(new
                {
                    added = false,
                    error = $"Handshake falhou: {url} não respondeu a GET / ({reply.Detail})."
                }, statusCode: StatusCodes.Status502BadGateway);
            }

            if (identity.Genesis != blockchain.GenesisHash)
            {
                logger.LogWarning("Anúncio de {Peer} recusado: genesis diferente (dificuldade {Difficulty}).",
                    url, identity.Difficulty);
                return Results.Conflict(new
                {
                    added = false,
                    error = $"Genesis diferente: o nó nasceu da dificuldade {identity.Difficulty} " +
                            $"(esta rede usa {blockchain.Difficulty}) e nenhum bloco dele valeria aqui."
                });
            }

            // Mesmo NODE_ID: é este próprio nó por outro endereço (localhost x 127.0.0.1) ou
            // dois nós com a mesma identidade — logs, métricas e traces ficariam ambíguos.
            if (string.Equals(identity.Node, node.NodeId, StringComparison.Ordinal))
            {
                return Results.Conflict(new
                {
                    added = false,
                    error = $"NODE_ID repetido ('{identity.Node}'): é este próprio nó ou dois nós com a mesma identidade."
                });
            }

            var addition = registry.Add(url);
            if (addition == PeerAddition.Full)
            {
                return Results.Json(new
                {
                    added = false,
                    error = $"Lista de peers cheia ({PeerRegistry.MaxPeers})."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (addition == PeerAddition.Added)
            {
                telemetry.PeerMembership("added");
                logger.LogInformation("Peer {Peer} ({Node}) entrou na lista. Agora são {Count} peers.",
                    url, identity.Node, registry.Count);
            }
            else
            {
                logger.LogInformation("Peer {Peer} ({Node}) se reanunciou.", url, identity.Node);
            }

            // A resposta é a "carta de boas-vindas": o genesis deste nó (o recém-chegado
            // confere do lado dele) e os peers conhecidos — é por ela que o nó novo descobre
            // o resto da rede a partir de uma única semente.
            return Results.Ok(new PeerWelcome(
                Added: addition == PeerAddition.Added,
                Node: node.NodeId,
                Genesis: blockchain.GenesisHash,
                Peers: [.. registry.GetPeers().Where(peer =>
                    !string.Equals(peer, url, StringComparison.OrdinalIgnoreCase))]));
        });

        // Despedida: o nó que vai desligar avisa (DELETE /peers?url=...). Idempotente — 200
        // mesmo se ele já não estava na lista; quem caiu sem avisar sai pelas falhas de conexão.
        group.MapDelete("/", (string url, PeerRegistry registry, NodeTelemetry telemetry,
            ILoggerFactory loggerFactory) =>
        {
            if (!NodeOptions.TryNormalizeUrl(url, out var peer))
                return Results.BadRequest(new { removed = false, error = "url inválida." });

            var removed = registry.Remove(peer);
            if (removed)
            {
                telemetry.PeerMembership("removed");
                loggerFactory.CreateLogger("Peers").LogInformation(
                    "Peer {Peer} se despediu e saiu da lista. Agora são {Count} peers.", peer, registry.Count);
            }

            return Results.Ok(new { removed, peers = registry.GetPeers() });
        });
    }
}
