namespace SatusBlockchain.Node.Core;

/// <summary>
/// O que o nó acha de um bloco, sem anexá-lo (<see cref="Blockchain.Inspect"/>, etapa 12): a
/// mesma régua do <c>AddBlock</c>, explicada item a item. É o que permite ao console mostrar
/// POR QUE um bloco adulterado é recusado — o hash recalculado não bate com o declarado.
/// </summary>
/// <param name="RecalculatedHash">SHA-256 do conteúdo do bloco, calculado agora pelo nó.</param>
/// <param name="HashMatches">O hash declarado é o do conteúdo (senão: adulterado).</param>
/// <param name="ProofOfWork">O hash declarado tem os zeros que a dificuldade do nó exige.</param>
/// <param name="InChain">O bloco já está na cadeia deste nó, na mesma posição.</param>
/// <param name="ExtendsTip">Índice e elo (<c>PreviousHash</c>) encaixam no topo da cadeia.</param>
/// <param name="WouldAccept">O <c>POST /blocks/receive</c> aceitaria o bloco agora.</param>
/// <param name="Verdict">A explicação em uma frase.</param>
public sealed record BlockInspection(
    string RecalculatedHash,
    bool HashMatches,
    bool ProofOfWork,
    bool InChain,
    bool ExtendsTip,
    bool WouldAccept,
    string Verdict);
