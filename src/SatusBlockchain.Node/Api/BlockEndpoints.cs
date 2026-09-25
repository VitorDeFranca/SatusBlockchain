using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Api;

/// <summary>Endpoints de mineração. A propagação entre nós entra na etapa 6.</summary>
public static class BlockEndpoints
{
    public static void MapBlockEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/blocks").WithTags("Blocks");

        // Minera sob demanda com o que está na mempool (~1s na dificuldade 4).
        group.MapPost("/mine", (Blockchain blockchain, Mempool mempool) =>
        {
            var pending = mempool.GetPending();

            // Pode retornar null se a cadeia avançar durante a mineração
            // (outro bloco chegou) — nesse caso a transação continua pendente.
            var block = blockchain.MineBlock(pending);
            if (block is null)
            {
                return Results.Conflict(new
                {
                    error = "A cadeia mudou durante a mineração. Tente minerar novamente."
                });
            }

            // Só remove da mempool o que de fato entrou no bloco.
            mempool.Remove(pending);
            return Results.Ok(block);
        });
    }
}