using System.Text.RegularExpressions;
using SatusBlockchain.Gateway.Network;
using SatusBlockchain.Gateway.Runtime;

namespace SatusBlockchain.Gateway.Nodes;

/// <summary>Corpo de <c>POST /api/nodes</c>: tudo opcional — sem nada, o console escolhe.</summary>
public sealed record CreateNodeRequest(string? Name, int? HostPort);

/// <summary>O nó criado e as escolhas feitas para ele (mostradas no registro de operações).</summary>
public sealed record NodeCreated(string Id, string Url, int? HostPort, string Seed, int Difficulty);

/// <summary>
/// As regras de entrar e sair da rede pelo console — independentes de Docker: a plataforma
/// (<see cref="INodeRuntime"/>) só executa. É o equivalente, com cliques, aos scripts
/// <c>add-node.ps1</c> e <c>remove-node.ps1</c>, com duas proteções a mais:
/// <list type="bullet">
///   <item>a <b>dificuldade é copiada de um nó vivo</b> — com outra, o genesis seria outro e o
///   handshake recusaria o nó novo (a armadilha registrada na etapa 6);</item>
///   <item>a <b>semente é um nó na cadeia líder</b>, e não um nó atrasado ou fora do ar.</item>
/// </list>
/// </summary>
public partial class NodeLifecycle(INodeRuntime runtime, NetworkService network, ILogger<NodeLifecycle> logger)
{
    public async Task<NodeCreated> CreateAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManaged();

        if (request.HostPort is { } port && (port < 1024 || port > 65535))
            throw new NodeOperationException(StatusCodes.Status400BadRequest, "Porta do host inválida (use 1024–65535).");

        var snapshot = await network.GetSnapshotAsync(cancellationToken);
        var platform = await runtime.ListAsync(cancellationToken);
        var taken = platform.Select(node => node.Id).Concat(snapshot.Nodes.Select(node => node.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var name = string.IsNullOrWhiteSpace(request.Name) ? NextName(taken) : request.Name.Trim().ToLowerInvariant();
        if (!NodeName().IsMatch(name))
            throw new NodeOperationException(StatusCodes.Status400BadRequest, $"Nome inválido: '{name}' (use nodeN, ex.: node4).");
        if (taken.Contains(name))
            throw new NodeOperationException(StatusCodes.Status409Conflict, $"Já existe um nó '{name}'.");

        // Semente: um nó da plataforma, de pé e na cadeia líder; sementes do compose primeiro.
        var running = platform.Where(node => node.State == RuntimeNodeState.Running)
            .Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seed = snapshot.Nodes
            .Where(node => running.Contains(node.Id) && node.State == NodeState.Running
                && node.Status == NodeStatus.Ok && node.Difficulty is not null)
            .OrderBy(node => node.Role == NodeRole.Seed ? 0 : 1)
            .ThenBy(node => node.Id, Comparer<string>.Create(NetworkService.NaturalCompare))
            .FirstOrDefault()
            ?? throw new NodeOperationException(StatusCodes.Status409Conflict,
                "Nenhum nó saudável (de pé e na cadeia líder) para servir de semente ao nó novo.");

        var created = await runtime.CreateAsync(
            new NodeSpec(name, seed.Url, seed.Difficulty!.Value, request.HostPort), cancellationToken);

        logger.LogInformation("Nó {Node} criado pelo console (semente {Seed}, dificuldade {Difficulty}).",
            created.Id, seed.Id, seed.Difficulty);

        return new NodeCreated(created.Id, created.Url, created.HostPort, seed.Id, seed.Difficulty.Value);
    }

    /// <param name="mode"><c>graceful</c> (padrão: despedida) ou <c>crash</c> (queda, sem aviso).</param>
    public async Task StopAsync(string id, string? mode, CancellationToken cancellationToken = default)
    {
        EnsureManaged();

        var graceful = mode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "graceful" => true,
            "crash" => false,
            _ => throw new NodeOperationException(StatusCodes.Status400BadRequest, "mode inválido: use graceful ou crash.")
        };

        await runtime.StopAsync(id, graceful, cancellationToken);
        logger.LogInformation("Nó {Node} {Acao} pelo console.", id, graceful ? "parado (com despedida)" : "derrubado (queda)");
    }

    public async Task StartAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureManaged();
        await runtime.StartAsync(id, cancellationToken);
        logger.LogInformation("Nó {Node} religado pelo console.", id);
    }

    /// <summary>
    /// Remove um nó extra: primeiro o para COM despedida (sai das listas dos outros na hora),
    /// depois apaga o container. Sementes do compose não são removidas — só param e religam.
    /// </summary>
    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureManaged();

        var node = (await runtime.ListAsync(cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new NodeOperationException(StatusCodes.Status404NotFound, $"Nó '{id}' não encontrado.");

        if (node.Role == NodeRole.Seed)
        {
            throw new NodeOperationException(StatusCodes.Status409Conflict,
                $"{node.Id} é semente do docker-compose: pode ser parado e religado, não removido.");
        }

        if (node.State == RuntimeNodeState.Running)
            await runtime.StopAsync(node.Id, graceful: true, cancellationToken);

        await runtime.RemoveAsync(node.Id, cancellationToken);
        logger.LogInformation("Nó {Node} removido pelo console.", node.Id);
    }

    /// <summary>O próximo <c>nodeN</c> livre: um a mais que o maior N em uso.</summary>
    public static string NextName(IEnumerable<string> taken)
    {
        var highest = taken
            .Select(id => NodeNumber().Match(id))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

        return $"node{highest + 1}";
    }

    private void EnsureManaged()
    {
        if (!runtime.CanManage)
        {
            throw new NodeOperationException(StatusCodes.Status501NotImplemented,
                "Criar, parar e remover nós exige o Docker (o console está sem acesso ao socket).");
        }
    }

    [GeneratedRegex(@"^node\d+$")]
    private static partial Regex NodeName();

    [GeneratedRegex(@"^node(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NodeNumber();
}
