using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SatusBlockchain.Node;
using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;

namespace SatusBlockchain.Node.Tests.Unit;

/// <summary>
/// Testes de unidade da propagação (etapa 6): o <see cref="PeerClient"/> conversa com um
/// <c>HttpMessageHandler</c> de mentira, escrito à mão — sem rede, sem processo e sem
/// pacote de mock. É aqui que a TOLERÂNCIA A FALHAS é verificada de forma determinística:
/// um peer que dá erro não pode impedir o envio aos demais nem lançar para o chamador.
/// </summary>
public class PeerClientTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Broadcast_SemPeers_NaoEnviaNada()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("não deveria enviar"));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Options(), NullLogger<PeerClient>.Instance);

        var results = await client.BroadcastBlockAsync(Block());

        Assert.Empty(results);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Broadcast_EnviaParaTodosOsPeers_NoEndpointDeRecebimento()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http,
            Options("http://node2:8080", "http://node3:8080"), NullLogger<PeerClient>.Instance);

        var results = await client.BroadcastBlockAsync(Block());

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.EndsWith("/blocks/receive", request.Url);

            // O corpo é o bloco cru (camelCase, igual ao GET /chain) — o mesmo que o
            // receptor desserializa em POST /blocks/receive.
            var block = JsonSerializer.Deserialize<Block>(request.Body, WebJson);
            Assert.Equal(1, block!.Index);
            Assert.Single(block.Transactions);
        });
        Assert.All(results, result => Assert.True(result.Accepted));
    }

    [Fact]
    public async Task Broadcast_PeerRecusa_ReportaNaoAceito_SemLancarExcecao()
    {
        // 409 é o que o peer responde para um bloco duplicado/órfão: não é erro de rede
        // e não pode virar exceção na mineração local.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Options("http://node2:8080"), NullLogger<PeerClient>.Instance);

        var result = Assert.Single(await client.BroadcastBlockAsync(Block()));

        Assert.False(result.Accepted);
        Assert.Equal("409", result.Detail);
    }

    [Fact]
    public async Task Broadcast_PeerInacessivel_NaoImpedeOsDemais()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Host == "node2"
                ? throw new HttpRequestException("conexão recusada")
                : new HttpResponseMessage(HttpStatusCode.OK));

        using var http = new HttpClient(handler);
        var client = new PeerClient(http,
            Options("http://node2:8080", "http://node3:8080"), NullLogger<PeerClient>.Instance);

        var results = await client.BroadcastBlockAsync(Block());

        var inacessivel = Assert.Single(results, result => result.Peer.Contains("node2"));
        Assert.False(inacessivel.Accepted);
        Assert.Equal("inacessível", inacessivel.Detail);

        // O peer seguinte foi atendido normalmente: a falha de um não cancela o push.
        var aceito = Assert.Single(results, result => result.Peer.Contains("node3"));
        Assert.True(aceito.Accepted);
    }

    [Fact]
    public async Task BroadcastTransaction_EnviaParaTodosOsPeers_NoEndpointDeRecebimento()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Options("http://node2:8080", "http://node3:8080"),
            NullLogger<PeerClient>.Instance);

        var results = await client.BroadcastTransactionAsync(new Transaction("alice", "bob", 10m));

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.EndsWith("/transactions/receive", request.Url);

            // A transação vai CRUA no corpo (camelCase), como em POST /transactions.
            var transacao = JsonSerializer.Deserialize<Transaction>(request.Body, WebJson);
            Assert.Equal(new Transaction("alice", "bob", 10m), transacao);
        });
        Assert.All(results, result => Assert.True(result.Accepted));
    }

    [Fact]
    public async Task BroadcastTransaction_PeerInacessivel_NaoLancaExcecao()
    {
        // Propagar não pode fazer o POST /transactions falhar: a transação já está na
        // mempool local e o peer pode recebê-la depois (via bloco ou nova tentativa).
        var handler = new StubHandler(_ => throw new HttpRequestException("conexão recusada"));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Options("http://node2:8080"), NullLogger<PeerClient>.Instance);

        var result = Assert.Single(await client.BroadcastTransactionAsync(new Transaction("alice", "bob", 10m)));

        Assert.False(result.Accepted);
        Assert.Equal("inacessível", result.Detail);
    }

    private static NodeOptions Options(params string[] peers) => new("node-test", 2, peers);

    private static Block Block() => new()
    {
        Index = 1,
        Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero),
        Transactions = [new Transaction("alice", "bob", 10m)],
        PreviousHash = "hash-anterior",
        Nonce = 1,
        Hash = "hash-do-bloco"
    };

    /// <summary>
    /// Handler de mentira: registra o que a aplicação enviou e devolve o que o teste
    /// mandar (ou lança, simulando peer inacessível).
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (Requests)
                Requests.Add((request.RequestUri!.ToString(), body));

            return respond(request);
        }
    }
}
