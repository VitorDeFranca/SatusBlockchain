using System.Net.Http.Json;
using System.Text.Json;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Networking;

/// <summary>
/// Comunicação de SAÍDA do nó (etapa 6): envia o bloco recém-minerado aos peers
/// configurados em PEERS — o "push" da blockchain.
///
/// Tolerância a falhas: um peer offline, mudo ou que recuse o bloco NÃO pode derrubar
/// a mineração local. Cada envio é isolado (uma falha vira log + resultado
/// "inacessível") e os envios acontecem em PARALELO, para que um peer lento não
/// atrase os demais. Quem decide aceitar o bloco é o peer receptor (RF-09).
///
/// O pull (consultar a cadeia dos peers) é assunto da etapa 7 e vive no POST /sync.
/// </summary>
public class PeerClient(HttpClient httpClient, NodeOptions options, ILogger<PeerClient> logger)
{
    /// <summary>
    /// Propaga o bloco para todos os peers. Nó sem peers (lista vazia) devolve lista
    /// vazia: comportamento idêntico ao de antes da propagação existir.
    /// </summary>
    public async Task<IReadOnlyList<PeerResult>> BroadcastBlockAsync(
        Block block, CancellationToken cancellationToken = default)
    {
        var sends = options.Peers
            .Select(peer => SendBlockAsync(peer, block, cancellationToken));

        return await Task.WhenAll(sends);
    }

    private async Task<PeerResult> SendBlockAsync(string peer, Block block, CancellationToken cancellationToken)
    {
        var endpoint = $"{peer}/blocks/receive";

        try
        {
            // JsonSerializerOptions.Web = camelCase: o corpo sai no MESMO formato do
            // GET /chain (um bloco cru), e não em PascalCase do serializador padrão.
            using var response = await httpClient.PostAsJsonAsync(
                endpoint, block, JsonSerializerOptions.Web, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Bloco {Index} propagado para {Peer}.", block.Index, peer);
                return new PeerResult(peer, Accepted: true, $"{(int)response.StatusCode}");
            }

            // 409: o peer já tem esse bloco ou ele não estende a cadeia de lá (peer que
            // ficou para trás, por exemplo — situação que o POST /sync da etapa 7 resolve).
            logger.LogWarning("Peer {Peer} recusou o bloco {Index}: {Status}.",
                peer, block.Index, (int)response.StatusCode);
            return new PeerResult(peer, Accepted: false, $"{(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // HttpRequestException: conexão recusada/DNS. TaskCanceledException: o
            // timeout do HttpClient estourou (peer no ar, mas travado).
            logger.LogWarning("Peer {Peer} inacessível: {Motivo}", peer, ex.Message);
            return new PeerResult(peer, Accepted: false, "inacessível");
        }
    }
}
