using SatusBlockchain.Gateway.Runtime;

namespace SatusBlockchain.Gateway.Network;

/// <summary>Se o nó respondeu ao gateway agora.</summary>
public enum NodeState
{
    /// <summary>Respondeu ao probe.</summary>
    Running,

    /// <summary>Deveria estar de pé, mas não respondeu (caído, travado, partição).</summary>
    Unreachable,

    /// <summary>A plataforma diz que está parado (container <c>exited</c>): nem foi consultado.</summary>
    Stopped
}

/// <summary>
/// Como a cadeia do nó se compara com a da rede. Só faz sentido para quem respondeu; os demais
/// são <see cref="Unknown"/>.
/// </summary>
public enum NodeStatus
{
    /// <summary>Na cadeia líder da rede.</summary>
    Ok,

    /// <summary>Cadeia mais curta que a líder: perdeu blocos (ficou offline, ainda não sincronizou).</summary>
    Behind,

    /// <summary>
    /// Fork: o nó tem blocos que não estão na cadeia líder — mesmo tamanho com outro topo, ou
    /// cadeia mais curta cujo topo não está na líder. Ao sincronizar, esses blocos viram órfãos.
    /// </summary>
    Diverged,

    /// <summary>A própria cadeia não passa na validação (hash, elo ou PoW quebrados).</summary>
    Invalid,

    /// <summary>Genesis diferente do da maioria: outro "universo" (outra dificuldade); nunca vai convergir.</summary>
    Incompatible,

    /// <summary>Sem dados de cadeia (parado ou fora do ar).</summary>
    Unknown
}

/// <summary>Um peer na lista de um nó; <c>NodeId</c> vem preenchido quando o gateway conhece esse nó.</summary>
public sealed record PeerRef(string Url, string? NodeId);

public sealed record ChainView(int Length, string LatestHash, bool Valid);

/// <summary>Falhas injetadas no nó pelo laboratório (etapa 12).</summary>
/// <param name="Partition">Grupo de partição (null = sem partição).</param>
/// <param name="DelayMs">Atraso nas respostas aos peers.</param>
public sealed record LabView(string? Partition, int DelayMs);

/// <param name="Url">Endereço pelo qual o gateway chama o nó (no Docker, o interno: <c>http://node1:8080</c>).</param>
/// <param name="HostPort">Porta publicada no host, para abrir o nó direto (<c>localhost:8081</c>).</param>
/// <param name="Mempool">Transações pendentes na mempool local do nó.</param>
/// <param name="AdvertiseUrl"><c>ADVERTISE_URL</c> do nó (null nos nós que não se anunciam).</param>
/// <param name="LatencyMs">Tempo do probe — o que o gateway "sentiu" ao perguntar ao nó.</param>
/// <param name="Lab">Partição e lentidão injetadas (null se o nó não respondeu).</param>
public sealed record NodeView(
    string Id,
    string Url,
    int? HostPort,
    NodeState State,
    NodeStatus Status,
    NodeRole Role,
    int? Difficulty,
    string? Genesis,
    ChainView? Chain,
    int? Mempool,
    string? AdvertiseUrl,
    IReadOnlyList<PeerRef> Peers,
    long? LatencyMs,
    LabView? Lab = null);

/// <param name="Reachable">Nós que responderam ao probe.</param>
/// <param name="MaxLength">Tamanho da cadeia líder (0 se nenhum nó respondeu).</param>
/// <param name="Converged">Todos os nós que responderam estão na cadeia líder.</param>
public sealed record NetworkSummary(int Total, int Reachable, int MaxLength, bool Converged);

/// <summary>A rede inteira num instante — o que a UI pede a cada poll.</summary>
/// <param name="Mode"><c>docker</c> (nós listados pela plataforma) ou <c>static</c> (lista + peers).</param>
public sealed record NetworkSnapshot(
    DateTimeOffset GeneratedAt,
    string Mode,
    NetworkSummary Summary,
    IReadOnlyList<NodeView> Nodes);
