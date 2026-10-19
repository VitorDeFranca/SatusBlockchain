using System.Text.RegularExpressions;

namespace SatusBlockchain.Node.Lab;

/// <summary>
/// Falhas injetadas de propósito (etapa 12 — laboratório de falhas e fork). Valem só para o
/// tráfego ENTRE NÓS; quem opera o nó de fora (console, Postman, curl) não é afetado — é isso
/// que permite minerar dos dois lados de uma partição e provocar um fork.
/// <list type="bullet">
///   <item><b>Partição</b>: o nó pertence a um grupo e só aceita peers do mesmo grupo. Dois
///   grupos = duas "metades" da rede que não se enxergam, sem desligar nada.</item>
///   <item><b>Lentidão</b>: o nó demora para responder aos peers. Acima de 2 s (o timeout do
///   <c>PeerClient</c>), para os peers ele é indistinguível de um nó caído.</item>
/// </list>
/// Por que não <c>docker network disconnect</c>: cortaria também o console, e o lado isolado
/// não poderia mais ser operado.
/// </summary>
public partial class LabState
{
    /// <summary>Atraso máximo aceito (10 s) — mais que isso só trava testes e demonstrações.</summary>
    public const int MaxDelayMs = 10_000;

    private volatile string? _partition;
    private volatile int _delayMs;

    /// <summary>Grupo de partição deste nó; <c>null</c> = sem partição (a rede inteira).</summary>
    public string? Partition => _partition;

    /// <summary>Atraso aplicado a cada requisição vinda de um peer.</summary>
    public int DelayMs => _delayMs;

    /// <param name="group">Letras, números, <c>-</c> ou <c>_</c> (até 20); vazio ou null = sair da partição.</param>
    public void SetPartition(string? group)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            _partition = null;
            return;
        }

        // Sem paramName de propósito: a mensagem vai para a resposta HTTP como está.
        if (!GroupName().IsMatch(group))
            throw new ArgumentException("Grupo inválido: use letras, números, '-' ou '_' (até 20 caracteres).");

        _partition = group;
    }

    public void SetDelay(int milliseconds)
    {
        if (milliseconds is < 0 or > MaxDelayMs)
            throw new ArgumentException($"Atraso inválido: use de 0 a {MaxDelayMs} ms.");

        _delayMs = milliseconds;
    }

    /// <summary>Volta ao normal: sem partição e sem atraso.</summary>
    public void Reset()
    {
        _partition = null;
        _delayMs = 0;
    }

    /// <summary>O peer está do mesmo lado da partição? (Sem grupo dos dois lados também conta.)</summary>
    public bool SameSide(string? senderPartition) =>
        string.Equals(_partition, string.IsNullOrEmpty(senderPartition) ? null : senderPartition, StringComparison.Ordinal);

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,20}$")]
    private static partial Regex GroupName();
}
