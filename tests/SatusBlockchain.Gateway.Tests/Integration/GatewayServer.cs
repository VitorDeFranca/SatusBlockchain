using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SatusBlockchain.Gateway.Tests.Integration;

/// <summary>
/// Sobe o executável REAL do gateway (o mesmo do container do console) em uma porta livre,
/// apontado para nós já em execução — mesmo desenho do <c>NodeServer</c> do nó. Sem Docker:
/// <c>RUNTIME=none</c>, e os nós vêm de <c>GATEWAY_NODES</c> + listas de peers.
/// </summary>
public sealed class GatewayServer : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();
    private bool _disposed;

    private GatewayServer(Process process, HttpClient client)
    {
        _process = process;
        Client = client;
    }

    public HttpClient Client { get; }

    public string Output
    {
        get
        {
            lock (_output)
                return _output.ToString();
        }
    }

    public static async Task<GatewayServer> StartAsync(params string[] nodes)
    {
        var address = $"http://127.0.0.1:{FreePort()}";
        var apphost = Path.Combine(AppContext.BaseDirectory, "SatusBlockchain.Gateway.exe");

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (File.Exists(apphost))
        {
            startInfo.FileName = apphost;
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SatusBlockchain.Gateway.dll"));
        }

        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(address);

        // Ambiente explícito: nada herdado de quem roda os testes (mesma regra do NodeServer).
        startInfo.Environment["GATEWAY_NODES"] = string.Join(",", nodes);
        startInfo.Environment["RUNTIME"] = "none";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo do gateway.");

        var server = new GatewayServer(process, new HttpClient
        {
            BaseAddress = new Uri(address),
            Timeout = TimeSpan.FromSeconds(30)
        });
        process.OutputDataReceived += server.Capture;
        process.ErrorDataReceived += server.Capture;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));
        return server;
    }

    private void Capture(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is null)
            return;

        lock (_output)
            _output.AppendLine(args.Data);
    }

    private async Task WaitUntilReadyAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"O gateway encerrou na inicialização:\n{Output}");

            try
            {
                using var response = await Client.GetAsync("/api/capabilities");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Ainda subindo.
            }

            await Task.Delay(100);
        }

        Dispose();
        throw new InvalidOperationException($"O gateway não respondeu em {timeout.TotalSeconds}s:\n{Output}");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Client.Dispose();

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(TimeSpan.FromSeconds(10));
        }

        _process.Dispose();
    }
}
