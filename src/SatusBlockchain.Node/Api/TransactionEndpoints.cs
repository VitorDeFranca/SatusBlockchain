using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Api;

/// <summary>
/// Endpoints da mempool. A etapa 7 acrescenta o **gossip**: a transação postada aqui é
/// empurrada aos peers, e cada nó aceita o que recebe na SUA própria mempool — estado
/// local, como em um cliente real de blockchain (não existe mempool compartilhada).
/// </summary>
public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/transactions").WithTags("Transactions");

        // Validação mínima didática (sem assinatura, sem saldos).
        // Entra na mempool local E é propagada: daí em diante qualquer nó que a tenha
        // recebido pode incluí-la em um bloco (basta minerar nele).
        group.MapPost("/", async (Transaction transaction, Mempool mempool, PeerClient peerClient,
            NodeTelemetry telemetry) =>
        {
            if (!IsValid(transaction))
            {
                telemetry.TransactionReceived(fromPeer: false, accepted: false);
                return InvalidTransaction();
            }

            // Dedup: postar a mesma transação duas vezes não enche a fila duas vezes.
            if (mempool.Add(transaction))
            {
                telemetry.TransactionReceived(fromPeer: false, accepted: true);
                await peerClient.BroadcastTransactionAsync(transaction);
            }
            else
            {
                telemetry.TransactionReceived(fromPeer: false, accepted: false);
            }

            return Results.Created("/transactions/pending", transaction);
        });

        // Recebe uma transação propagada por outro nó (gossip de 1 hop: o receptor NÃO
        // re-propaga — com as listas de PEERS completas, um hop alcança todos os nós).
        group.MapPost("/receive", (Transaction transaction, Mempool mempool, Blockchain blockchain,
            NodeTelemetry telemetry, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Transactions");

            if (!IsValid(transaction))
            {
                telemetry.TransactionReceived(fromPeer: true, accepted: false);
                return InvalidTransaction();
            }

            // O bloco pode chegar ANTES da transação (depende de qual peer responde
            // primeiro). Sem esta checagem, uma transação já confirmada ficaria pendente
            // para sempre na mempool local — e poderia entrar em um segundo bloco.
            if (blockchain.ContainsTransaction(transaction))
            {
                telemetry.TransactionReceived(fromPeer: true, accepted: false);
                logger.LogWarning("Transação {From}->{To} recusada: já está confirmada em um bloco.",
                    transaction.From, transaction.To);
                return Results.Conflict(new
                {
                    accepted = false,
                    error = "Transação já confirmada na cadeia local (não volta para a mempool)."
                });
            }

            if (!mempool.Add(transaction))
            {
                telemetry.TransactionReceived(fromPeer: true, accepted: false);
                logger.LogWarning("Transação {From}->{To} recusada: já está pendente na mempool.",
                    transaction.From, transaction.To);
                return Results.Conflict(new
                {
                    accepted = false,
                    error = "Transação já está pendente na mempool local."
                });
            }

            telemetry.TransactionReceived(fromPeer: true, accepted: true);
            var pending = mempool.GetPending().Count;
            logger.LogInformation("Transação {From}->{To} recebida de um peer. Mempool com {Count} pendentes.",
                transaction.From, transaction.To, pending);

            return Results.Ok(new { accepted = true, pending });
        });

        group.MapGet("/pending", (Mempool mempool) => Results.Ok(mempool.GetPending()));
    }

    /// <summary>Validação mínima didática (sem assinatura, sem saldos).</summary>
    private static bool IsValid(Transaction transaction) =>
        !string.IsNullOrWhiteSpace(transaction.From)
        && !string.IsNullOrWhiteSpace(transaction.To)
        && transaction.Amount > 0;

    private static IResult InvalidTransaction() => Results.BadRequest(new
    {
        error = "Transação inválida: From, To e Amount > 0 são obrigatórios."
    });
}
