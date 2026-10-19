using SatusBlockchain.Gateway.Nodes;

namespace SatusBlockchain.Gateway.Runtime;

/// <summary>
/// Sem plataforma: o gateway só conhece os nós de <c>GATEWAY_NODES</c> e os que eles listam
/// como peers. É o modo de quem roda os nós com <c>dotnet run</c>, sem Docker — o console
/// mostra e opera a rede, mas não cria nem derruba nós.
/// </summary>
public sealed class NullNodeRuntime : INodeRuntime
{
    public string Name => "none";

    public bool CanManage => false;

    public Task<IReadOnlyList<RuntimeNode>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RuntimeNode>>([]);

    public Task<RuntimeNode> CreateAsync(NodeSpec spec, CancellationToken cancellationToken = default) =>
        throw NotSupported();

    public Task StopAsync(string id, bool graceful, CancellationToken cancellationToken = default) =>
        throw NotSupported();

    public Task StartAsync(string id, CancellationToken cancellationToken = default) => throw NotSupported();

    public Task RemoveAsync(string id, CancellationToken cancellationToken = default) => throw NotSupported();

    private static NodeOperationException NotSupported() => new(StatusCodes.Status501NotImplemented,
        "Criar, parar e remover nós exige o Docker (o console está sem acesso ao socket).");
}
