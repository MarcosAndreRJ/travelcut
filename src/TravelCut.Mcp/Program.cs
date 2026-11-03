using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Composition;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Mcp.Configuration;
using TravelCut.Mcp.Facade;
using TravelCut.Mcp.Tools;

// Servidor MCP local (stdio) do TravelCut. stdout é do protocolo: nenhum texto de diagnóstico vai para lá.
var parsed = McpCommandLine.Parse(args, Environment.GetEnvironmentVariable);
if (parsed.ShowHelp)
{
    Console.Error.WriteLine(McpCommandLine.Usage);
    return 0;
}
if (parsed.Configuration is null)
{
    Console.Error.WriteLine("TravelCut.Mcp: " + parsed.Error);
    Console.Error.WriteLine(McpCommandLine.Usage);
    return 2;
}
var configuration = parsed.Configuration;

// stderr em UTF-8 (sem BOM) para logs legíveis; stdout não é alterado.
Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true });

var hostOptions = TravelCutHostOptions.FromEnvironment(AppContext.BaseDirectory, configuration.DatabasePath);

// O servidor não migra o banco por padrão: quem atualiza o schema é o Desktop. Sem --migrate-database, só verifica
// (leitura, sem criar arquivo) e falha com código ≠ 0 e mensagem orientando a abrir o Desktop uma vez.
if (!configuration.MigrateDatabase)
{
    var readiness = TravelCutSchemaInspector.Inspect(hostOptions.DatabasePath);
    if (!readiness.IsReady)
    {
        Console.Error.WriteLine($"TravelCut.Mcp: o banco não está pronto para o servidor MCP ({readiness.Reason}).");
        Console.Error.WriteLine("Abra o TravelCut Desktop uma vez para atualizar o banco. Para migrar pelo servidor, use --migrate-database.");
        return 3;
    }
}

var builder = Host.CreateApplicationBuilder();

// Logs somente em stderr (o provedor padrão de console escreve em stdout e corromperia o protocolo).
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);
// SQL do EF Core só em Warning: evita ruído e qualquer detalhe de dados no stderr.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

builder.Services.AddSingleton(configuration);
builder.Services.AddTravelCutCore(hostOptions);
// Mutações em voo continuam quando o cliente cancela, mas são interrompidas no desligamento do servidor.
builder.Services.AddSingleton(sp => new McpIdempotencyCache(sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
builder.Services.AddSingleton<TravelCutMcpFacade>();

var mcp = builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "travelcut",
            Version = "0.1.0",
        };
        options.ServerInstructions =
            "Fachada do TravelCut Studio. Leituras são seguras. Mutações (submit_review, queue_analysis, apply_plan) " +
            "exigem idempotencyKey, só afetam projetos autorizados e apply_plan só aplica após dry-run e confirmação do usuário.";
    })
    .WithStdioServerTransport()
    .WithTools<TravelCutReadTools>();

if (configuration.AllowMutations)
{
    mcp.WithTools<TravelCutMutationTools>();
}

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<TravelCutMcpFacade>>();

// Migração explícita (mesmo passo do Desktop) somente com --migrate-database; sem a flag o schema já foi verificado acima.
if (configuration.MigrateDatabase)
{
    try
    {
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TravelCutDbContext>().EnsureSchemaUpdated();
        logger.LogInformation("Schema do banco atualizado por --migrate-database.");
    }
    catch (Exception ex) when (ex is InvalidOperationException or ProjectLockException or IOException or UnauthorizedAccessException or ArgumentException)
    {
        // Sem stack trace nem caminhos: código estável (migração em andamento, trava ou acesso ao arquivo).
        var code = ex is ProjectLockException locked ? locked.Code
            : ex is InvalidOperationException ? ex.Message
            : "ACESSO_AO_BANCO";
        Console.Error.WriteLine($"TravelCut.Mcp: não foi possível migrar o banco ({code}). Tente de novo quando outro processo do TravelCut não estiver usando o banco.");
        return 3;
    }
}

logger.LogInformation(
    "Servidor MCP TravelCut iniciado (stdio). Mutações: {Mutations}. Projetos autorizados: {ProjectCount}.",
    configuration.AllowMutations ? "habilitadas" : "desligadas",
    configuration.AuthorizedProjects.Count);

await host.RunAsync();
return 0;
