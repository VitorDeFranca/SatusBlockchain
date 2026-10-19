using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace SatusBlockchain.Gateway.Network;

/// <summary>Formato de <c>GET /chain/validate</c> do nó.</summary>
public sealed record ChainStatusDto(bool Valid, int Length, string LatestHash);

/// <summary>Formato de <c>GET /</c> do nó (só o que o gateway usa).</summary>
public sealed record IdentityDto(string Node, int Difficulty, string Genesis);

/// <summary>Formato de <c>GET /peers</c> do nó.</summary>
public sealed record PeersDto(string Node, string? Url, List<string> Peers);

/// <summary>Formato de <c>GET /lab</c> do nó (etapa 12): falhas injetadas.</summary>
public sealed record LabDto(string? Partition, int DelayMs);

/// <summary>O que um probe trouxe de UM nó. <c>Reachable == false</c> quando a cadeia não veio.</summary>
public sealed record ProbeResult(
    string Url,
    bool Reachable,
    long LatencyMs,
    string? NodeId,
    string? AdvertiseUrl,
    IReadOnlyList<string> Peers,
    ChainStatusDto? Chain,
    int? Mempool,
    IdentityDto? Identity,
    LabDto? Lab = null);

/// <summary>
/// Pergunta a UM nó como ele está, usando a API que ele já expõe — o nó não muda por causa do
/// gateway. Cinco chamadas em paralelo; o nó conta como alcançável se <c>/chain/validate</c>
/// respondeu (é a chamada que diz o essencial: tamanho, último bloco e validade). As demais
/// (peers, mempool, identidade e o laboratório) são complementares: se falharem, o nó aparece
/// com menos detalhe, não some.
///
/// <c>GET /</c> (identidade: dificuldade e genesis) só muda quando o nó reinicia, então é
/// guardado por <see cref="IdentityTtl"/> — poupa ao nó uma validação completa da cadeia por poll.
/// </summary>
public class NodeProbe(HttpClient http, TimeProvider clock)
{
    public static readonly TimeSpan IdentityTtl = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, (IdentityDto Identity, DateTimeOffset At)> _identities =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<ProbeResult> ProbeAsync(string url, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();

        var chainTask = GetAsync<ChainStatusDto>($"{url}/chain/validate", cancellationToken);
        var peersTask = GetAsync<PeersDto>($"{url}/peers", cancellationToken);
        var pendingTask = GetAsync<List<JsonElement>>($"{url}/transactions/pending", cancellationToken);
        var identityTask = GetIdentityAsync(url, cancellationToken);
        var labTask = GetAsync<LabDto>($"{url}/lab", cancellationToken);

        await Task.WhenAll(chainTask, peersTask, pendingTask, identityTask, labTask);

        var chain = chainTask.Result;
        var peers = peersTask.Result;
        var identity = identityTask.Result;

        return new ProbeResult(
            url,
            Reachable: chain is not null,
            LatencyMs: timer.ElapsedMilliseconds,
            NodeId: peers?.Node ?? identity?.Node,
            AdvertiseUrl: NormalizeOrNull(peers?.Url),
            Peers: peers is null ? [] : [.. peers.Peers.Select(Normalize)],
            Chain: chain,
            Mempool: pendingTask.Result?.Count,
            Identity: identity,
            Lab: labTask.Result);
    }

    /// <summary>
    /// Os hashes da cadeia de um nó, em ordem (etapa 12). Só é pedido quando há nó com cadeia
    /// mais curta: é o que separa "atrasado" (o topo dele está na cadeia líder) de "fork" (não está).
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetChainHashesAsync(string url, CancellationToken cancellationToken = default)
    {
        var chain = await GetAsync<List<BlockHashDto>>($"{url}/chain", cancellationToken);
        return chain?.Select(block => block.Hash).ToList();
    }

    private sealed record BlockHashDto(string Hash);

    private async Task<IdentityDto?> GetIdentityAsync(string url, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (_identities.TryGetValue(url, out var cached) && now - cached.At < IdentityTtl)
            return cached.Identity;

        var identity = await GetAsync<IdentityDto>($"{url}/", cancellationToken);
        if (identity is not null)
            _identities[url] = (identity, now);

        // Se esta tentativa falhou, o valor antigo (ainda que vencido) é melhor que nada.
        return identity ?? (_identities.TryGetValue(url, out var stale) ? stale.Identity : null);
    }

    /// <summary>
    /// GET que nunca lança por causa do nó: fora do ar, lento, status de erro ou corpo que não
    /// é o esperado viram <c>null</c>. Só o cancelamento de quem chamou (o gateway desligando, o
    /// navegador que desistiu) é propagado.
    /// </summary>
    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<T>(WebJson, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or NotSupportedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static string Normalize(string url) =>
        GatewayOptions.TryNormalizeUrl(url, out var normalized) ? normalized : url.Trim();

    private static string? NormalizeOrNull(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : Normalize(url);
}
