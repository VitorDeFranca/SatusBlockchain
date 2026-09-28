namespace SatusBlockchain.Node.Networking;

/// <summary>
/// Resultado do envio de um bloco a UM peer. É o que sobra do push para quem chamou
/// (os detalhes já ficam no log do <see cref="PeerClient"/>): assim a mineração local
/// consegue registrar o que aconteceu sem nunca depender de um peer estar no ar.
/// </summary>
/// <param name="Peer">Endereço do peer, como configurado em PEERS.</param>
/// <param name="Accepted"><c>true</c> se o peer aceitou o bloco (resposta 2xx).</param>
/// <param name="Detail">Status HTTP recebido (ex.: <c>409</c>) ou <c>inacessível</c>.</param>
public sealed record PeerResult(string Peer, bool Accepted, string Detail);
