using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Networking;

/// <summary>
/// Cadeia lida de UM peer no <c>POST /sync</c> (etapa 8). O desenho espelha
/// <see cref="PeerResult"/>: o que falha vira resultado, nunca exceção — um peer fora do
/// ar não pode derrubar a sincronização.
/// </summary>
/// <param name="Peer">Endereço do peer, como configurado em PEERS.</param>
/// <param name="Chain">A cadeia recebida, ou <c>null</c> se não deu para ler.</param>
/// <param name="Detail">
/// Quantos blocos vieram, o status HTTP recebido, ou <c>inacessível</c>.
/// </param>
public sealed record PeerChain(string Peer, IReadOnlyList<Block>? Chain, string Detail);