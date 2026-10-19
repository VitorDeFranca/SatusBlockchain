using System.Text.Json;
using System.Text.Json.Serialization;
using SatusBlockchain.Gateway;
using SatusBlockchain.Gateway.Api;
using SatusBlockchain.Gateway.Network;
using SatusBlockchain.Gateway.Nodes;
using SatusBlockchain.Gateway.Runtime;

var builder = WebApplication.CreateBuilder(args);

// O polling da UI (a cada 2 s) e o probe dos nós (4 chamadas por nó) logariam cada requisição:
// só avisos dessas categorias; o log do console fica para o que ele decide (nó criado, removido).
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Configuração (GATEWAY_NODES, RUNTIME, DOCKER_SOCKET, *_URL), validada na inicialização.
var options = GatewayOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);

// O JSON do gateway segue o dos nós: camelCase, e enums como texto ("behind", "unreachable") —
// a UI compara palavras legíveis, não números.
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// Probe: 1,5 s por chamada. Um nó travado não pode segurar o snapshot (e, com ele, a tela):
// vira "inacessível", que é exatamente o que se quer mostrar.
builder.Services.AddHttpClient<NodeProbe>(client => client.Timeout = TimeSpan.FromMilliseconds(1500));

// Operações repassadas: minerar (PoW) e sincronizar (anúncio + pull) levam segundos.
builder.Services.AddHttpClient<NodeProxy>(client => client.Timeout = TimeSpan.FromSeconds(30));

// Plataforma onde os nós rodam: Docker (socket montado) ou nenhuma.
var dockerAvailable = File.Exists(options.DockerSocket);
if (options.Runtime == RuntimeMode.Docker && !dockerAvailable)
{
    throw new InvalidOperationException(
        $"RUNTIME=docker, mas o socket '{options.DockerSocket}' não existe. " +
        "Monte /var/run/docker.sock no container ou use RUNTIME=none.");
}

if (options.Runtime != RuntimeMode.None && dockerAvailable)
    builder.Services.AddSingleton<INodeRuntime>(_ => new DockerNodeRuntime(DockerNodeRuntime.CreateClient(options.DockerSocket)));
else
    builder.Services.AddSingleton<INodeRuntime, NullNodeRuntime>();

builder.Services.AddSingleton<NetworkService>();
builder.Services.AddSingleton<NodeLifecycle>();

// Laboratório (etapa 12): partição, reset e sync em todos os nós de uma vez. Transient porque
// usa o NodeProxy (cliente HTTP tipado, transient).
builder.Services.AddTransient<LabService>();

var app = builder.Build();

// A SPA compilada (ui/ → wwwroot): mesma origem da API, então nada de CORS.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapNetworkEndpoints();
app.MapNodeEndpoints();
app.MapLabEndpoints();

app.Run();
