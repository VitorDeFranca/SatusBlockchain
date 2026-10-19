namespace SatusBlockchain.Node.Lab;

/// <summary>
/// Cabeçalhos que marcam uma requisição como tráfego ENTRE NÓS (etapa 12). Quem os põe é o
/// <see cref="Networking.PeerHeadersHandler"/>, no <c>HttpClient</c> do <c>PeerClient</c>; quem
/// os lê é o <see cref="PeerTrafficMiddleware"/>, na entrada de cada nó.
/// </summary>
public static class PeerTraffic
{
    /// <summary>Presente em toda requisição de nó para nó (push, gossip, pull, anúncio, despedida).</summary>
    public const string PeerHeader = "X-Satus-Peer";

    /// <summary>Grupo de partição de quem enviou (ausente = sem partição).</summary>
    public const string PartitionHeader = "X-Satus-Partition";
}

/// <summary>
/// Aplica o laboratório na entrada do nó: requisição de peer de OUTRO grupo de partição é
/// recusada com 503; e, se o nó está "lento", toda requisição de peer espera antes de ser
/// atendida. Requisições sem <see cref="PeerTraffic.PeerHeader"/> (console, Postman, curl)
/// passam direto — o operador continua enxergando os dois lados da partição.
/// </summary>
public sealed class PeerTrafficMiddleware(RequestDelegate next, LabState lab, ILogger<PeerTrafficMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(PeerTraffic.PeerHeader))
        {
            await next(context);
            return;
        }

        var sender = context.Request.Headers[PeerTraffic.PartitionHeader].ToString();

        if (!lab.SameSide(sender))
        {
            logger.LogInformation(
                "Partição simulada: {Method} {Path} de um peer do grupo {Sender} recusado (este nó: {Mine}).",
                context.Request.Method, context.Request.Path, Describe(sender), Describe(lab.Partition));

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = $"Partição simulada: este nó ({Describe(lab.Partition)}) não aceita peers de {Describe(sender)}."
            });
            return;
        }

        if (lab.DelayMs > 0)
        {
            try
            {
                await Task.Delay(lab.DelayMs, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // O peer desistiu (timeout de 2 s) antes do atraso acabar: nada a responder.
                return;
            }
        }

        await next(context);
    }

    private static string Describe(string? partition) =>
        string.IsNullOrEmpty(partition) ? "sem partição" : $"grupo {partition}";
}
