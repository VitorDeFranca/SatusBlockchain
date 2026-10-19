using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Networking;

/// <summary>
/// Ciclo de vida do nó NA REDE (etapa 10): entrar ao subir e se despedir ao desligar.
///
/// Só age quando o nó tem <c>ADVERTISE_URL</c> — sem saber o próprio endereço, ele não tem
/// o que anunciar. Um nó sem ele se comporta como nas etapas 6–9 (lista = <c>PEERS</c>),
/// mas continua ACEITANDO quem se anuncia a ele: é o papel das sementes (node1..node3).
///
/// Não é um <c>BackgroundService</c>: nada roda em laço, nada consulta a rede sozinho de
/// tempos em tempos. São dois ganchos do ciclo de vida do processo — "subiu" e "vai
/// desligar" — mais o reanúncio que o <c>POST /sync</c> dispara. Mantém a filosofia da
/// etapa 8: a rede só muda quando alguém (operador, processo) faz algo.
/// </summary>
public class MembershipService(IServiceScopeFactory scopes, NodeOptions options, PeerRegistry registry,
    Blockchain blockchain, NodeTelemetry telemetry, IHostApplicationLifetime lifetime,
    ILogger<MembershipService> logger) : IHostedService
{
    /// <summary>O nó sabe o próprio endereço e, portanto, participa da membership dinâmica.</summary>
    public bool Enabled => options.AdvertiseUrl is not null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            logger.LogInformation("Sem ADVERTISE_URL: o nó não se anuncia (lista inicial = PEERS).");
            return Task.CompletedTask;
        }

        // O anúncio só pode sair DEPOIS de o servidor abrir a porta: quem recebe o anúncio
        // chama este nó de volta (handshake em GET /). ApplicationStarted dispara exatamente
        // aí; Task.Run tira o trabalho de dentro do callback, que não deve bloquear.
        lifetime.ApplicationStarted.Register(() =>
            _ = Task.Run(() => JoinAsync(lifetime.ApplicationStopping)));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Desligamento gracioso (SIGTERM do <c>docker stop</c>, Ctrl+C): avisa os peers para
    /// tirarem este nó da lista. Um <c>docker kill</c> pula esta etapa — e é aí que entra a
    /// detecção de falha do <see cref="PeerRegistry"/>.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
            return;

        logger.LogInformation("Desligando: despedida para {Count} peers.", registry.Count);

        using var scope = scopes.CreateScope();
        var peerClient = scope.ServiceProvider.GetRequiredService<PeerClient>();
        await peerClient.BroadcastLeaveAsync(options.AdvertiseUrl!, cancellationToken);
    }

    /// <summary>
    /// Entrar na rede = anunciar-se (e descobrir os peers dos peers) + sincronizar a cadeia.
    /// O sync é a parte que o push não cobre: um nó novo nasce só com o genesis, e nenhum
    /// push reenvia o passado. Nunca lança — falhar ao entrar deixa o nó sozinho, não morto.
    /// </summary>
    public async Task JoinAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var answered = await AnnounceAsync(cancellationToken);
            if (answered == 0)
            {
                logger.LogWarning(
                    "Nenhum peer aceitou o anúncio: o nó segue isolado (as sementes de PEERS continuam na lista).");
                return;
            }

            using var scope = scopes.CreateScope();
            var synchronizer = scope.ServiceProvider.GetRequiredService<ChainSynchronizer>();
            var report = await synchronizer.SyncAsync(cancellationToken);

            logger.LogInformation(
                "Entrada na rede concluída: {Peers} peers na lista, cadeia com {Length} blocos.",
                registry.Count, report.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // O nó começou a desligar no meio da entrada: nada a fazer.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha inesperada ao entrar na rede.");
        }
    }

    /// <summary>
    /// Anuncia este nó a cada peer conhecido E aos que eles revelarem — busca em largura a
    /// partir das sementes: basta UM endereço em <c>PEERS</c> para o nó chegar a todos.
    ///
    /// Também é o reanúncio do <c>POST /sync</c>: quem volta de uma queda ou partição pode
    /// ter sido removido da lista dos outros, e se anunciar de novo o devolve a elas.
    /// </summary>
    /// <returns>Quantos peers aceitaram o anúncio (0 se o nó não tem ADVERTISE_URL).</returns>
    public async Task<int> AnnounceAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled)
            return 0;

        var self = options.AdvertiseUrl!;
        using var scope = scopes.CreateScope();
        var peerClient = scope.ServiceProvider.GetRequiredService<PeerClient>();

        var pending = new Queue<string>(registry.GetPeers());
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { self };
        var answered = 0;

        // O teto evita que uma resposta maliciosa (lista enorme) vire uma varredura sem fim.
        while (visited.Count <= PeerRegistry.MaxPeers && pending.TryDequeue(out var peer))
        {
            if (!visited.Add(peer))
                continue;

            var reply = await peerClient.AnnounceAsync(peer, self, cancellationToken);
            var welcome = reply.Value;

            if (welcome is null)
            {
                Explain(reply);
                continue;
            }

            // O peer já conferiu o NOSSO genesis antes de aceitar; conferir o DELE fecha o
            // handshake nos dois sentidos (protege de um peer com defeito ou malicioso).
            if (welcome.Genesis != blockchain.GenesisHash)
            {
                Forget(peer, "genesis diferente");
                continue;
            }

            answered++;
            if (registry.Add(peer) == PeerAddition.Added)
                telemetry.PeerMembership("added");

            foreach (var known in welcome.Peers)
            {
                if (NodeOptions.TryNormalizeUrl(known, out var url) && !visited.Contains(url))
                    pending.Enqueue(url);
            }
        }

        logger.LogInformation("Anúncio como {Self}: {Answered} peers aceitaram; {Count} na lista.",
            self, answered, registry.Count);
        return answered;
    }

    /// <summary>Traduz a recusa de um anúncio em log — e em remoção, quando ela é definitiva.</summary>
    private void Explain(PeerReply<PeerWelcome> reply)
    {
        switch (reply.Detail)
        {
            case "409":
                // Incompatível (genesis/dificuldade diferente) ou NODE_ID repetido: ficar com ele
                // na lista seria empurrar blocos que ele recusa para sempre.
                Forget(reply.Peer, "o peer recusou o anúncio (409: incompatível ou NODE_ID repetido)");
                break;

            case "502":
                // O peer tentou nos chamar de volta e não conseguiu: quase sempre é o
                // ADVERTISE_URL apontando para um endereço que os outros não alcançam.
                logger.LogWarning(
                    "Peer {Peer} não conseguiu chamar este nó em {Self} (502): confira ADVERTISE_URL.",
                    reply.Peer, options.AdvertiseUrl);
                break;

            default:
                // "inacessível" já foi logado pelo PeerClient; 503 = lista do peer cheia.
                if (reply.Detail != PeerClient.Unreachable)
                    logger.LogWarning("Peer {Peer} não aceitou o anúncio ({Detail}).", reply.Peer, reply.Detail);
                break;
        }
    }

    private void Forget(string peer, string reason)
    {
        if (registry.Remove(peer))
            telemetry.PeerMembership("removed");

        logger.LogWarning("Peer {Peer} fora da lista: {Motivo}.", peer, reason);
    }
}
