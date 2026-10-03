using System.Net.Http.Json;
using System.Text.Json;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Networking;

/// <summary>
/// Comunicação de SAÍDA do nó (etapas 6 e 7): o **push** do bloco minerado e o **gossip**
/// das transações, para os peers configurados em PEERS.
///
/// Tolerância a falhas: um peer offline, mudo ou que recuse o payload NÃO pode derrubar a
/// operação local (minerar ou aceitar uma transação). Cada envio é isolado — uma falha vira
/// log + resultado "inacessível" — e os envios acontecem em PARALELO, para que um peer
/// lento não atrase os demais. Quem decide aceitar é o peer receptor: bloco via
/// `Blockchain.AddBlock`, transação via validação + dedup da mempool.
///
/// O **pull** (ler a cadeia dos peers, etapa 8) também mora aqui, em
/// <see cref="GetChainsAsync"/>, e é acionado pelo POST /sync: o push leva o bloco novo,
/// mas nunca a HISTÓRIA que um nó perdeu enquanto estava fora do ar.
/// </summary>
public class PeerClient(HttpClient httpClient, NodeOptions options, ILogger<PeerClient> logger)
{
    private const string Unreachable = "inacessível";

    /// <summary>
    /// Propaga o bloco recém-minerado para todos os peers (push, etapa 6). Nó sem peers
    /// (lista vazia) devolve lista vazia: comportamento idêntico a não existir propagação.
    /// </summary>
    public async Task<IReadOnlyList<PeerResult>> BroadcastBlockAsync(
        Block block, CancellationToken cancellationToken = default)
    {
        var results = await BroadcastAsync("/blocks/receive", block, cancellationToken);
        LogResults(results, $"Bloco {block.Index}");
        return results;
    }

    /// <summary>
    /// Propaga a transação recém-recebida para todos os peers (gossip, etapa 7). É o que
    /// permite postar a transação em UM nó e ter qualquer outro incluindo-a em um bloco.
    /// </summary>
    public async Task<IReadOnlyList<PeerResult>> BroadcastTransactionAsync(
        Transaction transaction, CancellationToken cancellationToken = default)
    {
        var results = await BroadcastAsync("/transactions/receive", transaction, cancellationToken);
        LogResults(results, $"a transação {transaction.From}->{transaction.To}");
        return results;
    }

    /// <summary>
    /// Lê a cadeia de cada peer em paralelo (pull, etapa 8) — a origem dos dados do
    /// <c>POST /sync</c>. Um peer ausente, lento ou com resposta ruim vira resultado
    /// (<c>Chain == null</c>), nunca exceção.
    /// </summary>
    public async Task<IReadOnlyList<PeerChain>> GetChainsAsync(CancellationToken cancellationToken = default)
    {
        var reads = options.Peers.Select(peer => GetChainAsync(peer, cancellationToken));
        var chains = await Task.WhenAll(reads);

        foreach (var chain in chains)
        {
            if (chain.Chain is not null)
                logger.LogInformation("Peer {Peer} respondeu com {Length} blocos.", chain.Peer, chain.Chain.Count);
            else
                logger.LogWarning("Não foi possível ler a cadeia do peer {Peer}: {Motivo}.", chain.Peer, chain.Detail);
        }

        return chains;
    }

    private async Task<PeerChain> GetChainAsync(string peer, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"{peer}/chain", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new PeerChain(peer, null, $"{(int)response.StatusCode}");

            // JsonSerializerOptions.Web = camelCase: é o mesmo formato que GET /chain
            // serializa E que o Hasher usa no cálculo do hash. Desserializar em outra
            // convenção faria a recontagem do hash não bater, e a cadeia pareceria inválida.
            var chain = await response.Content.ReadFromJsonAsync<List<Block>>(
                JsonSerializerOptions.Web, cancellationToken);

            return chain is null
                ? new PeerChain(peer, null, "corpo vazio")
                : new PeerChain(peer, chain, $"{chain.Count} blocos");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Peer {Peer} inacessível: {Motivo}", peer, ex.Message);
            return new PeerChain(peer, null, Unreachable);
        }
        catch (JsonException ex)
        {
            // Respondeu 200 com algo que não é uma cadeia. Vira resultado, para o sync
            // simplesmente ignorar este peer em vez de estourar exceção no endpoint.
            logger.LogWarning("Cadeia ilegível do peer {Peer}: {Motivo}", peer, ex.Message);
            return new PeerChain(peer, null, "JSON inválido");
        }
    }

    /// <summary>
    /// Envia o mesmo payload a todos os peers, em paralelo: um peer lento (ou fora do ar)
    /// não atrasa nem cancela o envio aos demais.
    /// </summary>
    private async Task<IReadOnlyList<PeerResult>> BroadcastAsync(
        string path, object payload, CancellationToken cancellationToken)
    {
        var sends = options.Peers.Select(peer => SendAsync(peer, path, payload, cancellationToken));

        return await Task.WhenAll(sends);
    }

    private async Task<PeerResult> SendAsync(string peer, string path, object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            // JsonSerializerOptions.Web = camelCase: o corpo sai no MESMO formato que a API
            // expõe (bloco cru de GET /chain, transação crua de POST /transactions), e não
            // em PascalCase, que é o padrão do serializador.
            using var response = await httpClient.PostAsJsonAsync(
                $"{peer}{path}", payload, JsonSerializerOptions.Web, cancellationToken);

            return new PeerResult(peer, response.IsSuccessStatusCode, $"{(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // HttpRequestException: conexão recusada/DNS. TaskCanceledException: o
            // timeout do HttpClient estourou (peer no ar, mas travado).
            logger.LogWarning("Peer {Peer} inacessível: {Motivo}", peer, ex.Message);
            return new PeerResult(peer, Accepted: false, Unreachable);
        }
    }

    /// <summary>
    /// Traduz cada envio em log (nunca em exceção) — é o que permite demonstrar a
    /// tolerância a falhas pela saída do container. O caso "inacessível" já foi logado
    /// com o motivo em <see cref="SendAsync"/>, então não se repete aqui.
    /// </summary>
    private void LogResults(IReadOnlyList<PeerResult> results, string subject)
    {
        foreach (var result in results)
        {
            if (result.Accepted)
            {
                logger.LogInformation("Peer {Peer} aceitou {Subject} ({Status}).",
                    result.Peer, subject, result.Detail);
                continue;
            }

            // 409: o peer já tem esse payload ou ele não se encaixa no estado de lá —
            // por exemplo, um nó que ficou para trás e ainda vai sincronizar (etapa 8).
            if (result.Detail != Unreachable)
                logger.LogWarning("Peer {Peer} recusou {Subject} ({Status}).",
                    result.Peer, subject, result.Detail);
        }
    }
}
