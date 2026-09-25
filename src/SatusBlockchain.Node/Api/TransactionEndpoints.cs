using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Api;

/// <summary>Endpoints da mempool: transações pendentes, aguardando mineração.</summary>
public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/transactions").WithTags("Transactions");

        // Validação mínima didática (sem assinatura, sem saldos).
        group.MapPost("/", (Transaction transaction, Mempool mempool) =>
        {
            if (string.IsNullOrWhiteSpace(transaction.From) ||
                string.IsNullOrWhiteSpace(transaction.To) ||
                transaction.Amount <= 0)
            {
                return Results.BadRequest(new
                {
                    error = "Transação inválida: From, To e Amount > 0 são obrigatórios."
                });
            }

            mempool.Add(transaction);
            return Results.Created("/transactions/pending", transaction);
        });

        group.MapGet("/pending", (Mempool mempool) => Results.Ok(mempool.GetPending()));
    }
}