using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SatusBlockchain.Node.Tests.Api;

/// <summary>
/// Sobe o executável REAL do nó (o mesmo que roda via <c>dotnet run</c> / Docker)
/// em uma porta livre, para testar a API REST de ponta a ponta: leitura de
/// NODE_ID/DIFFICULTY do ambiente, injeção de dependência, rotas, serialização
/// e o estado em memória (cadeia e mempool).
///
/// Cada nó é um processo próprio e isolado: um teste não interfere no outro.
/// </summary>
public sealed class NodeServer : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();

    private NodeServer(Process process, HttpClient client, string baseAddress)
    {
        _process = process;
        Client = client;
        BaseAddress = baseAddress;
    }

    public HttpClient Client { get; }

    public string BaseAddress { get; }

    /// <summary>Saída do processo (stdout + stderr), útil em falhas de inicialização.</summary>
    public string Output
    {
        get
        {
            lock (_output)
                return _output.ToString();
        }
    }

    /// <summary>
    /// O executável do nó é copiado para a pasta de saída deste projeto de testes,
    /// porque o projeto de testes referencia o projeto do nó.
    /// </summary>
    private static string AppHostPath =>
        Path.Combine(AppContext.BaseDirectory, "SatusBlockchain.Node.exe");

    /// <summary>
    /// Sobe um nó e espera ele responder. <paramref name="difficulty"/> é tipado (byte)
    /// porque este é o caminho normal; valores inválidos são testados via
    /// <see cref="CreateStartInfo"/>.
    /// </summary>
    public static async Task<NodeServer> StartAsync(string nodeId = "node-test", byte difficulty = 2)
    {
        var address = $"http://127.0.0.1:{FreePort()}";
        var startInfo = CreateStartInfo(address, nodeId, difficulty.ToString());

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo do nó.");

        var client = new HttpClient
        {
            BaseAddress = new Uri(address),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var node = new NodeServer(process, client, address);
        process.OutputDataReceived += node.CaptureOutput;
        process.ErrorDataReceived += node.CaptureOutput;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await node.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));
        return node;
    }

    /// <summary>
    /// Como o nó é iniciado em produção: argumentos de linha de comando + variáveis
    /// de ambiente. O ambiente é definido explicitamente para o teste não depender
    /// das variáveis de quem o executa.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string urls, string nodeId = "node-test", string difficulty = "2")
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (File.Exists(AppHostPath))
        {
            startInfo.FileName = AppHostPath;
        }
        else
        {
            // Fora do Windows (ou sem apphost) o nó é executado via runtime.
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SatusBlockchain.Node.dll"));
        }

        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(urls);

        startInfo.Environment["NODE_ID"] = nodeId;
        startInfo.Environment["DIFFICULTY"] = difficulty;

        return startInfo;
    }

    private void CaptureOutput(object sender, DataReceivedEventArgs args)
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
            {
                throw new InvalidOperationException(
                    $"O nó encerrou com código {_process.ExitCode} durante a inicialização:\n{Output}");
            }

            try
            {
                using var response = await Client.GetAsync("/chain/validate");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Ainda subindo (Kestrel não abriu a porta).
            }

            await Task.Delay(100);
        }

        Dispose();
        throw new InvalidOperationException(
            $"O nó não respondeu em {BaseAddress} em {timeout.TotalSeconds}s:\n{Output}");
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
        Client.Dispose();

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(TimeSpan.FromSeconds(10));
        }

        _process.Dispose();
    }
}