using SatusBlockchain.Node.Networking;

namespace SatusBlockchain.Node.Tests.Unit;

/// <summary>
/// Testes de unidade da lista viva de peers (etapa 10): quem entra, quem sai e quando um
/// peer que parou de responder é removido — sem rede e sem processo.
/// </summary>
public class PeerRegistryTests
{
    [Fact]
    public void NasceComAsSementesDePeers()
    {
        var registry = Registry(peers: ["http://node2:8080", "http://node3:8080"]);

        Assert.Equal(new[] { "http://node2:8080", "http://node3:8080" }, registry.GetPeers());
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void ProprioEnderecoNasSementes_EIgnorado()
    {
        // Engano comum de configuração: copiar a mesma lista de PEERS para todos os nós.
        var registry = Registry(peers: ["http://node1:8080", "http://node2:8080"], self: "http://node1:8080");

        Assert.Equal(new[] { "http://node2:8080" }, registry.GetPeers());
        Assert.Equal(PeerAddition.Self, registry.Add("http://node1:8080"));
    }

    [Fact]
    public void Add_NovoPeer_Entra_E_Repetido_NaoDuplica()
    {
        var registry = Registry();

        Assert.Equal(PeerAddition.Added, registry.Add("http://node4:8080"));
        // Reanúncio (nó que voltou): não duplica, comparação sem diferenciar maiúsculas.
        Assert.Equal(PeerAddition.AlreadyKnown, registry.Add("HTTP://NODE4:8080"));

        Assert.Single(registry.GetPeers());
    }

    [Fact]
    public void Add_ListaCheia_Recusa()
    {
        // Teto contra quem registra endereços em massa para transformar o nó em canhão de POSTs.
        var registry = Registry();
        for (var i = 0; i < PeerRegistry.MaxPeers; i++)
            Assert.Equal(PeerAddition.Added, registry.Add($"http://node{i}:8080"));

        Assert.Equal(PeerAddition.Full, registry.Add("http://um-a-mais:8080"));
        Assert.Equal(PeerRegistry.MaxPeers, registry.Count);
    }

    [Fact]
    public void Remove_TiraDaLista_E_InformaSeEstava()
    {
        var registry = Registry(peers: ["http://node2:8080"]);

        Assert.True(registry.Remove("http://node2:8080"));   // despedida: até semente sai
        Assert.False(registry.Remove("http://node2:8080"));  // idempotente
        Assert.Empty(registry.GetPeers());
    }

    [Fact]
    public void RecordFailure_SaiNaTerceiraFalhaSeguida()
    {
        var registry = Registry();
        registry.Add("http://node4:8080");

        Assert.False(registry.RecordFailure("http://node4:8080"));
        Assert.False(registry.RecordFailure("http://node4:8080"));
        Assert.True(registry.RecordFailure("http://node4:8080"));

        Assert.False(registry.Contains("http://node4:8080"));
    }

    [Fact]
    public void RecordSuccess_ZeraAContagem()
    {
        // "Seguidas" é a palavra-chave: uma resposta no meio prova que o peer está vivo.
        var registry = Registry();
        registry.Add("http://node4:8080");

        registry.RecordFailure("http://node4:8080");
        registry.RecordFailure("http://node4:8080");
        registry.RecordSuccess("http://node4:8080");
        registry.RecordFailure("http://node4:8080");
        registry.RecordFailure("http://node4:8080");

        Assert.True(registry.Contains("http://node4:8080"));
    }

    [Fact]
    public void Reanuncio_ZeraAContagem()
    {
        // O nó que voltou e se anunciou de novo recomeça do zero.
        var registry = Registry();
        registry.Add("http://node4:8080");
        registry.RecordFailure("http://node4:8080");
        registry.RecordFailure("http://node4:8080");

        registry.Add("http://node4:8080");

        Assert.False(registry.RecordFailure("http://node4:8080"));
        Assert.True(registry.Contains("http://node4:8080"));
    }

    [Fact]
    public void RecordFailure_SementeNuncaSai()
    {
        // As sementes são o caminho de volta para a rede depois de uma queda ou partição.
        var registry = Registry(peers: ["http://node2:8080"]);

        for (var i = 0; i < PeerRegistry.MaxConsecutiveFailures * 3; i++)
            Assert.False(registry.RecordFailure("http://node2:8080"));

        Assert.True(registry.Contains("http://node2:8080"));
    }

    [Fact]
    public void RecordFailure_PeerDesconhecido_NaoFazNada()
    {
        // O handshake chama de volta quem ainda não está na lista: falhar não muda nada.
        var registry = Registry();

        Assert.False(registry.RecordFailure("http://estranho:8080"));
        Assert.Empty(registry.GetPeers());
    }

    private static PeerRegistry Registry(string[]? peers = null, string? self = null) =>
        new(new NodeOptions("node-test", 2, peers ?? [], self));
}
