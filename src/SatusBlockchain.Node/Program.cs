using SatusBlockchain.Node;
using SatusBlockchain.Node.Api;
using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;

var builder = WebApplication.CreateBuilder(args);

// Configuração do nó (NODE_ID, DIFFICULTY, PEERS), validada na inicialização.
var nodeOptions = NodeOptions.FromConfiguration(builder.Configuration);

// Estado do nó: instâncias únicas em memória (o estado replicado de cada nó).
builder.Services.AddSingleton(nodeOptions);
// A cadeia precisa da dificuldade CONFIGURADA: sem esta fábrica, o container
// usaria o valor padrão do parâmetro do construtor (ProofOfWork.DefaultDifficulty)
// e o DIFFICULTY do ambiente seria ignorado — o nó reportaria "2" e mineraria na
// dificuldade 4. Regressão coberta por ApiTests.DifficultyDoAmbiente_ChegaNaCadeia.
builder.Services.AddSingleton(_ => new Blockchain(nodeOptions.Difficulty));
builder.Services.AddSingleton<Mempool>();

// HTTP de SAÍDA para os peers (push da etapa 6). O timeout é curto de propósito:
// um peer mudo não pode segurar a resposta do POST /blocks/mine, que precisa do
// resultado do push para propagar de forma previsível (e a falha é tolerada).
builder.Services.AddHttpClient<PeerClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(2);
});

var app = builder.Build();

// Identidade e situação do nó — útil para conferir a demo.
app.MapGet("/", (NodeOptions node, Blockchain blockchain) => Results.Ok(new
{
    node = node.NodeId,
    difficulty = node.Difficulty,
    blocks = blockchain.Length,
    valid = blockchain.IsValid()
}));

app.MapChainEndpoints();
app.MapTransactionEndpoints();
app.MapBlockEndpoints();
app.MapPeerEndpoints();

app.Run();

