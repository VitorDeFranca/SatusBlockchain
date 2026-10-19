using SatusBlockchain.Gateway.Nodes;

namespace SatusBlockchain.Gateway.Api;

/// <summary>
/// Laboratório de falhas e fork (etapa 12) — ações sobre a rede inteira. As de um nó só
/// (partição, atraso, inspecionar, entregar um bloco) estão em <see cref="NodeEndpoints"/>.
/// </summary>
public static class LabEndpoints
{
    public static void MapLabEndpoints(this IEndpointRouteBuilder routes)
    {
        var lab = routes.MapGroup("/api/lab").WithTags("Lab");

        // Partição: { "groups": { "node3": "B" } } isola o node3; { "groups": {} } cura tudo.
        lab.MapPut("/partition", async (PartitionPlan plan, LabService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.PartitionAsync(plan.Groups ?? [], cancellationToken)));

        // Desfaz todas as falhas injetadas (partição e lentidão) em todos os nós.
        lab.MapDelete("/", async (LabService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ResetAsync(cancellationToken)));

        // Cada nó puxa a cadeia mais longa dos peers: a convergência depois de curar a partição.
        lab.MapPost("/sync-all", async (LabService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.SyncAllAsync(cancellationToken)));
    }
}
