namespace SatusBlockchain.Node.Networking;

/// <summary>
/// A visão que ESTE nó tem da rede (etapa 10): quem são os peers agora. Até a etapa 9 a
/// lista era o <c>PEERS</c> lido na subida, imutável; aqui ela vira estado — nasce das
/// sementes de <c>PEERS</c> e muda durante a execução:
/// <list type="bullet">
///   <item><b>entra</b> quem se anuncia (<c>POST /peers</c>) e quem o nó descobre ao entrar na rede;</item>
///   <item><b>sai</b> quem se despede (<c>DELETE /peers</c>) e quem para de responder.</item>
/// </list>
///
/// Thread-safe por <c>lock</c>, como <see cref="Core.Mempool"/>: requisições simultâneas
/// leem a lista (push, gossip, sync) enquanto outras a alteram (anúncio, despedida, falha).
///
/// Cada nó tem a SUA lista — como a mempool, membership é estado local, sem consenso.
/// Duas listas diferentes por um tempo não quebram nada: a validade da cadeia é conferida
/// por cada nó, e a re-propagação faz o bloco chegar mesmo a quem não está em todas as listas.
/// </summary>
public class PeerRegistry
{
    /// <summary>
    /// Teto da lista: sem ele, qualquer um poderia registrar milhares de endereços e fazer
    /// este nó disparar um POST para cada um a cada bloco minerado.
    /// </summary>
    public const int MaxPeers = 32;

    /// <summary>
    /// Chamadas seguidas SEM RESPOSTA até o peer sair da lista. Uma só não basta (um
    /// container reiniciando, um pico de rede); três seguidas, sem nenhuma resposta no meio,
    /// é um nó que foi embora sem se despedir.
    /// </summary>
    public const int MaxConsecutiveFailures = 3;

    // Ordem de entrada (para GET /peers ser estável) + contagem de falhas por peer.
    // As chaves do dicionário são o índice de "quem é conhecido".
    private readonly List<string> _peers = [];
    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seeds;
    private readonly string? _self;
    private readonly Lock _lock = new();

    /// <summary>
    /// As sementes vêm de <c>PEERS</c>; o endereço do próprio nó (<c>ADVERTISE_URL</c>), se
    /// aparecer ali por engano de configuração, é ignorado — ninguém é peer de si mesmo.
    /// </summary>
    public PeerRegistry(NodeOptions options)
    {
        _self = options.AdvertiseUrl;
        _seeds = new HashSet<string>(options.Peers, StringComparer.OrdinalIgnoreCase);

        foreach (var seed in options.Peers)
            Add(seed);
    }

    /// <summary>Cópia da lista atual — protege a lista interna (mesmo padrão da Mempool).</summary>
    public IReadOnlyList<string> GetPeers()
    {
        lock (_lock)
            return [.. _peers];
    }

    /// <summary>Quantidade de peers conhecidos — leitura do gauge <c>satus.peers.count</c>.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _peers.Count;
        }
    }

    public bool Contains(string peer)
    {
        lock (_lock)
            return _failures.ContainsKey(peer);
    }

    /// <summary>O endereço é o deste próprio nó (<c>ADVERTISE_URL</c>)?</summary>
    public bool IsSelf(string peer) =>
        _self is not null && string.Equals(peer, _self, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Registra um peer. Espera o endereço já normalizado (<see cref="NodeOptions.TryNormalizeUrl"/>).
    /// Registrar de novo quem já está na lista não é erro: é o nó que voltou e se reanunciou,
    /// então a contagem de falhas dele recomeça do zero.
    /// </summary>
    public PeerAddition Add(string peer)
    {
        lock (_lock)
        {
            if (IsSelf(peer))
                return PeerAddition.Self;

            if (_failures.ContainsKey(peer))
            {
                _failures[peer] = 0;
                return PeerAddition.AlreadyKnown;
            }

            if (_peers.Count >= MaxPeers)
                return PeerAddition.Full;

            _peers.Add(peer);
            _failures[peer] = 0;
            return PeerAddition.Added;
        }
    }

    /// <summary>Tira o peer da lista (despedida, ou handshake que provou incompatibilidade).</summary>
    /// <returns><c>true</c> se ele estava na lista.</returns>
    public bool Remove(string peer)
    {
        lock (_lock)
        {
            if (!_failures.Remove(peer))
                return false;

            _peers.RemoveAll(known => string.Equals(known, peer, StringComparison.OrdinalIgnoreCase));
            return true;
        }
    }

    /// <summary>O peer respondeu (qualquer status HTTP): está vivo, zera a contagem de falhas.</summary>
    public void RecordSuccess(string peer)
    {
        lock (_lock)
        {
            if (_failures.ContainsKey(peer))
                _failures[peer] = 0;
        }
    }

    /// <summary>
    /// O peer não respondeu (conexão recusada, DNS que não resolve, timeout). Na
    /// <see cref="MaxConsecutiveFailures"/>-ésima falha seguida ele sai da lista — é a
    /// detecção de falha de quem caiu sem se despedir.
    ///
    /// <b>Sementes nunca saem por falha</b>: elas são configuração do operador, e é por elas
    /// que um nó volta a enxergar a rede depois de uma queda ou partição. Só saem se se
    /// despedirem (<c>DELETE /peers</c>).
    /// </summary>
    /// <returns><c>true</c> se o peer foi removido agora.</returns>
    public bool RecordFailure(string peer)
    {
        lock (_lock)
        {
            if (!_failures.TryGetValue(peer, out var failures))
                return false;

            _failures[peer] = ++failures;
            if (failures < MaxConsecutiveFailures || _seeds.Contains(peer))
                return false;

            _failures.Remove(peer);
            _peers.RemoveAll(known => string.Equals(known, peer, StringComparison.OrdinalIgnoreCase));
            return true;
        }
    }
}

/// <summary>Desfecho de <see cref="PeerRegistry.Add"/> — um estado por motivo, como <c>ChainUpdate</c>.</summary>
public enum PeerAddition
{
    /// <summary>Entrou na lista agora.</summary>
    Added,

    /// <summary>Já estava na lista (reanúncio): a contagem de falhas foi zerada.</summary>
    AlreadyKnown,

    /// <summary>É o endereço do próprio nó.</summary>
    Self,

    /// <summary>A lista chegou a <see cref="PeerRegistry.MaxPeers"/>.</summary>
    Full
}
