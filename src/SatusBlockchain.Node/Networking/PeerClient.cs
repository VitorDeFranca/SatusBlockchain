using System.Net.Http.Json;
using System.Text.Json;
using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Networking;

/// <summary>
/// Comunicação de SAÍDA do nó: o **push** do bloco minerado, o **gossip** das transações,
/// o **pull** da cadeia e — na etapa 10 — as mensagens de entrada e saída da rede (anúncio,
/// handshake e despedida). Os destinos vêm do <see cref="PeerRegistry"/>, a lista VIVA de
/// peers. Tolerância a falhas: um peer offline, mudo ou que recuse o payload NÃO pode
/// derrubar a operação local (minerar ou aceitar uma transação).
///
/// É aqui também que mora a detecção de falha: qualquer resposta HTTP prova que o peer está
/// vivo; cada conexão recusada conta contra ele no registro, até ele sair da lista.
/// </summary>
public class PeerClient(HttpClient httpClient, PeerRegistry registry, ILogger<PeerClient> logger,
    NodeTelemetry telemetry)
{
    /// <summary><c>Detail</c> dos resultados quando o peer não respondeu (rede, DNS, timeout).</summary>
    public const string Unreachable = "inacessível";

    /// <summary>
    /// Propaga o bloco para todos os peers (push, etapa 6). Nó sem peers (lista vazia)
    /// devolve lista vazia: comportamento idêntico a não existir propagação.
    /// </summary>
    public async Task<IReadOnlyList<PeerResult>> BroadcastBlockAsync(
        Block block, CancellationToken cancellationToken = default)
    {
        var results = await BroadcastAsync("/blocks/receive", block, kind: "block", cancellationToken);
        LogResults(results, $"Bloco {block.Index}");
        return results;
    }

    /// <summary>
    /// Propaga a transação para todos os peers (gossip, etapa 7). É o que permite postar a
    /// transação em UM nó e ter qualquer outro incluindo-a em um bloco.
    /// </summary>
    public async Task<IReadOnlyList<PeerResult>> BroadcastTransactionAsync(
        Transaction transaction, CancellationToken cancellationToken = default)
    {
        var results = await BroadcastAsync("/transactions/receive", transaction, kind: "transaction", cancellationToken);
        LogResults(results, $"a transação {transaction.From}->{transaction.To}");
        return results;
    }

    /// <summary>
    /// Re-propagação (etapa 10): repassa adiante, SEM esperar, um bloco que este nó acabou
    /// de aceitar de um peer. Não esperar é de propósito: se o receptor aguardasse os peers
    /// DELE antes de responder, um nó fora do ar dois saltos adiante (~2s de timeout) viraria
    /// lentidão na resposta ao remetente — que contaria isso como falha DESTE nó e poderia
    /// tirá-lo da lista por engano. Quem minera continua aguardando o próprio push (etapa 6).
    /// </summary>
    public void RelayBlock(Block block) => Detach(BroadcastBlockAsync(block));

    /// <summary>Re-propagação de uma transação aceita na mempool (mesmo raciocínio do bloco).</summary>
    public void RelayTransaction(Transaction transaction) => Detach(BroadcastTransactionAsync(transaction));

    /// <summary>
    /// Lê a cadeia de cada peer em paralelo (pull, etapa 8) — a origem dos dados do
    /// <c>POST /sync</c>. Um peer ausente, lento ou com resposta ruim vira resultado
    /// (<c>Chain == null</c>), nunca exceção.
    /// </summary>
    public async Task<IReadOnlyList<PeerChain>> GetChainsAsync(CancellationToken cancellationToken = default)
    {
        var reads = registry.GetPeers().Select(peer => GetChainAsync(peer, cancellationToken));
        var chains = await Task.WhenAll(reads);

        foreach (var chain in chains)
        {
            if (chain.Chain is not null)
            {
                telemetry.PeerPull("read");
                logger.LogInformation("Peer {Peer} respondeu com {Length} blocos.", chain.Peer, chain.Chain.Count);
            }
            else
            {
                telemetry.PeerPull(chain.Detail == Unreachable ? "unreachable" : "invalid");
                logger.LogWarning("Não foi possível ler a cadeia do peer {Peer}: {Motivo}.", chain.Peer, chain.Detail);
            }
        }

        return chains;
    }

    /// <summary>
    /// Anuncia este nó a UM peer (etapa 10): <c>POST {peer}/peers</c> com o próprio endereço.
    /// Aceito, a resposta traz o genesis do peer e a lista de peers que ele conhece.
    /// </summary>
    public Task<PeerReply<PeerWelcome>> AnnounceAsync(string peer, string self,
        CancellationToken cancellationToken = default) =>
        CallAsync<PeerWelcome>(peer, () => httpClient.PostAsJsonAsync(
            $"{peer}/peers", new PeerAnnouncement(self), JsonSerializerOptions.Web, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Handshake (etapa 10): lê a identidade (<c>GET /</c>) de um nó que pediu para entrar.
    /// Chamar de volta o endereço anunciado prova que ele existe e responde ali.
    /// </summary>
    public Task<PeerReply<NodeIdentity>> GetIdentityAsync(string url,
        CancellationToken cancellationToken = default) =>
        CallAsync<NodeIdentity>(url, () => httpClient.GetAsync($"{url}/", cancellationToken),
            cancellationToken);

    /// <summary>
    /// Despedida (etapa 10): <c>DELETE {peer}/peers?url=...</c> em todos os peers, em
    /// paralelo, quando o nó é desligado. Peer fora do ar aqui não importa — quem não ouviu
    /// a despedida vai remover este nó sozinho, pelas falhas de conexão.
    /// </summary>
    public async Task<IReadOnlyList<PeerResult>> BroadcastLeaveAsync(string self,
        CancellationToken cancellationToken = default)
    {
        var leaves = registry.GetPeers().Select(peer => LeaveAsync(peer, self, cancellationToken));
        var results = await Task.WhenAll(leaves);

        LogResults(results, "a despedida");
        return results;
    }

    private async Task<PeerChain> GetChainAsync(string peer, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"{peer}/chain", cancellationToken);
            registry.RecordSuccess(peer);

            if (!response.IsSuccessStatusCode)
                return new PeerChain(peer, null, $"{(int)response.StatusCode}");

            var chain = await response.Content.ReadFromJsonAsync<List<Block>>(
                JsonSerializerOptions.Web, cancellationToken);

            return chain is null
                ? new PeerChain(peer, null, "corpo vazio")
                : new PeerChain(peer, chain, $"{chain.Count} blocos");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Peer {Peer} inacessível: {Motivo}", peer, ex.Message);
            RecordUnreachable(peer);
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
    /// <param name="kind">Rótulo de domínio fechado da métrica: <c>block</c> ou <c>transaction</c>.</param>
    private async Task<IReadOnlyList<PeerResult>> BroadcastAsync(
        string path, object payload, string kind, CancellationToken cancellationToken)
    {
        var sends = registry.GetPeers().Select(peer => SendAsync(peer, path, payload, kind, cancellationToken));

        return await Task.WhenAll(sends);
    }

    private async Task<PeerResult> SendAsync(string peer, string path, object payload, string kind,
        CancellationToken cancellationToken)
    {
        try
        {
            // JsonSerializerOptions.Web = camelCase: o corpo sai no MESMO formato que a API
            // expõe (bloco cru de GET /chain, transação crua de POST /transactions), e não
            // em PascalCase, que é o padrão do serializador.
            using var response = await httpClient.PostAsJsonAsync(
                $"{peer}{path}", payload, JsonSerializerOptions.Web, cancellationToken);
            registry.RecordSuccess(peer);

            var accepted = response.IsSuccessStatusCode;
            telemetry.PeerPush(kind, accepted, unreachable: false);
            return new PeerResult(peer, accepted, $"{(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // HttpRequestException: conexão recusada/DNS. TaskCanceledException: o
            // timeout do HttpClient estourou (peer no ar, mas travado).
            telemetry.PeerPush(kind, accepted: false, unreachable: true);
            logger.LogWarning("Peer {Peer} inacessível: {Motivo}", peer, ex.Message);
            RecordUnreachable(peer);
            return new PeerResult(peer, Accepted: false, Unreachable);
        }
    }

    private async Task<PeerResult> LeaveAsync(string peer, string self, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.DeleteAsync(
                $"{peer}/peers?url={Uri.EscapeDataString(self)}", cancellationToken);
            return new PeerResult(peer, response.IsSuccessStatusCode, $"{(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Peer {Peer} inacessível: {Motivo}", peer, ex.Message);
            return new PeerResult(peer, Accepted: false, Unreachable);
        }
    }

    /// <summary>
    /// Chamada de membership a UM nó, com o corpo da resposta desserializado. Mesmo
    /// tratamento de falha do push e do pull: vira <see cref="PeerReply{T}"/>, nunca exceção.
    /// </summary>
    private async Task<PeerReply<T>> CallAsync<T>(string peer, Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            using var response = await send();
            registry.RecordSuccess(peer);

            if (!response.IsSuccessStatusCode)
                return new PeerReply<T>(peer, null, $"{(int)response.StatusCode}");

            var value = await response.Content.ReadFromJsonAsync<T>(
                JsonSerializerOptions.Web, cancellationToken);
            return new PeerReply<T>(peer, value, $"{(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Peer {Peer} inacessível: {Motivo}", peer, ex.Message);
            RecordUnreachable(peer);
            return new PeerReply<T>(peer, null, Unreachable);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Resposta ilegível do peer {Peer}: {Motivo}", peer, ex.Message);
            return new PeerReply<T>(peer, null, "JSON inválido");
        }
    }

    /// <summary>
    /// Detecção de falha (etapa 10): toda chamada sem resposta conta contra o peer — conexão
    /// recusada, DNS que não resolve ou timeout. O timeout entra porque é como um nó morto
    /// aparece em muitas redes (e no próprio Windows, onde conectar numa porta fechada não é
    /// recusado na hora). Isso só é seguro porque nenhum receptor faz o remetente esperar
    /// pelos peers DELE (<see cref="RelayBlock"/>): a demora medida aqui é sempre do peer.
    /// </summary>
    private void RecordUnreachable(string peer)
    {
        if (!registry.RecordFailure(peer))
            return;

        telemetry.PeerMembership("evicted");
        logger.LogWarning("Peer {Peer} removido da lista: {Falhas} chamadas seguidas sem resposta.",
            peer, PeerRegistry.MaxConsecutiveFailures);
    }

    /// <summary>
    /// Solta uma tarefa de propagação sem aguardá-la. O broadcast já transforma falha de
    /// rede em resultado; isto só garante que um erro inesperado vire log, não exceção perdida.
    /// </summary>
    private void Detach(Task task) =>
        _ = task.ContinueWith(
            failed => logger.LogError(failed.Exception, "Falha inesperada ao repassar aos peers."),
            TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>
    /// Traduz cada envio em log (nunca em exceção) — é o que permite demonstrar a
    /// tolerância a falhas pela saída do container. O caso "inacessível" já foi logado
    /// com o motivo no envio, então não se repete aqui.
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

            if (result.Detail == Unreachable)
                continue;

            // 409: o peer já tem esse payload ou ele não se encaixa no estado de lá. Com a
            // re-propagação (etapa 10) é o desfecho NORMAL do eco — o mesmo bloco chega por
            // vários caminhos —, por isso é informação, não aviso.
            if (result.Detail == "409")
            {
                logger.LogInformation("Peer {Peer} já tinha ou não encaixou {Subject} ({Status}).",
                    result.Peer, subject, result.Detail);
                continue;
            }

            logger.LogWarning("Peer {Peer} recusou {Subject} ({Status}).",
                result.Peer, subject, result.Detail);
        }
    }
}
