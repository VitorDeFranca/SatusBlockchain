using System.Text;
using System.Text.Json.Nodes;
using SatusBlockchain.Gateway.Network;

namespace SatusBlockchain.Gateway.Nodes;

/// <summary>A resposta de um nó, crua: status e corpo como ele mandou.</summary>
public sealed record NodeReply(int Status, string Body)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    public IResult ToResult() => Results.Content(string.IsNullOrWhiteSpace(Body) ? "{}" : Body,
        "application/json", Encoding.UTF8, Status);
}

/// <summary>
/// Repassa as operações da UI (transação, minerar, sync, laboratório) ao nó, SEM traduzir nada:
/// status e corpo voltam como o nó respondeu. Um 409 "a cadeia mudou durante a mineração" ou o
/// relatório do sync chegam à tela exatamente como a API dos nós os define — o gateway não cria
/// uma segunda versão das regras.
///
/// A única resposta própria do gateway é o 502, quando o nó nem respondeu.
/// </summary>
public class NodeProxy(HttpClient http)
{
    public async Task<IResult> SendAsync(NodeView node, HttpMethod method, string path, string? json,
        CancellationToken cancellationToken) =>
        (await ForwardAsync(node, method, path, json, cancellationToken)).ToResult();

    /// <summary>Como <see cref="SendAsync"/>, mas devolve a resposta para quem orquestra vários nós.</summary>
    public async Task<NodeReply> ForwardAsync(NodeView node, HttpMethod method, string path, string? json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{node.Url}{path}");
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return new NodeReply((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException)
                                   && !cancellationToken.IsCancellationRequested)
        {
            return Unreachable(node);
        }
    }

    /// <summary>
    /// A cadeia do nó: inteira (explorador, etapa 12) ou só os últimos <paramref name="last"/>
    /// blocos (o painel do nó mostra o topo da cadeia).
    /// </summary>
    public async Task<IResult> ChainAsync(NodeView node, int? last, CancellationToken cancellationToken)
    {
        var reply = await ForwardAsync(node, HttpMethod.Get, "/chain", null, cancellationToken);
        if (last is null || !reply.IsSuccess)
            return reply.ToResult();

        try
        {
            if (JsonNode.Parse(reply.Body) is not JsonArray chain)
                return Unreachable(node).ToResult();

            var tail = new JsonArray([.. chain.Skip(Math.Max(0, chain.Count - last.Value)).Select(block => block?.DeepClone())]);
            return Results.Content(tail.ToJsonString(), "application/json", Encoding.UTF8);
        }
        catch (System.Text.Json.JsonException)
        {
            return Unreachable(node).ToResult();
        }
    }

    private static NodeReply Unreachable(NodeView node) => new(StatusCodes.Status502BadGateway,
        System.Text.Json.JsonSerializer.Serialize(new { error = $"{node.Id} não respondeu ({node.Url})." }));
}
