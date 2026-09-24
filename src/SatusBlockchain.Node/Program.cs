var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Etapa 0 (Fundação): apenas um endpoint de identificação do nó.
// Os endpoints da blockchain serão adicionados incrementalmente (ver docs/ROADMAP.md).
var nodeId = builder.Configuration["NODE_ID"] ?? "node-local";

app.MapGet("/", () => Results.Ok(new { node = nodeId, status = "running" }));

app.Run();
