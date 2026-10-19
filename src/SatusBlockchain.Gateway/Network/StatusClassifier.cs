namespace SatusBlockchain.Gateway.Network;

/// <summary>O que o classificador precisa saber de cada nó que respondeu.</summary>
public sealed record ChainObservation(string Id, string? Genesis, ChainView Chain);

/// <summary>
/// Compara as cadeias dos nós e diz quem está onde. Função pura (sem rede, sem relógio): a regra
/// de "convergida / atrasada / fork" mora aqui, em um lugar só, e a UI só a exibe.
///
/// A cadeia LÍDER é a que o consenso do projeto escolheria — a válida mais longa
/// (<c>Blockchain.TryReplaceChain</c>). Num empate de tamanho com blocos finais diferentes (um
/// fork de verdade), o gateway desempata pelo hash mais comum e, persistindo o empate, pelo
/// menor: a escolha é arbitrária, mas estável entre polls (a tela não pisca), e o que importa é
/// o fork aparecer como <see cref="NodeStatus.Diverged"/>.
/// </summary>
public static class StatusClassifier
{
    /// <param name="leaderChain">
    /// Hashes da cadeia líder, em ordem (etapa 12). Sem ela, toda cadeia mais curta é "atrasada";
    /// com ela, a mais curta cujo topo NÃO está na líder é um fork — os blocos próprios dela
    /// vão virar órfãos no sync, e é isso que o laboratório quer mostrar.
    /// </param>
    public static IReadOnlyDictionary<string, NodeStatus> Classify(IReadOnlyList<ChainObservation> observations,
        IReadOnlyList<string>? leaderChain = null)
    {
        var statuses = new Dictionary<string, NodeStatus>();
        if (observations.Count == 0)
            return statuses;

        // 1. Genesis: o da maioria é "a rede". Quem tem outro nunca vai convergir com ela.
        var genesis = observations
            .Where(o => o.Genesis is not null)
            .GroupBy(o => o.Genesis!)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();

        // 2. Os nós "da rede": genesis compatível (ou desconhecido — não dá para acusar) e cadeia válida.
        var network = new List<ChainObservation>();
        foreach (var observation in observations)
        {
            if (observation.Genesis is not null && genesis is not null && observation.Genesis != genesis)
                statuses[observation.Id] = NodeStatus.Incompatible;
            else if (!observation.Chain.Valid)
                statuses[observation.Id] = NodeStatus.Invalid;
            else
                network.Add(observation);
        }

        if (network.Count == 0)
            return statuses;

        // 3. Cadeia líder: a mais longa; no empate, o último bloco mais comum.
        var maxLength = network.Max(o => o.Chain.Length);
        var leaderHash = LeaderHash(network, maxLength);

        foreach (var observation in network)
        {
            statuses[observation.Id] =
                observation.Chain.Length < maxLength ? (OnLeaderChain(observation, leaderChain) ? NodeStatus.Behind : NodeStatus.Diverged)
                : observation.Chain.LatestHash == leaderHash ? NodeStatus.Ok
                : NodeStatus.Diverged;
        }

        return statuses;
    }

    /// <summary>
    /// Um nó que está na cadeia líder — de quem o gateway busca a cadeia completa quando precisa
    /// separar "atrasado" de "fork". Null se ninguém está nela.
    /// </summary>
    public static string? LeaderId(IReadOnlyList<ChainObservation> observations,
        IReadOnlyDictionary<string, NodeStatus> statuses) =>
        observations.FirstOrDefault(o => statuses.GetValueOrDefault(o.Id) == NodeStatus.Ok)?.Id;

    /// <summary>Tamanho da cadeia líder entre os nós da rede (0 se nenhum é válido e compatível).</summary>
    public static int MaxLength(IReadOnlyList<ChainObservation> observations,
        IReadOnlyDictionary<string, NodeStatus> statuses) =>
        observations
            .Where(o => statuses.TryGetValue(o.Id, out var status)
                && status is NodeStatus.Ok or NodeStatus.Behind or NodeStatus.Diverged)
            .Select(o => o.Chain.Length)
            .DefaultIfEmpty(0)
            .Max();

    private static string LeaderHash(IEnumerable<ChainObservation> network, int maxLength) =>
        network
            .Where(o => o.Chain.Length == maxLength)
            .GroupBy(o => o.Chain.LatestHash)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .First();

    /// <summary>
    /// O topo de uma cadeia mais curta está na líder, na mesma posição? Então ela é só um
    /// prefixo: o nó está atrasado. Sem a cadeia líder não dá para saber — fica "atrasado".
    /// </summary>
    private static bool OnLeaderChain(ChainObservation observation, IReadOnlyList<string>? leaderChain) =>
        leaderChain is null
        || (observation.Chain.Length <= leaderChain.Count
            && leaderChain[observation.Chain.Length - 1] == observation.Chain.LatestHash);
}
