namespace SatusBlockchain.Gateway.Nodes;

/// <summary>
/// Uma operação de nó que não pôde ser feita, com o status HTTP que a API deve devolver e uma
/// mensagem que a UI mostra como está (por isso em português e voltada a quem usa o console).
/// </summary>
public sealed class NodeOperationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
