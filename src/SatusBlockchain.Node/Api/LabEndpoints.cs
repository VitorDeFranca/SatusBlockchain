using SatusBlockchain.Node.Lab;

namespace SatusBlockchain.Node.Api;

/// <summary>Corpo de <c>PUT /lab/partition</c>: o grupo do nó, ou null para sair da partição.</summary>
public sealed record PartitionRequest(string? Group);

/// <summary>Corpo de <c>PUT /lab/delay</c>: atraso, em ms, nas respostas aos peers.</summary>
public sealed record DelayRequest(int Ms);

/// <summary>
/// Laboratório de falhas (etapa 12): injeta partição e lentidão no tráfego entre nós. É
/// didático e sem autenticação, como o resto da API — o efeito vale até o nó reiniciar ou
/// alguém chamar <c>DELETE /lab</c>.
/// </summary>
public static class LabEndpoints
{
    public static void MapLabEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/lab").WithTags("Lab");

        group.MapGet("/", (NodeOptions node, LabState lab) => Results.Ok(State(node, lab)));

        // Põe o nó num grupo de partição: a partir daí ele só fala com peers do mesmo grupo.
        group.MapPut("/partition", (PartitionRequest request, NodeOptions node, LabState lab,
            ILoggerFactory loggerFactory) =>
        {
            try
            {
                lab.SetPartition(request.Group);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            loggerFactory.CreateLogger("Lab").LogWarning(
                "Laboratório: partição agora é {Partition}.", lab.Partition ?? "nenhuma (rede inteira)");
            return Results.Ok(State(node, lab));
        });

        // Deixa o nó lento para os peers (acima de 2 s, eles o veem como caído).
        group.MapPut("/delay", (DelayRequest request, NodeOptions node, LabState lab, ILoggerFactory loggerFactory) =>
        {
            try
            {
                lab.SetDelay(request.Ms);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            loggerFactory.CreateLogger("Lab").LogWarning("Laboratório: atraso para peers agora é {Delay} ms.", lab.DelayMs);
            return Results.Ok(State(node, lab));
        });

        // Volta ao normal: sem partição e sem atraso.
        group.MapDelete("/", (NodeOptions node, LabState lab, ILoggerFactory loggerFactory) =>
        {
            lab.Reset();
            loggerFactory.CreateLogger("Lab").LogWarning("Laboratório: falhas removidas (sem partição, sem atraso).");
            return Results.Ok(State(node, lab));
        });
    }

    private static object State(NodeOptions node, LabState lab) =>
        new { node = node.NodeId, partition = lab.Partition, delayMs = lab.DelayMs };
}
