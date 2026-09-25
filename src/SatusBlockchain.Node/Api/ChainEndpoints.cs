using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Api;

/// <summary>Endpoints de leitura e validação da cadeia local do nó.</summary>
public static class ChainEndpoints
{
    public static void MapChainEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/chain").WithTags("Chain");

        // Cadeia completa do nó.
        group.MapGet("/", (Blockchain blockchain) => Results.Ok(blockchain.GetChain()));

        // Validação da cadeia: encadeamento, integridade dos hashes e Proof of Work.
        group.MapGet("/validate", (Blockchain blockchain) => Results.Ok(new
        {
            valid = blockchain.IsValid(),
            length = blockchain.Length,
            latestHash = blockchain.GetLatestBlock().Hash
        }));
    }
}