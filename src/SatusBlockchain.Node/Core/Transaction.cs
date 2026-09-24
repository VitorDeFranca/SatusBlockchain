namespace SatusBlockchain.Node.Core;

/// <summary>
/// Registro mínimo de uma transferência. Deliberadamente sem assinatura digital
/// e sem validação de saldo (ver docs/REQUIREMENTS.md, seção "Fora de escopo").
/// É um record: duas transações com os mesmos dados são consideradas iguais.
/// </summary>
public record Transaction(string From, string To, decimal Amount);