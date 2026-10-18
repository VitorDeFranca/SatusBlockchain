using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SatusBlockchain.Node;
using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;
using SatusBlockchain.Node.Observability;

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
        var client = new PeerClient(http, Peers(), NullLogger<PeerClient>.Instance, Telemetry());

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
            Peers("http://node2:8080", "http://node3:8080"), NullLogger<PeerClient>.Instance, Telemetry());

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
        var client = new PeerClient(http, Peers("http://node2:8080"), NullLogger<PeerClient>.Instance, Telemetry());

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
            Peers("http://node2:8080", "http://node3:8080"), NullLogger<PeerClient>.Instance, Telemetry());

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
        var client = new PeerClient(http, Peers("http://node2:8080", "http://node3:8080"),
            NullLogger<PeerClient>.Instance, Telemetry());

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
        var client = new PeerClient(http, Peers("http://node2:8080"), NullLogger<PeerClient>.Instance, Telemetry());

        var result = Assert.Single(await client.BroadcastTransactionAsync(new Transaction("alice", "bob", 10m)));

        Assert.False(result.Accepted);
        Assert.Equal("inacessível", result.Detail);
    }

    [Fact]
    public async Task GetChains_LêACadeiaDeCadaPeer()
    {
        // O pull (etapa 8) lê GET /chain — em camelCase, o mesmo formato que a API
        // serializa e que o Hasher usa no cálculo do hash.
        var handler = new StubHandler(_ => Json(JsonSerializer.Serialize(new[] { Block(), Block() }, WebJson)));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Peers("http://node2:8080", "http://node3:8080"),
            NullLogger<PeerClient>.Instance, Telemetry());

        var chains = await client.GetChainsAsync();

        Assert.Equal(2, chains.Count);
        Assert.All(handler.Requests, request => Assert.EndsWith("/chain", request.Url));
        Assert.All(chains, result =>
        {
            Assert.NotNull(result.Chain);
            Assert.Equal(2, result.Chain!.Count);
            Assert.Equal("hash-do-bloco", result.Chain[0].Hash);
        });
    }

    [Fact]
    public async Task GetChains_PeerInacessivel_ReportaNull_SemImpedirOsDemais()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Host == "node2"
                ? throw new HttpRequestException("conexão recusada")
                : Json("[]"));

        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Peers("http://node2:8080", "http://node3:8080"),
            NullLogger<PeerClient>.Instance, Telemetry());

        var chains = await client.GetChainsAsync();

        var inacessivel = Assert.Single(chains, chain => chain.Peer.Contains("node2"));
        Assert.Null(inacessivel.Chain);
        Assert.Equal("inacessível", inacessivel.Detail);

        // O peer seguinte foi lido normalmente: a falha de um não cancela o pull.
        var lido = Assert.Single(chains, chain => chain.Peer.Contains("node3"));
        Assert.NotNull(lido.Chain);
    }

    [Fact]
    public async Task GetChains_RespostaQueNaoECadeia_ViraResultado_E_NaoEstouraExcecao()
    {
        // Respondeu 200 com um JSON que não é uma lista de blocos. Vira resultado, para o
        // POST /sync ignorar este peer em vez de responder 500.
        var handler = new StubHandler(_ => Json("{\"nao\":\"e uma cadeia\"}"));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Peers("http://node2:8080"), NullLogger<PeerClient>.Instance, Telemetry());

        var result = Assert.Single(await client.GetChainsAsync());

        Assert.Null(result.Chain);
        Assert.Equal("JSON inválido", result.Detail);
    }

    [Fact]
    public async Task GetChains_SemPeers_NaoChamaNenhum()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("não deveria chamar"));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Peers(), NullLogger<PeerClient>.Instance, Telemetry());

        var chains = await client.GetChainsAsync();

        Assert.Empty(chains);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Broadcast_PeerRecusa_IncrementaContadorDePush()
    {
        // O desfecho por peer (aceito/recusado/inacessível) só existe dentro do
        // PeerClient — é lá que o contador satus.peer.push é registrado (etapa 9).
        // 409 é recusa do peer; "inacessível" é falha de rede: tags diferentes.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var http = new HttpClient(handler);
        var telemetry = Telemetry();
        using var spy = new MetricSpy(telemetry);
        var client = new PeerClient(http, Peers("http://node2:8080"),
            NullLogger<PeerClient>.Instance, telemetry);

        await client.BroadcastBlockAsync(Block());

        Assert.Equal(1, spy.TotalWithTags("satus.peer.push",
            ("kind", "block"), ("outcome", "refused")));
    }

    [Theory]
    [InlineData(false)] // conexão recusada / DNS
    [InlineData(true)]  // timeout — é como um nó morto aparece em muitas redes (e no Windows)
    public async Task Broadcast_SemRespostaTresVezes_TiraOPeerDaLista(bool timeout)
    {
        // Etapa 10 — detecção de falha: um peer que ENTROU em execução (não é semente) e não
        // responde três vezes seguidas foi embora sem se despedir: sai da lista.
        var handler = new StubHandler(_ =>
        {
            if (timeout)
                throw new TaskCanceledException("timeout");
            throw new HttpRequestException("conexão recusada");
        });
        using var http = new HttpClient(handler);
        var registry = Peers();
        registry.Add("http://node4:8080");
        var telemetry = Telemetry();
        using var spy = new MetricSpy(telemetry);
        var client = new PeerClient(http, registry, NullLogger<PeerClient>.Instance, telemetry);

        await client.BroadcastBlockAsync(Block());
        await client.BroadcastBlockAsync(Block());
        Assert.True(registry.Contains("http://node4:8080")); // duas falhas ainda não bastam

        await client.BroadcastBlockAsync(Block());

        Assert.False(registry.Contains("http://node4:8080"));
        Assert.Equal(1, spy.TotalWithTags("satus.peer.membership", ("change", "evicted")));

        // Fora da lista, o peer não é mais procurado: o próximo push nem tenta.
        await client.BroadcastBlockAsync(Block());
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Broadcast_SementeNuncaSaiDaListaPorFalha()
    {
        // Sementes (PEERS) são configuração do operador e o caminho de volta depois de uma
        // queda ou partição: falham, aparecem como "inacessível", mas continuam na lista.
        var handler = new StubHandler(_ => throw new HttpRequestException("conexão recusada"));
        using var http = new HttpClient(handler);
        var registry = Peers("http://node2:8080");
        var client = new PeerClient(http, registry, NullLogger<PeerClient>.Instance, Telemetry());

        for (var i = 0; i < PeerRegistry.MaxConsecutiveFailures + 2; i++)
            await client.BroadcastBlockAsync(Block());

        Assert.True(registry.Contains("http://node2:8080"));
    }

    [Fact]
    public async Task Broadcast_UmaRespostaNoMeio_ZeraAContagem()
    {
        // "Seguidas" é a palavra-chave: qualquer resposta (até um 409) prova que está vivo.
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 3
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            : throw new HttpRequestException("conexão recusada"));
        using var http = new HttpClient(handler);
        var registry = Peers();
        registry.Add("http://node4:8080");
        var client = new PeerClient(http, registry, NullLogger<PeerClient>.Instance, Telemetry());

        // falha, falha, RESPOSTA, falha, falha → nunca chegou a três seguidas.
        for (var i = 0; i < 5; i++)
            await client.BroadcastBlockAsync(Block());

        Assert.True(registry.Contains("http://node4:8080"));
    }

    [Fact]
    public async Task Announce_Aceito_DevolveGenesisEPeersDoOutroNo()
    {
        // O anúncio (etapa 10) vai no corpo de POST /peers; a resposta é a "carta de
        // boas-vindas", com a qual o nó novo descobre o resto da rede.
        var welcome = new PeerWelcome(true, "node1", "hash-do-genesis", ["http://node2:8080"]);
        var handler = new StubHandler(_ => Json(JsonSerializer.Serialize(welcome, WebJson)));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Peers(), NullLogger<PeerClient>.Instance, Telemetry());

        var reply = await client.AnnounceAsync("http://node1:8080", "http://node4:8080");

        Assert.NotNull(reply.Value);
        Assert.Equal("hash-do-genesis", reply.Value!.Genesis);
        Assert.Equal(["http://node2:8080"], reply.Value.Peers);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("http://node1:8080/peers", request.Url);
        Assert.Equal("http://node4:8080",
            JsonSerializer.Deserialize<PeerAnnouncement>(request.Body, WebJson)!.Url);
    }

    [Fact]
    public async Task Announce_Recusado_ViraResultadoComOStatus()
    {
        // 409 = o peer achou este nó incompatível: vira resultado, quem decide o que fazer
        // (tirar o peer da lista) é o MembershipService.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Peers(), NullLogger<PeerClient>.Instance, Telemetry());

        var reply = await client.AnnounceAsync("http://node1:8080", "http://node4:8080");

        Assert.Null(reply.Value);
        Assert.Equal("409", reply.Detail);
    }

    [Fact]
    public async Task BroadcastLeave_EnviaDeleteComOProprioEnderecoATodosOsPeers()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var client = new PeerClient(http, Peers("http://node1:8080", "http://node2:8080"),
            NullLogger<PeerClient>.Instance, Telemetry());

        var results = await client.BroadcastLeaveAsync("http://node4:8080");

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Accepted));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("DELETE", request.Method);
            Assert.EndsWith("/peers?url=http://node4:8080", Uri.UnescapeDataString(request.Url));
        });
    }

    /// <summary>
    /// Telemetria real para o construtor do <see cref="PeerClient"/> (etapa 9): instância
    /// barata (genesis na dificuldade 2), sem exportador — os testes leem os contadores
    /// com um <c>MeterListener</c> quando o desfecho é o que está sob teste.
    /// </summary>
    private static NodeTelemetry Telemetry() => new(new Blockchain(2), new Mempool(), Peers());

    /// <summary>Registro com as sementes informadas — como o PEERS de um nó (etapa 10).</summary>
    private static PeerRegistry Peers(params string[] peers) => new(new NodeOptions("node-test", 2, peers));

    /// <summary>Resposta 200 com corpo JSON (o media type não importa: o PeerClient
    /// desserializa o corpo, não negocia content-type).</summary>
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    };

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
        public List<(string Url, string Body, string Method)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (Requests)
                Requests.Add((request.RequestUri!.ToString(), body, request.Method.Method));

            return respond(request);
        }
    }
}
