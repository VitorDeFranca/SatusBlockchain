namespace SatusBlockchain.Gateway.Runtime;

/// <summary>Papel do nó na rede: semente do compose ou nó que entrou em execução (etapa 10).</summary>
public enum NodeRole
{
    Seed,
    Dynamic
}

/// <summary>O que a plataforma (Docker) sabe do nó, sem perguntar a ele.</summary>
public enum RuntimeNodeState
{
    Running,
    Stopped
}

/// <summary>Um nó conhecido pela plataforma onde ele roda.</summary>
/// <param name="Id">Nome do container (= <c>NODE_ID</c> nos nós do compose e nos criados pelo console).</param>
/// <param name="Url">Endereço pelo qual o gateway chama o nó, ex.: <c>http://node1:8080</c>.</param>
/// <param name="HostPort">Porta publicada no host (o <c>localhost:8081</c> do node2), se houver.</param>
public sealed record RuntimeNode(string Id, string Url, RuntimeNodeState State, NodeRole Role, int? HostPort);

/// <summary>O que o console decidiu para um nó novo (nome, semente, dificuldade, porta).</summary>
/// <param name="Seed">Endereço INTERNO da semente — o nó novo se anuncia a ela ao subir.</param>
/// <param name="Difficulty">Copiada de um nó vivo: com outra, o genesis seria outro e o handshake recusaria.</param>
public sealed record NodeSpec(string Name, string Seed, int Difficulty, int? HostPort);

/// <summary>
/// A plataforma onde os nós rodam. É a única parte do gateway que sabe o que é um container;
/// o resto (quem pode ser removido, qual a semente, qual o próximo nome) fica no
/// <c>NodeLifecycle</c>, que funciona igual com qualquer implementação.
/// </summary>
public interface INodeRuntime
{
    /// <summary><c>docker</c> ou <c>none</c> — vai para o snapshot e para <c>/api/capabilities</c>.</summary>
    string Name { get; }

    /// <summary>A plataforma sabe criar, parar, religar e remover nós?</summary>
    bool CanManage { get; }

    Task<IReadOnlyList<RuntimeNode>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Cria e liga um nó novo. Ao subir, ele se anuncia à semente e sincroniza sozinho.</summary>
    Task<RuntimeNode> CreateAsync(NodeSpec spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Para o nó. <paramref name="graceful"/>: SIGTERM (<c>docker stop</c>) — o nó se despede
    /// dos peers; senão SIGKILL (<c>docker kill</c>) — uma queda, ninguém é avisado.
    /// </summary>
    Task StopAsync(string id, bool graceful, CancellationToken cancellationToken = default);

    Task StartAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Apaga o container (que já deve estar parado).</summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);
}
