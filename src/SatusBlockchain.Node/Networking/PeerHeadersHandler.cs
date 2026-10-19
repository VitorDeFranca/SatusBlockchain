using SatusBlockchain.Node.Lab;

namespace SatusBlockchain.Node.Networking;

/// <summary>
/// Marca toda requisição do <see cref="PeerClient"/> como tráfego entre nós (etapa 12): o
/// cabeçalho <c>X-Satus-Peer</c> e, se este nó está numa partição simulada, o grupo dele em
/// <c>X-Satus-Partition</c>. O grupo é lido no momento do envio — mudar a partição vale para a
/// próxima requisição, sem recriar cliente nenhum.
/// </summary>
public sealed class PeerHeadersHandler(LabState lab) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(PeerTraffic.PeerHeader, "1");

        if (lab.Partition is { } partition)
            request.Headers.TryAddWithoutValidation(PeerTraffic.PartitionHeader, partition);

        return base.SendAsync(request, cancellationToken);
    }
}
