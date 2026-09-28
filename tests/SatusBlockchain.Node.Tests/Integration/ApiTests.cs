using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Integration;

/// <summary>
/// Testes de integração da API REST (Etapa 4). Cada teste sobe um nó real, isolado,
/// e conversa com ele por HTTP — exatamente como um cliente faria.
///
/// Cobre o que a etapa entregou: configuração por ambiente (NODE_ID / DIFFICULTY),
/// os endpoints, a validação de entrada e a passagem mempool -> bloco.
/// </summary>
public class ApiTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Get_Raiz_RetornaIdentidadeESituacaoDoNo()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-api", difficulty: 2);

        using var json = await GetJsonAsync(node, "/");

        Assert.Equal("node-api", json.RootElement.GetProperty("node").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("difficulty").GetByte());
        Assert.Equal(1, json.RootElement.GetProperty("blocks").GetInt32());
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task GetChain_RetornaApenasOGenesis()
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);

        using var json = await GetJsonAsync(node, "/chain");

        var genesis = Assert.Single(Elements(json.RootElement));
        Assert.Equal(0, genesis.GetProperty("index").GetInt64());
        Assert.Equal(Blockchain.GenesisPreviousHash, genesis.GetProperty("previousHash").GetString());
        Assert.Empty(genesis.GetProperty("transactions").EnumerateArray());
        // O genesis também passa pelo Proof of Work, na dificuldade do nó.
        Assert.StartsWith("00", genesis.GetProperty("hash").GetString());
    }

    [Fact]
    public async Task GetChainValidate_RetornaCadeiaValida()
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);

        using var json = await GetJsonAsync(node, "/chain/validate");

        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("length").GetInt32());
        Assert.Equal(64, json.RootElement.GetProperty("latestHash").GetString()!.Length);
    }

    [Fact]
    public async Task DoisNos_PartemDoMesmoGenesis()
    {
        // Pré-requisito do consenso (etapas seguintes): nós diferentes, com o mesmo
        // DIFFICULTY, precisam produzir exatamente o mesmo bloco genesis.
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);

        using var chainA = await GetJsonAsync(nodeA, "/chain");
        using var chainB = await GetJsonAsync(nodeB, "/chain");

        Assert.Equal(
            chainA.RootElement[0].GetProperty("hash").GetString(),
            chainB.RootElement[0].GetProperty("hash").GetString());
    }

    [Fact]
    public async Task PostTransaction_Retorna201_E_FicaPendente()
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);
        var transacao = new Transaction("alice", "bob", 10m);

        var response = await PostTransactionAsync(node, transacao);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/transactions/pending", response.Headers.Location?.ToString());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("alice", body.RootElement.GetProperty("from").GetString());
        Assert.Equal("bob", body.RootElement.GetProperty("to").GetString());
        Assert.Equal(10m, body.RootElement.GetProperty("amount").GetDecimal());

        using var pending = await GetJsonAsync(node, "/transactions/pending");
        var pendente = Assert.Single(Elements(pending.RootElement));
        Assert.Equal("bob", pendente.GetProperty("to").GetString());
    }

    [Theory]
    [InlineData("", "bob", 10)]      // sem origem
    [InlineData("alice", "", 10)]    // sem destino
    [InlineData("alice", "bob", 0)]  // valor zerado
    [InlineData("alice", "bob", -5)] // valor negativo
    public async Task PostTransactionInvalida_Retorna400_E_NaoEntraNaMempool(string from, string to, decimal amount)
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);

        var response = await PostTransactionAsync(node, new Transaction(from, to, amount));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var pending = await GetJsonAsync(node, "/transactions/pending");
        Assert.Empty(pending.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task PostMine_ComTransacoesPendentes_MinaBloco_E_EsvaziaAMempool()
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);
        using var chain = await GetJsonAsync(node, "/chain");
        var genesisHash = chain.RootElement[0].GetProperty("hash").GetString();

        await PostTransactionAsync(node, new Transaction("alice", "bob", 10m));
        await PostTransactionAsync(node, new Transaction("bob", "carol", 4.5m));

        var response = await node.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var block = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, block.RootElement.GetProperty("index").GetInt64());
        Assert.Equal(genesisHash, block.RootElement.GetProperty("previousHash").GetString());
        Assert.Equal(2, block.RootElement.GetProperty("transactions").GetArrayLength());
        Assert.StartsWith("00", block.RootElement.GetProperty("hash").GetString());

        // As transações saíram da mempool porque agora estão dentro do bloco.
        using var pending = await GetJsonAsync(node, "/transactions/pending");
        Assert.Empty(pending.RootElement.EnumerateArray());

        using var validation = await GetJsonAsync(node, "/chain/validate");
        Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(2, validation.RootElement.GetProperty("length").GetInt32());
    }

    [Fact]
    public async Task PostMine_SemTransacoesPendentes_MinaBlocoVazio()
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);

        var response = await node.Client.PostAsync("/blocks/mine", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var block = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, block.RootElement.GetProperty("index").GetInt64());
        Assert.Empty(block.RootElement.GetProperty("transactions").EnumerateArray());
    }

    [Fact]
    public async Task DifficultyInvalida_ImpedeOInicioDoNo()
    {
        // DIFFICULTY acima do máximo (64 caracteres no hash) deve derrubar o nó na
        // inicialização, com mensagem clara — em vez de minerar para sempre.
        var startInfo = NodeServer.CreateStartInfo("http://127.0.0.1:1", nodeId: "node-invalido", difficulty: "100");
        using var process = Process.Start(startInfo)!;

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("DIFFICULTY inválida", stderr);
    }

    [Fact]
    public async Task DifficultyDoAmbiente_ChegaNaCadeia()
    {
        // Com DIFFICULTY=0 o primeiro nonce testado (0) já satisfaz o Proof of Work.
        // Se o valor do ambiente não chegasse à Blockchain, ela cairia na dificuldade
        // padrão (4) e o nonce seria > 0 — foi exatamente esse o bug de fiação do DI.
        using var node = await NodeServer.StartAsync(difficulty: 0);

        using var chain = await GetJsonAsync(node, "/chain");
        var genesis = Assert.Single(Elements(chain.RootElement));
        Assert.Equal(0, genesis.GetProperty("nonce").GetInt64());

        var response = await node.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var block = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, block.RootElement.GetProperty("nonce").GetInt64());
    }

    private static async Task<JsonDocument> GetJsonAsync(NodeServer node, string path)
    {
        var response = await node.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PostTransactionAsync(NodeServer node, Transaction transaction) =>
        node.Client.PostAsync("/transactions", new StringContent(
            JsonSerializer.Serialize(transaction, WebJson), Encoding.UTF8, "application/json"));

    private static List<JsonElement> Elements(JsonElement array) => [.. array.EnumerateArray()];
}