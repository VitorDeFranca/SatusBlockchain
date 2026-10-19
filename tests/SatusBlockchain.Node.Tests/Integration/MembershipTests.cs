using System.Net;
using System.Text;
using System.Text.Json;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Integration;

/// <summary>
/// Testes de integração da MEMBERSHIP DINÂMICA (etapa 10): nós reais entrando e saindo da
/// rede com o sistema rodando. A semente é um nó SEM ADVERTISE_URL — o papel do node1 no
/// docker-compose —, e o nó novo é o que o add-node cria: ADVERTISE_URL + uma semente em PEERS.
///
/// A entrada roda em segundo plano depois de o nó abrir a porta (o handshake precisa
/// chamá-lo de volta), então estes testes ESPERAM o estado aparecer (polling), do mesmo
/// jeito que alguém olhando GET /peers esperaria.
/// </summary>
public class MembershipTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task NoNovo_SeAnunciaASemente_E_PassaAReceberOsBlocosDela()
    {
        using var semente = await NodeServer.StartAsync(nodeId: "node-semente", difficulty: 2);
        using var novo = await NodeServer.StartAsync(nodeId: "node-novo", difficulty: 2,
            peers: [semente.BaseAddress], advertise: true);

        // A semente não tinha o nó novo em PEERS: ele entrou na lista dela em execução.
        await WaitUntilAsync(async () => (await PeersAsync(semente)).Contains(novo.BaseAddress),
            "a semente nunca registrou o nó novo");

        // E daí em diante o push da semente chega ao nó novo.
        var mined = await semente.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, mined.StatusCode);
        Assert.Equal(await HashesAsync(semente), await HashesAsync(novo));
    }

    [Fact]
    public async Task NoNovo_DescobreORestoDaRede_ApartirDeUmaSemente()
    {
        using var semente = await NodeServer.StartAsync(nodeId: "node-semente", difficulty: 2);
        using var primeiro = await NodeServer.StartAsync(nodeId: "node-primeiro", difficulty: 2,
            peers: [semente.BaseAddress], advertise: true);
        await WaitUntilAsync(async () => (await PeersAsync(semente)).Contains(primeiro.BaseAddress),
            "a semente nunca registrou o primeiro nó");

        // O segundo só conhece a semente — e a resposta do anúncio revela o primeiro.
        using var segundo = await NodeServer.StartAsync(nodeId: "node-segundo", difficulty: 2,
            peers: [semente.BaseAddress], advertise: true);

        await WaitUntilAsync(async () => (await PeersAsync(primeiro)).Contains(segundo.BaseAddress),
            "o primeiro nó nunca registrou o segundo (descoberta pelos peers da semente)");

        // O lado do segundo também é assíncrono: ele só registra o primeiro DEPOIS de ler a
        // resposta do anúncio — que chega um instante após o primeiro já tê-lo registrado.
        await WaitUntilAsync(async () =>
        {
            var peersDoSegundo = await PeersAsync(segundo);
            return peersDoSegundo.Contains(semente.BaseAddress) && peersDoSegundo.Contains(primeiro.BaseAddress);
        }, "o segundo nó não terminou com a semente e o primeiro na lista");
    }

    [Fact]
    public async Task NoNovo_SincronizaACadeiaAoEntrar()
    {
        // O push só leva blocos NOVOS; a história, quem entra precisa puxar (etapa 8).
        using var semente = await NodeServer.StartAsync(nodeId: "node-semente", difficulty: 2);
        await PostTransactionAsync(semente, new Transaction("alice", "bob", 10m));
        await MineAsync(semente);
        await MineAsync(semente);

        using var novo = await NodeServer.StartAsync(nodeId: "node-novo", difficulty: 2,
            peers: [semente.BaseAddress], advertise: true);

        await WaitUntilAsync(async () => (await HashesAsync(novo)).Length == 3,
            "o nó novo não sincronizou a cadeia ao entrar");
        Assert.Equal(await HashesAsync(semente), await HashesAsync(novo));
    }

    [Fact]
    public async Task PostPeers_Aceito_DevolveBoasVindas_E_DeletePeers_Remove()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var outro = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);

        var response = await AnnounceAsync(node, outro.BaseAddress);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var welcome = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var raiz = welcome.RootElement;
            Assert.True(raiz.GetProperty("added").GetBoolean());
            Assert.Equal("node-a", raiz.GetProperty("node").GetString());
            Assert.Equal(await GenesisAsync(node), raiz.GetProperty("genesis").GetString());
        }
        Assert.Contains(outro.BaseAddress, await PeersAsync(node));

        // Reanúncio: idempotente, sem duplicar.
        using (var again = JsonDocument.Parse(
            await (await AnnounceAsync(node, outro.BaseAddress)).Content.ReadAsStringAsync()))
            Assert.False(again.RootElement.GetProperty("added").GetBoolean());
        Assert.Single(await PeersAsync(node));

        // Despedida.
        var leave = await node.Client.DeleteAsync($"/peers?url={Uri.EscapeDataString(outro.BaseAddress)}");
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        using (var body = JsonDocument.Parse(await leave.Content.ReadAsStringAsync()))
            Assert.True(body.RootElement.GetProperty("removed").GetBoolean());
        Assert.Empty(await PeersAsync(node));
    }

    [Fact]
    public async Task PostPeers_GenesisDiferente_Retorna409_E_NaoRegistra()
    {
        // Dificuldade diferente = genesis diferente = outro universo: nenhum bloco de lá
        // valeria aqui, então nem entra na lista.
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var outroUniverso = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 0);
        Assert.NotEqual(await GenesisAsync(node), await GenesisAsync(outroUniverso));

        var response = await AnnounceAsync(node, outroUniverso.BaseAddress);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await PeersAsync(node));
    }

    [Fact]
    public async Task PostPeers_EnderecoQueNaoResponde_Retorna502()
    {
        // O handshake chama de volta o endereço anunciado: quem não responde ali não entra.
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);

        var response = await AnnounceAsync(node, "http://127.0.0.1:1"); // porta morta

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(await PeersAsync(node));
    }

    [Theory]
    [InlineData("node4:8080")]       // sem esquema
    [InlineData("")]                 // vazio
    public async Task PostPeers_UrlInvalida_Retorna400(string url)
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);

        var response = await AnnounceAsync(node, url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostPeers_MesmoNodeId_Retorna409()
    {
        // Dois nós com a mesma identidade deixariam logs, métricas e traces ambíguos (é
        // também o que acontece com o próprio nó anunciado por um segundo endereço).
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var gemeo = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);

        var response = await AnnounceAsync(node, gemeo.BaseAddress);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await PeersAsync(node));
    }

    [Fact]
    public async Task NoQueCaiuSemSeDespedir_SaiDaListaAposTresFalhas()
    {
        using var semente = await NodeServer.StartAsync(nodeId: "node-semente", difficulty: 2);
        using var novo = await NodeServer.StartAsync(nodeId: "node-novo", difficulty: 2,
            peers: [semente.BaseAddress], advertise: true);
        await WaitUntilAsync(async () => (await PeersAsync(semente)).Contains(novo.BaseAddress),
            "a semente nunca registrou o nó novo");

        // Queda: o processo morre (Kill), sem a despedida do desligamento gracioso.
        novo.Dispose();

        // Cada transação nova é propagada ao nó morto: conexão recusada, uma falha por vez.
        for (var i = 1; i <= 2; i++)
            await PostTransactionAsync(semente, new Transaction("alice", "bob", i));
        Assert.Contains(novo.BaseAddress, await PeersAsync(semente)); // duas não bastam

        await PostTransactionAsync(semente, new Transaction("alice", "bob", 3m));
        Assert.DoesNotContain(novo.BaseAddress, await PeersAsync(semente));
    }

    private static Task<HttpResponseMessage> AnnounceAsync(NodeServer node, string url) =>
        node.Client.PostAsync("/peers", new StringContent(
            JsonSerializer.Serialize(new { url }, WebJson), Encoding.UTF8, "application/json"));

    private static async Task<List<string>> PeersAsync(NodeServer node)
    {
        using var json = await GetJsonAsync(node, "/peers");
        return [.. json.RootElement.GetProperty("peers").EnumerateArray().Select(peer => peer.GetString()!)];
    }

    private static async Task<string> GenesisAsync(NodeServer node)
    {
        using var json = await GetJsonAsync(node, "/");
        return json.RootElement.GetProperty("genesis").GetString()!;
    }

    private static async Task<string[]> HashesAsync(NodeServer node)
    {
        using var chain = await GetJsonAsync(node, "/chain");
        return [.. chain.RootElement.EnumerateArray().Select(bloco => bloco.GetProperty("hash").GetString()!)];
    }

    private static async Task MineAsync(NodeServer node)
    {
        var response = await node.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task PostTransactionAsync(NodeServer node, Transaction transaction)
    {
        var response = await node.Client.PostAsync("/transactions", new StringContent(
            JsonSerializer.Serialize(transaction, WebJson), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<JsonDocument> GetJsonAsync(NodeServer node, string path)
    {
        var response = await node.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Espera uma condição ficar verdadeira (a entrada na rede é assíncrona). Falha com a
    /// mensagem informada se o prazo acabar.
    /// </summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string failure,
        int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(200);
        }

        Assert.Fail(failure);
    }
}
