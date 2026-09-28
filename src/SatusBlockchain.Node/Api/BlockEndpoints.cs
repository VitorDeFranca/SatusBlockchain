using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;

namespace SatusBlockchain.Node.Api;

/// <summary>
/// Endpoints de mineração e de recebimento de blocos (etapa 6: minar PROPAGA, receber
/// ACEITA ou RECUSA — as regras de aceitação ficam no domínio, em Blockchain.AddBlock).
/// </summary>
public static class BlockEndpoints
{
    public static void MapBlockEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/blocks").WithTags("Blocks");

        // Minera sob demanda com o que está na mempool e propaga o bloco aos peers
        // (push: POST /blocks/receive). Um peer offline não derruba a mineração local:
        // o PeerClient trata cada falha individualmente e apenas registra no log.
        group.MapPost("/mine", async (Blockchain blockchain, Mempool mempool, PeerClient peerClient) =>
        {
            var pending = mempool.GetPending();

            // Pode retornar null se a cadeia avançar durante a mineração
            // (outro bloco chegou por push) — nesse caso a transação continua pendente.
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

            await peerClient.BroadcastBlockAsync(block);
            return Results.Ok(block);
        });

        // Recebe um bloco propagado por outro nó. O corpo é o bloco CRU — o mesmo shape
        // de um item de GET /chain, o que permite copiar um bloco de um nó e postar no outro.
        group.MapPost("/receive", (Block block, Blockchain blockchain, Mempool mempool,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Blocks");

            if (!blockchain.AddBlock(block))
            {
                // 409 (conflito com o estado atual), não 400: o bloco está bem formado,
                // só não encaixa na cadeia local AGORA — duplicado, órfão (índice fora de
                // sequência) ou adulterado (hash/PoW não conferem, ver Blockchain.IsValidBlock).
                logger.LogWarning("Bloco {Index} recusado: não estende a cadeia local.", block.Index);
                return Results.Conflict(new
                {
                    accepted = false,
                    length = blockchain.Length,
                    error = "Bloco não estende a cadeia local (duplicado, órfão ou inválido)."
                });
            }

            // Transações confirmadas em OUTRO nó saem da mempool local. Sem isso este nó
            // re-mineraria as mesmas transações e criaria um fork desnecessário.
            if (block.Transactions is not null)
                mempool.Remove(block.Transactions);

            logger.LogInformation("Bloco {Index} recebido de um peer. Cadeia com {Length} blocos.",
                block.Index, blockchain.Length);
            return Results.Ok(new { accepted = true, index = block.Index, length = blockchain.Length });
        });
    }
}
