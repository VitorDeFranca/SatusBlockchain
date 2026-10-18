namespace SatusBlockchain.Node.Networking;

/// <summary>
/// Corpo de <c>POST /peers</c> (etapa 10): "eu existo, me chame neste endereço". É o que
/// um nó novo manda a cada peer ao entrar na rede.
/// </summary>
/// <param name="Url">O <c>ADVERTISE_URL</c> de quem se anuncia.</param>
public sealed record PeerAnnouncement(string? Url);

/// <summary>
/// Resposta de <c>POST /peers</c> aceito: quem respondeu, o genesis dele (o nó novo confere
/// do lado de lá também — o handshake vale nos dois sentidos) e os peers que ele conhece,
/// que é como o nó novo descobre o resto da rede a partir de uma única semente.
/// </summary>
public sealed record PeerWelcome(bool Added, string Node, string Genesis, IReadOnlyList<string> Peers);

/// <summary>
/// Identidade pública de um nó, lida de <c>GET /</c>. É o que o handshake confere antes de
/// registrar um nó: se ele responde no endereço que anunciou e se nasceu do mesmo genesis.
/// </summary>
public sealed record NodeIdentity(string Node, byte Difficulty, string Genesis);

/// <summary>
/// Resposta de UM peer a uma chamada de membership. Mesmo desenho de
/// <see cref="PeerResult"/> e <see cref="PeerChain"/>: o que falha vira resultado, nunca
/// exceção.
/// </summary>
/// <param name="Peer">Endereço chamado.</param>
/// <param name="Value">O corpo lido, ou <c>null</c> se não houve resposta útil.</param>
/// <param name="Detail">Status HTTP recebido (ex.: <c>409</c>) ou <c>inacessível</c>.</param>
public sealed record PeerReply<T>(string Peer, T? Value, string Detail) where T : class;
