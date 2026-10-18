using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Tests.Unit;

/// <summary>
/// Testes de unidade do ciclo de vida do nó na rede (etapa 10): o anúncio em largura a
/// partir das sementes e a despedida do desligamento — com um <c>HttpMessageHandler</c> de
/// mentira no lugar da rede. A despedida fica testada aqui porque os testes de integração
/// matam o processo (queda), e não há como mandar um SIGTERM de verdade a um processo
/// filho no Windows.
/// </summary>
public class MembershipServiceTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private const string Self = "http://node4:8080";

    [Fact]
    public async Task StopAsync_ComAdvertiseUrl_DespedeSeDeTodosOsPeers()
    {
        var node = CreateNode(seeds: ["http://node1:8080", "http://node2:8080"], advertise: Self,
            respond: _ => new HttpResponseMessage(HttpStatusCode.OK));

        await node.Membership.StopAsync(CancellationToken.None);

        Assert.Equal(2, node.Requests.Count);
        Assert.All(node.Requests, request =>
        {
            Assert.Equal("DELETE", request.Method);
            Assert.EndsWith($"/peers?url={Self}", Uri.UnescapeDataString(request.Url));
        });
    }

    [Fact]
    public async Task StopAsync_SemAdvertiseUrl_NaoSeDespede()
    {
        // Sem saber o próprio endereço, não há o que pedir para os outros removerem.
        var node = CreateNode(seeds: ["http://node1:8080"], advertise: null,
            respond: _ => throw new InvalidOperationException("não deveria chamar"));

        await node.Membership.StopAsync(CancellationToken.None);

        Assert.Empty(node.Requests);
    }

    [Fact]
    public async Task AnnounceAsync_DescobreOsPeersDaSemente_E_SeAnunciaATodos()
    {
        // A semente conhece o node2 (e já lista o próprio node4, recém-registrado): o
        // anúncio segue para o node2, mas nunca para si mesmo.
        Node? node = null;
        node = CreateNode(seeds: ["http://node1:8080"], advertise: Self, respond: request =>
            request.RequestUri!.Host == "node1"
                ? Welcome(node!.Genesis, "http://node2:8080", Self)
                : Welcome(node!.Genesis, "http://node1:8080"));

        var answered = await node.Membership.AnnounceAsync();

        Assert.Equal(2, answered);
        Assert.Equal(new[] { "http://node1:8080", "http://node2:8080" }, node.Registry.GetPeers());
        Assert.Equal(
            new[] { "http://node1:8080/peers", "http://node2:8080/peers" },
            node.Requests.Select(request => request.Url).Order());
        Assert.All(node.Requests, request => Assert.Equal(Self,
            JsonSerializer.Deserialize<PeerAnnouncement>(request.Body, WebJson)!.Url));
    }

    [Fact]
    public async Task AnnounceAsync_PeerRecusaCom409_SaiDaLista()
    {
        // 409 = incompatível (outro genesis ou NODE_ID repetido): manter o peer seria
        // empurrar para ele blocos que ele recusa para sempre — vale até para semente.
        Node? node = null;
        node = CreateNode(seeds: ["http://node1:8080", "http://node2:8080"], advertise: Self, respond: request =>
            request.RequestUri!.Host == "node1"
                ? new HttpResponseMessage(HttpStatusCode.Conflict)
                : Welcome(node!.Genesis));

        var answered = await node.Membership.AnnounceAsync();

        Assert.Equal(1, answered);
        Assert.Equal(new[] { "http://node2:8080" }, node.Registry.GetPeers());
    }

    [Fact]
    public async Task AnnounceAsync_BoasVindasComOutroGenesis_SaiDaLista()
    {
        // O handshake vale nos dois sentidos: o nó novo confere o genesis de quem respondeu.
        var node = CreateNode(seeds: ["http://node1:8080"], advertise: Self,
            respond: _ => Welcome("genesis-de-outro-universo"));

        var answered = await node.Membership.AnnounceAsync();

        Assert.Equal(0, answered);
        Assert.Empty(node.Registry.GetPeers());
    }

    [Fact]
    public async Task AnnounceAsync_SemAdvertiseUrl_NaoAnuncia()
    {
        var node = CreateNode(seeds: ["http://node1:8080"], advertise: null,
            respond: _ => throw new InvalidOperationException("não deveria chamar"));

        Assert.Equal(0, await node.Membership.AnnounceAsync());
        Assert.Empty(node.Requests);
    }

    private static HttpResponseMessage Welcome(string genesis, params string[] peers) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(
                new PeerWelcome(true, "outro-no", genesis, peers), WebJson))
        };

    /// <summary>
    /// Monta um nó em memória: registro, cadeia (dificuldade 2) e o MembershipService com
    /// um provedor de serviços que entrega um PeerClient ligado ao handler de mentira.
    /// </summary>
    private static Node CreateNode(string[] seeds, string? advertise, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var options = new NodeOptions("node4", 2, seeds, advertise);
        var registry = new PeerRegistry(options);
        var blockchain = new Blockchain(2);
        var mempool = new Mempool();
        var telemetry = new NodeTelemetry(blockchain, mempool, registry);
        var handler = new StubHandler(respond);

        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(blockchain)
            .AddSingleton(mempool)
            .AddSingleton(telemetry)
            .AddTransient(_ => new PeerClient(new HttpClient(handler), registry,
                NullLogger<PeerClient>.Instance, telemetry))
            .AddTransient<ChainSynchronizer>()
            .BuildServiceProvider();

        var membership = new MembershipService(services.GetRequiredService<IServiceScopeFactory>(),
            options, registry, blockchain, telemetry, new IdleLifetime(), NullLogger<MembershipService>.Instance);

        return new Node(membership, registry, blockchain.GenesisHash, handler.Requests);
    }

    private sealed record Node(MembershipService Membership, PeerRegistry Registry, string Genesis,
        List<(string Url, string Body, string Method)> Requests);

    /// <summary>Ciclo de vida parado: estes testes chamam Announce/Stop diretamente.</summary>
    private sealed class IdleLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

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
