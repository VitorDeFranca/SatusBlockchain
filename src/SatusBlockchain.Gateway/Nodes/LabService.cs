using System.Text.Json;
using SatusBlockchain.Gateway.Network;

namespace SatusBlockchain.Gateway.Nodes;

/// <summary>Corpo de <c>PUT /api/lab/partition</c>: o grupo de cada nó (quem não aparece sai da partição).</summary>
public sealed record PartitionPlan(Dictionary<string, string?>? Groups);

/// <summary>O que UM nó respondeu a uma ação do laboratório.</summary>
/// <param name="Body">O corpo JSON do nó (o relatório do sync, o estado do laboratório...).</param>
public sealed record LabNodeResult(string Node, int Status, JsonElement? Body);

/// <summary>
/// Ações do laboratório que valem para a REDE, não para um nó (etapa 12): aplicar uma partição
/// a todos de uma vez, desfazer as falhas de todos e sincronizar todos. Cada nó continua sendo
/// quem decide (o gateway só repassa), e o resultado de cada um volta separado — um nó fora do
/// ar não impede os outros.
/// </summary>
public class LabService(NetworkService network, NodeProxy proxy)
{
    /// <summary>
    /// Aplica a partição: cada nó em execução recebe o grupo do mapa — ou nenhum, se não está
    /// nele. "Isolar o node3" é <c>{ "node3": "B" }</c>; curar é um mapa vazio.
    /// </summary>
    public async Task<IReadOnlyList<LabNodeResult>> PartitionAsync(IReadOnlyDictionary<string, string?> groups,
        CancellationToken cancellationToken = default)
    {
        var byId = new Dictionary<string, string?>(groups, StringComparer.OrdinalIgnoreCase);
        var nodes = await RunningAsync(cancellationToken);

        var unknown = byId.Keys
            .Where(id => !nodes.Any(node => string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Select(id => new LabNodeResult(id, StatusCodes.Status404NotFound,
                Json(new { error = $"Nó '{id}' não está em execução." })));

        var applied = await Task.WhenAll(nodes.Select(node => RunAsync(node, HttpMethod.Put, "/lab/partition",
            JsonSerializer.Serialize(new { group = byId.GetValueOrDefault(node.Id) }), cancellationToken)));

        return [.. applied, .. unknown];
    }

    /// <summary>Tira todas as falhas injetadas (partição e lentidão) de todos os nós.</summary>
    public async Task<IReadOnlyList<LabNodeResult>> ResetAsync(CancellationToken cancellationToken = default)
    {
        var nodes = await RunningAsync(cancellationToken);
        return await Task.WhenAll(nodes.Select(node => RunAsync(node, HttpMethod.Delete, "/lab", null, cancellationToken)));
    }

    /// <summary>
    /// <c>POST /sync</c> em cada nó, um de cada vez e em ordem: é o "cada nó puxa a cadeia mais
    /// longa dos peers" que faz a rede convergir depois de curar uma partição. Em sequência, o
    /// relatório de cada um reflete o que os anteriores já adotaram.
    /// </summary>
    public async Task<IReadOnlyList<LabNodeResult>> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<LabNodeResult>();
        foreach (var node in await RunningAsync(cancellationToken))
            results.Add(await RunAsync(node, HttpMethod.Post, "/sync", null, cancellationToken));
        return results;
    }

    private async Task<List<NodeView>> RunningAsync(CancellationToken cancellationToken) =>
        [.. (await network.GetSnapshotAsync(cancellationToken)).Nodes.Where(node => node.State == NodeState.Running)];

    private async Task<LabNodeResult> RunAsync(NodeView node, HttpMethod method, string path, string? json,
        CancellationToken cancellationToken)
    {
        var reply = await proxy.ForwardAsync(node, method, path, json, cancellationToken);
        return new LabNodeResult(node.Id, reply.Status, Parse(reply.Body));
    }

    private static JsonElement? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
}
