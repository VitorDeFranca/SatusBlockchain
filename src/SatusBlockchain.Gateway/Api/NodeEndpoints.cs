using SatusBlockchain.Gateway.Network;
using SatusBlockchain.Gateway.Nodes;

namespace SatusBlockchain.Gateway.Api;

/// <summary>
/// Operações sobre UM nó, endereçado pelo id que a tela mostra (<c>node2</c>):
/// <list type="bullet">
///   <item><b>repassadas</b> à API do nó (<see cref="NodeProxy"/>): blocos, mempool, transação, minerar, sync;</item>
///   <item><b>de plataforma</b> (<see cref="NodeLifecycle"/>): criar, parar, derrubar, religar, remover.</item>
/// </list>
/// </summary>
public static class NodeEndpoints
{
    public static void MapNodeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/nodes").WithTags("Nodes");

        // ---- leitura (painel do nó e explorador) ----
        // Sem `last`: a cadeia inteira (explorador, etapa 12). Com `last`: só o topo (painel).
        group.MapGet("/{id}/chain", (string id, int? last, NetworkService network, NodeProxy proxy,
                CancellationToken cancellationToken) =>
            WithNodeAsync(id, network, cancellationToken,
                node => proxy.ChainAsync(node, last is null ? null : Math.Clamp(last.Value, 1, 50), cancellationToken)));

        group.MapGet("/{id}/pending", (string id, NetworkService network, NodeProxy proxy,
                CancellationToken cancellationToken) =>
            WithNodeAsync(id, network, cancellationToken,
                node => proxy.SendAsync(node, HttpMethod.Get, "/transactions/pending", null, cancellationToken)));

        // ---- operações repassadas ao nó (status e corpo voltam como o nó respondeu) ----
        group.MapPost("/{id}/transactions", (string id, HttpRequest request, NetworkService network,
                NodeProxy proxy, CancellationToken cancellationToken) =>
            ForwardBodyAsync(id, request, network, proxy, HttpMethod.Post, "/transactions", cancellationToken));

        group.MapPost("/{id}/mine", (string id, NetworkService network, NodeProxy proxy,
                CancellationToken cancellationToken) =>
            WithNodeAsync(id, network, cancellationToken,
                node => proxy.SendAsync(node, HttpMethod.Post, "/blocks/mine", null, cancellationToken)));

        group.MapPost("/{id}/sync", (string id, NetworkService network, NodeProxy proxy,
                CancellationToken cancellationToken) =>
            WithNodeAsync(id, network, cancellationToken,
                node => proxy.SendAsync(node, HttpMethod.Post, "/sync", null, cancellationToken)));

        // ---- explorador e laboratório (etapa 12), também repassados ----
        // O nó explica o que faria com o bloco (hash recalculado, PoW, encaixe) sem anexá-lo.
        group.MapPost("/{id}/blocks/inspect", (string id, HttpRequest request, NetworkService network,
                NodeProxy proxy, CancellationToken cancellationToken) =>
            ForwardBodyAsync(id, request, network, proxy, HttpMethod.Post, "/blocks/inspect", cancellationToken));

        // Entrega um bloco ao nó como um peer faria — é assim que o bloco adulterado é recusado (409).
        group.MapPost("/{id}/blocks/receive", (string id, HttpRequest request, NetworkService network,
                NodeProxy proxy, CancellationToken cancellationToken) =>
            ForwardBodyAsync(id, request, network, proxy, HttpMethod.Post, "/blocks/receive", cancellationToken));

        group.MapPut("/{id}/lab/partition", (string id, HttpRequest request, NetworkService network,
                NodeProxy proxy, CancellationToken cancellationToken) =>
            ForwardBodyAsync(id, request, network, proxy, HttpMethod.Put, "/lab/partition", cancellationToken));

        group.MapPut("/{id}/lab/delay", (string id, HttpRequest request, NetworkService network,
                NodeProxy proxy, CancellationToken cancellationToken) =>
            ForwardBodyAsync(id, request, network, proxy, HttpMethod.Put, "/lab/delay", cancellationToken));

        group.MapDelete("/{id}/lab", (string id, NetworkService network, NodeProxy proxy,
                CancellationToken cancellationToken) =>
            WithNodeAsync(id, network, cancellationToken,
                node => proxy.SendAsync(node, HttpMethod.Delete, "/lab", null, cancellationToken)));

        // ---- ciclo de vida (Docker) ----
        group.MapPost("/", (CreateNodeRequest? request, NodeLifecycle lifecycle, CancellationToken cancellationToken) =>
            GuardAsync(async () =>
            {
                var created = await lifecycle.CreateAsync(request ?? new CreateNodeRequest(null, null), cancellationToken);
                return Results.Created($"/api/nodes/{created.Id}", created);
            }));

        group.MapPost("/{id}/stop", (string id, string? mode, NodeLifecycle lifecycle, CancellationToken cancellationToken) =>
            GuardAsync(async () =>
            {
                await lifecycle.StopAsync(id, mode, cancellationToken);
                return Results.Ok(new { id, mode = mode ?? "graceful" });
            }));

        group.MapPost("/{id}/start", (string id, NodeLifecycle lifecycle, CancellationToken cancellationToken) =>
            GuardAsync(async () =>
            {
                await lifecycle.StartAsync(id, cancellationToken);
                return Results.Ok(new { id });
            }));

        group.MapDelete("/{id}", (string id, NodeLifecycle lifecycle, CancellationToken cancellationToken) =>
            GuardAsync(async () =>
            {
                await lifecycle.RemoveAsync(id, cancellationToken);
                return Results.Ok(new { id, removed = true });
            }));
    }

    /// <summary>
    /// Acha o nó antes de repassar a operação: 404 se o console não o conhece, 409 se ele está
    /// parado (não há a quem repassar — religue-o antes).
    /// </summary>
    private static async Task<IResult> WithNodeAsync(string id, NetworkService network,
        CancellationToken cancellationToken, Func<NodeView, Task<IResult>> action)
    {
        var node = await network.FindAsync(id, cancellationToken);

        if (node is null)
            return Results.NotFound(new { error = $"Nó '{id}' não encontrado na rede." });

        if (node.State == NodeState.Stopped)
            return Results.Conflict(new { error = $"{node.Id} está parado: religue-o antes." });

        return await action(node);
    }

    /// <summary>Repassa ao nó o corpo JSON recebido, como veio (quem valida é o nó).</summary>
    private static async Task<IResult> ForwardBodyAsync(string id, HttpRequest request, NetworkService network,
        NodeProxy proxy, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);

        return await WithNodeAsync(id, network, cancellationToken,
            node => proxy.SendAsync(node, method, path, json, cancellationToken));
    }

    /// <summary>Erros previstos das operações de plataforma viram JSON com o status certo.</summary>
    private static async Task<IResult> GuardAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (NodeOperationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
    }
}
