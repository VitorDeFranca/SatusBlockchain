using SatusBlockchain.Gateway.Network;
using SatusBlockchain.Gateway.Runtime;

namespace SatusBlockchain.Gateway.Api;

/// <summary>Leitura: a rede inteira num instante e o que este console sabe fazer.</summary>
public static class NetworkEndpoints
{
    public static void MapNetworkEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api").WithTags("Network");

        // O que a UI pede a cada poll (2 s): um snapshot de todos os nós, em vez de N chamadas.
        api.MapGet("/network", (NetworkService network, CancellationToken cancellationToken) =>
            network.GetSnapshotAsync(cancellationToken));

        // A UI esconde o que não existe neste ambiente (sem Docker, nada de criar/derrubar nós).
        api.MapGet("/capabilities", (INodeRuntime runtime, GatewayOptions options) => Results.Ok(new
        {
            runtime = runtime.Name,
            canManageNodes = runtime.CanManage,
            links = options.Links
        }));
    }
}
