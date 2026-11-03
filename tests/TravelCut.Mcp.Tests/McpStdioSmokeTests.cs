using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TravelCut.Mcp.Facade;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>
/// Smoke test de transporte real: inicia o executável do servidor como processo e fala JSON-RPC por linhas no stdio.
/// Usa banco temporário (TRAVELCUT_MCP_DATABASE) e o simulador do editor (TRAVELCUT_EDITOR_ADAPTER=mock).
/// Não toca em %LOCALAPPDATA% nem chama o DaVinci.
/// </summary>
public sealed class McpStdioSmokeTests : IDisposable
{
    private static readonly string[] ReadOnlyTools =
    [
        "list_projects", "get_inventory", "get_transcript", "get_suggestions",
        "get_edit_plan", "job_status", "get_editor_capabilities", "get_motion_capabilities",
    ];

    private static readonly string[] MutationTools = ["submit_review", "queue_analysis", "apply_plan"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc_stdio_" + Guid.NewGuid().ToString("N"));

    public McpStdioSmokeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço: o processo pode ainda estar liberando o SQLite.
        }
    }

    [Fact]
    public async Task Stdio_SemAllowMutations_ListaSomenteFerramentasDeLeitura()
    {
        using var session = StdioSession.Start(_root, new[] { "--migrate-database" }, new Dictionary<string, string>());

        await session.InitializeAsync();
        var tools = await session.ListToolsAsync();

        Assert.Equal(ReadOnlyTools.OrderBy(n => n), tools.OrderBy(n => n));
        Assert.DoesNotContain(MutationTools, tools.Contains);

        var list = await session.CallToolAsync("list_projects", new JsonObject());
        Assert.False(list["result"]!["isError"]?.GetValue<bool>() ?? false);

        await session.CloseInputAndAssertExitsAsync();
    }

    [Fact]
    public async Task Stdio_ComAllowMutationsEProjeto_ListaMutacoesEExecutaErroEstruturado()
    {
        var projectId = Guid.NewGuid().ToString();
        using var session = StdioSession.Start(
            _root,
            new[] { "--allow-mutations", "--project", projectId, "--migrate-database" },
            new Dictionary<string, string>());

        await session.InitializeAsync();
        var tools = await session.ListToolsAsync();
        Assert.Equal((ReadOnlyTools.Concat(MutationTools)).OrderBy(n => n), tools.OrderBy(n => n));

        // Projeto autorizado, mas inexistente no banco: o caminho até o IpcDispatcher devolve erro estruturado.
        var call = await session.CallToolAsync("queue_analysis", new JsonObject
        {
            ["projectId"] = projectId,
            ["idempotencyKey"] = "smoke-stdio-1",
        });
        var result = call["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var error = JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
        Assert.False(string.IsNullOrWhiteSpace(error["code"]!.GetValue<string>()));
        Assert.DoesNotContain(_root, error.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        await session.CloseInputAndAssertExitsAsync();
    }

    [Fact]
    public async Task Stdio_BancoSemSchemaSemFlag_SaiComCodigoDiferenteDeZeroEOrientaAbrirODesktop()
    {
        // Banco vazio (arquivo sem tabelas): o servidor não migra sozinho e falha antes de atender clientes.
        File.WriteAllBytes(Path.Combine(_root, "smoke.db"), []);

        using var session = StdioSession.Start(_root, Array.Empty<string>(), new Dictionary<string, string>());
        var exit = await session.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.NotEqual(0, exit);
        Assert.Contains("Abra o TravelCut Desktop uma vez para atualizar o banco", session.StdErr);
        Assert.Empty(session.StdOutLines);
        Assert.DoesNotContain(_root, session.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stdio_BancoVazioComMigrateDatabase_MigraEAtendeLeitura()
    {
        File.WriteAllBytes(Path.Combine(_root, "smoke.db"), []);

        using var session = StdioSession.Start(_root, new[] { "--migrate-database" }, new Dictionary<string, string>());
        await session.InitializeAsync();
        var tools = await session.ListToolsAsync();

        Assert.Contains("list_projects", tools);
        await session.CloseInputAndAssertExitsAsync();
    }

    [Fact]
    public async Task Stdio_ArgumentoDesconhecido_TerminaComCodigoDeErroSemProtocolo()
    {
        using var session = StdioSession.Start(_root, new[] { "--shell" }, new Dictionary<string, string>());

        var exit = await session.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(2, exit);
        Assert.Contains("Argumento desconhecido", session.StdErr);
        Assert.Empty(session.StdOutLines);
    }

    /// <summary>Sessão JSON-RPC por linhas sobre o processo do servidor. stdout só deve conter JSON.</summary>
    private sealed class StdioSession : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
        private readonly Process _process;
        private readonly StringBuilder _stderr = new();
        private readonly List<string> _stdoutLines = new();
        private readonly Task _stderrPump;
        private readonly StreamReader _stdout;
        private int _nextId = 1;

        private StdioSession(Process process)
        {
            _process = process;
            _stdout = process.StandardOutput;
            _stderrPump = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync()) is not null)
                {
                    lock (_stderr) _stderr.AppendLine(line);
                }
            });
        }

        public string StdErr { get { lock (_stderr) return _stderr.ToString(); } }
        public IReadOnlyList<string> StdOutLines => _stdoutLines;

        public static StdioSession Start(string workingRoot, string[] args, Dictionary<string, string> extraEnv)
        {
            var serverDir = Path.GetDirectoryName(typeof(TravelCutMcpFacade).Assembly.Location)!;
            var exe = Path.Combine(serverDir, "TravelCut.Mcp.exe");
            var dll = Path.Combine(serverDir, "TravelCut.Mcp.dll");

            var psi = new ProcessStartInfo
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CreateNoWindow = true,
            };

            if (File.Exists(exe))
            {
                psi.FileName = exe;
            }
            else
            {
                psi.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
                psi.ArgumentList.Add(dll);
            }
            foreach (var a in args) psi.ArgumentList.Add(a);

            psi.Environment["TRAVELCUT_MCP_DATABASE"] = Path.Combine(workingRoot, "smoke.db");
            psi.Environment["TRAVELCUT_EDITOR_ADAPTER"] = "mock";
            psi.Environment.Remove("TRAVELCUT_MCP_PROJECTS");
            foreach (var kv in extraEnv) psi.Environment[kv.Key] = kv.Value;

            var process = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar o servidor MCP.");
            return new StdioSession(process);
        }

        public async Task InitializeAsync()
        {
            var init = await RequestAsync("initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "smoke", ["version"] = "1" },
            });
            Assert.NotNull(init["result"]);
            await WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
        }

        public async Task<List<string>> ListToolsAsync()
        {
            var response = await RequestAsync("tools/list", new JsonObject());
            return response["result"]!["tools"]!.AsArray()
                .Select(t => t!["name"]!.GetValue<string>())
                .ToList();
        }

        public Task<JsonNode> CallToolAsync(string name, JsonObject arguments) =>
            RequestAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments });

        public async Task CloseInputAndAssertExitsAsync()
        {
            _process.StandardInput.Close();
            var exit = await WaitForExitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, exit);

            // Todo stdout deve ser JSON-RPC válido (uma mensagem por linha).
            await DrainStdoutAsync();
            foreach (var line in _stdoutLines)
            {
                using var doc = JsonDocument.Parse(line);
                Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
            }
        }

        public async Task<int> WaitForExitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await _process.WaitForExitAsync(cts.Token);
            await _stderrPump;
            return _process.ExitCode;
        }

        private async Task<JsonNode> RequestAsync(string method, JsonObject parameters)
        {
            int id = _nextId++;
            await WriteLineAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters,
            });

            while (true)
            {
                var line = await ReadLineAsync();
                var node = JsonNode.Parse(line)!;
                // Notificações do servidor (log) não têm id; ignoramos até a resposta correspondente.
                if (node["id"] is JsonNode idNode && idNode.GetValue<int>() == id)
                {
                    return node;
                }
            }
        }

        private async Task WriteLineAsync(JsonNode message)
        {
            await _process.StandardInput.WriteLineAsync(message.ToJsonString());
            await _process.StandardInput.FlushAsync();
        }

        private async Task<string> ReadLineAsync()
        {
            using var cts = new CancellationTokenSource(Timeout);
            var line = await _stdout.ReadLineAsync(cts.Token)
                ?? throw new InvalidOperationException("Servidor encerrou stdout inesperadamente. stderr: " + StdErr);
            _stdoutLines.Add(line);
            return line;
        }

        private async Task DrainStdoutAsync()
        {
            string? line;
            while ((line = await _stdout.ReadLineAsync()) is not null)
            {
                _stdoutLines.Add(line);
            }
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
            _process.Dispose();
        }
    }
}
