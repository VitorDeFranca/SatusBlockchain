using SatusBlockchain.Node;
using SatusBlockchain.Node.Api;
using SatusBlockchain.Node.Core;

var builder = WebApplication.CreateBuilder(args);

// Configuração do nó (NODE_ID, DIFFICULTY), validada na inicialização.
var nodeOptions = NodeOptions.FromConfiguration(builder.Configuration);

// Estado do nó: instâncias únicas em memória (o estado replicado de cada nó).
builder.Services.AddSingleton(nodeOptions);
builder.Services.AddSingleton<Blockchain>();
builder.Services.AddSingleton<Mempool>();

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

app.Run();
