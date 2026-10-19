using System.Text.RegularExpressions;
using SatusBlockchain.Gateway.Runtime;

namespace SatusBlockchain.Gateway.Network;

/// <summary>
/// Monta o <see cref="NetworkSnapshot"/>: descobre quais nós existem, pergunta a cada um como
/// ele está (em paralelo) e compara as cadeias. Um nó fora do ar NUNCA derruba o snapshot — ele
/// aparece como <see cref="NodeState.Unreachable"/>, que é justamente o que a tela quer mostrar.
///
/// Descoberta, em duas fontes que se somam:
/// <list type="number">
///   <item>a <b>plataforma</b> (<see cref="INodeRuntime"/>): todos os containers do projeto,
///   inclusive os parados — que ela sabe e o nó, parado, não pode contar;</item>
///   <item>a <b>lista estática</b> (<c>GATEWAY_NODES</c>) + as <b>listas de peers</b> dos nós
///   que respondem, em rodadas (busca em largura): basta um endereço para achar a rede toda.</item>
/// </list>
/// </summary>
public partial class NetworkService(NodeProbe probe, INodeRuntime runtime, GatewayOptions options,
    TimeProvider clock, ILogger<NetworkService> logger)
{
    /// <summary>Limite de rodadas da busca pelas listas de peers (cada rodada acha os vizinhos novos).</summary>
    private const int MaxRounds = 5;

    /// <summary>Teto de nós por snapshot (a lista de peers de cada nó já é limitada a 32).</summary>
    private const int MaxNodes = 64;

    private NetworkSnapshot? _last;

    public async Task<NetworkSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        // ---- 1. Descoberta pela plataforma ----
        var known = new Dictionary<string, RuntimeNode?>(StringComparer.OrdinalIgnoreCase);
        var stopped = new List<RuntimeNode>();

        foreach (var node in await ListRuntimeAsync(cancellationToken))
        {
            if (node.State == RuntimeNodeState.Stopped)
                stopped.Add(node);
            else
                known[node.Url] = node;
        }

        // ---- 2. Lista estática ----
        foreach (var url in options.StaticNodes)
            known.TryAdd(url, null);

        // Nós parados não são consultados, e as listas de peers não devem "redescobri-los".
        var skipped = new HashSet<string>(stopped.Select(node => node.Url), StringComparer.OrdinalIgnoreCase);

        // ---- 3. Probe em rodadas: cada rodada consulta os nós novos e acha os vizinhos deles ----
        var probed = new List<Probed>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = known.Keys.ToList();

        for (var round = 0; round < MaxRounds && pending.Count > 0; round++)
        {
            var batch = pending
                .Where(url => !seenUrls.Contains(url) && !skipped.Contains(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxNodes - probed.Count)
                .ToList();
            pending.Clear();

            if (batch.Count == 0)
                break;

            var results = await Task.WhenAll(batch.Select(url => probe.ProbeAsync(url, cancellationToken)));

            for (var i = 0; i < batch.Count; i++)
            {
                var result = results[i];
                seenUrls.Add(batch[i]);
                probed.Add(new Probed(batch[i], known.GetValueOrDefault(batch[i]), result));

                // O endereço que o nó diz ter pode diferir do que o gateway usou para chamá-lo
                // (localhost x 127.0.0.1): marcar os dois como vistos evita consultá-lo duas vezes.
                if (result.AdvertiseUrl is not null)
                    seenUrls.Add(result.AdvertiseUrl);

                foreach (var peer in result.Peers)
                {
                    if (!seenUrls.Contains(peer) && !skipped.Contains(peer) && known.TryAdd(peer, null))
                        pending.Add(peer);
                }
            }
        }

        var snapshot = await BuildAsync(probed, stopped, cancellationToken);
        _last = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Acha um nó pelo id. Usa o último snapshot (o que a tela está mostrando) e só monta um novo
    /// se o nó não estiver nele — a UI sempre opera sobre um nó que acabou de ver.
    /// </summary>
    public async Task<NodeView?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var node = _last?.Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
        if (node is not null)
            return node;

        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<RuntimeNode>> ListRuntimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await runtime.ListAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Docker indisponível não pode derrubar a tela: segue só com a lista estática e as
            // listas de peers (o mesmo que o modo sem Docker).
            logger.LogWarning(ex, "Não foi possível listar os nós pela plataforma ({Runtime}).", runtime.Name);
            return [];
        }
    }

    private async Task<NetworkSnapshot> BuildAsync(List<Probed> probed, List<RuntimeNode> stopped,
        CancellationToken cancellationToken)
    {
        // Um nó chamado por dois endereços (ex.: localhost e 127.0.0.1) responde igual nos dois:
        // fica o primeiro. Só deduplica quem respondeu — sem resposta não há como saber.
        var groups = probed
            .GroupBy(item => item.Result.Reachable ? item.Result.NodeId ?? item.Url : item.Url,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        var entries = groups.Select(group => group.First()).ToList();

        var observations = entries
            .Where(item => item.Result.Reachable)
            .Select(item => new ChainObservation(item.Id, item.Result.Identity?.Genesis, item.Chain!))
            .ToList();

        var statuses = StatusClassifier.Classify(observations);

        // Etapa 12: cadeia mais curta pode ser só atraso (o topo dela está na líder) ou um fork
        // (não está). Separar os dois exige a cadeia da líder — buscada só quando há atrasado.
        if (statuses.Values.Contains(NodeStatus.Behind)
            && StatusClassifier.LeaderId(observations, statuses) is { } leaderId
            && entries.FirstOrDefault(item => item.Id == leaderId) is { } leader
            && await probe.GetChainHashesAsync(leader.Url, cancellationToken) is { } leaderChain)
        {
            statuses = StatusClassifier.Classify(observations, leaderChain);
        }

        // endereço -> id, para ligar a lista de peers de cada nó aos nós que o gateway conhece.
        // Todos os endereços de um mesmo nó (o que o gateway usou e o que ele diz ter) apontam
        // para o id do representante do grupo.
        var idByUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var id = group.First().Id;
            foreach (var item in group)
            {
                idByUrl[item.Url] = id;
                if (item.Result.AdvertiseUrl is not null)
                    idByUrl[item.Result.AdvertiseUrl] = id;
            }
        }
        foreach (var node in stopped)
            idByUrl[node.Url] = node.Id;

        // O que se sabia de cada endereço no snapshot anterior: um nó que acabou de cair não
        // responde mais quem é, mas continua sendo o mesmo nó.
        var previous = _last?.Nodes
            .GroupBy(node => node.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var views = new List<NodeView>();

        foreach (var item in entries)
        {
            var result = item.Result;

            // Sem a plataforma, quem se anuncia (ADVERTISE_URL) é dinâmico; quem não, semente.
            // Quem não respondeu não diz se se anuncia: fica o papel que tinha antes.
            var role = item.Runtime?.Role
                ?? (result.Reachable
                    ? (result.AdvertiseUrl is null ? NodeRole.Seed : NodeRole.Dynamic)
                    : previous?.GetValueOrDefault(item.Url)?.Role ?? NodeRole.Seed);

            views.Add(new NodeView(
                item.Id,
                item.Url,
                item.Runtime?.HostPort,
                result.Reachable ? NodeState.Running : NodeState.Unreachable,
                result.Reachable ? statuses.GetValueOrDefault(item.Id, NodeStatus.Unknown) : NodeStatus.Unknown,
                role,
                result.Identity?.Difficulty,
                result.Identity?.Genesis,
                item.Chain,
                result.Mempool,
                result.AdvertiseUrl,
                [.. result.Peers.Select(peer => new PeerRef(peer, idByUrl.GetValueOrDefault(peer)))],
                result.LatencyMs,
                result.Lab is { } lab ? new LabView(lab.Partition, lab.DelayMs) : null));
        }

        foreach (var node in stopped)
        {
            views.Add(new NodeView(node.Id, node.Url, node.HostPort, NodeState.Stopped, NodeStatus.Unknown,
                node.Role, null, null, null, null, null, [], null));
        }

        views.Sort((a, b) => NaturalCompare(a.Id, b.Id));

        var running = views.Where(view => view.State == NodeState.Running).ToList();

        var summary = new NetworkSummary(
            Total: views.Count,
            Reachable: running.Count,
            MaxLength: StatusClassifier.MaxLength(observations, statuses),
            Converged: running.Count > 0 && running.All(view => view.Status == NodeStatus.Ok));

        return new NetworkSnapshot(clock.GetUtcNow(), runtime.Name == "docker" ? "docker" : "static",
            summary, views);
    }

    /// <summary>Ordem "natural": <c>node2</c> antes de <c>node10</c> (a alfabética os inverteria).</summary>
    internal static int NaturalCompare(string a, string b)
    {
        var left = TrailingDigits().Match(a);
        var right = TrailingDigits().Match(b);

        if (left.Success && right.Success
            && string.Equals(a[..left.Index], b[..right.Index], StringComparison.OrdinalIgnoreCase)
            && long.TryParse(left.Value, out var leftNumber)
            && long.TryParse(right.Value, out var rightNumber)
            && leftNumber != rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingDigits();

    /// <summary>Um nó consultado: o endereço usado, o que a plataforma sabe dele e a resposta.</summary>
    private sealed record Probed(string Url, RuntimeNode? Runtime, ProbeResult Result)
    {
        /// <summary>
        /// O id que a tela mostra: o do container quando há (é o NODE_ID dos nós do compose),
        /// senão o que o próprio nó diz, senão o endereço.
        /// </summary>
        public string Id => Runtime?.Id ?? Result.NodeId ?? Url;

        public ChainView? Chain => Result.Chain is { } chain
            ? new ChainView(chain.Length, chain.LatestHash, chain.Valid)
            : null;
    }
}
