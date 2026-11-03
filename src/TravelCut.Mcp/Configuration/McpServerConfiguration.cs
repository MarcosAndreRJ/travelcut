namespace TravelCut.Mcp.Configuration;

/// <summary>
/// Configuração do servidor, definida pelo operador (linha de comando ou ambiente), nunca pelo cliente MCP.
/// </summary>
/// <param name="AllowMutations">true somente com <c>--allow-mutations</c>. Padrão: false (mutações não são registradas).</param>
/// <param name="AuthorizedProjects">Projetos autorizados (<c>--project</c> repetível e/ou <c>TRAVELCUT_MCP_PROJECTS</c>). Vazio = nenhum.</param>
/// <param name="DatabasePath">Banco alternativo (<c>TRAVELCUT_MCP_DATABASE</c>); null = mesmo banco do Desktop.</param>
/// <param name="MigrateDatabase">true somente com <c>--migrate-database</c>: o servidor atualiza o schema na inicialização.
/// Padrão false: o servidor só verifica o schema e falha se ele não estiver pronto (quem migra é o Desktop).</param>
public sealed record McpServerConfiguration(
    bool AllowMutations,
    IReadOnlySet<Guid> AuthorizedProjects,
    string? DatabasePath,
    bool MigrateDatabase = false)
{
    public const string AllowMutationsFlag = "--allow-mutations";
    public const string ProjectFlag = "--project";
    public const string MigrateDatabaseFlag = "--migrate-database";
    public const string ProjectsEnvVar = "TRAVELCUT_MCP_PROJECTS";
    public const string DatabaseEnvVar = "TRAVELCUT_MCP_DATABASE";
}

/// <summary>Resultado do parse: configuração, ou pedido de ajuda, ou erro (mensagem sem valores sensíveis).</summary>
public sealed record McpParseResult(McpServerConfiguration? Configuration, bool ShowHelp, string? Error);

public static class McpCommandLine
{
    public const string Usage =
        "Uso: TravelCut.Mcp [--allow-mutations] [--project <guid>]... [--migrate-database]\n" +
        "  --allow-mutations   registra as ferramentas de mutação (padrão: somente leitura)\n" +
        "  --project <guid>    autoriza um projeto (repetível)\n" +
        "  --migrate-database  atualiza o schema do banco na inicialização (padrão: não migra; abra o Desktop uma vez)\n" +
        "Ambiente: TRAVELCUT_MCP_PROJECTS=<guid1>;<guid2>  TRAVELCUT_MCP_DATABASE=<caminho absoluto do .db>\n" +
        "Logs vão somente para stderr; stdout é o protocolo MCP.";

    /// <summary>Interpreta argumentos e ambiente. Não lança exceção por entrada inválida.</summary>
    public static McpParseResult Parse(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        bool allow = false;
        bool help = false;
        bool migrate = false;
        var projects = new HashSet<Guid>();

        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i] ?? string.Empty;
            if (arg == McpServerConfiguration.AllowMutationsFlag)
            {
                allow = true;
            }
            else if (arg == McpServerConfiguration.MigrateDatabaseFlag)
            {
                migrate = true;
            }
            else if (arg is "--help" or "-h")
            {
                help = true;
            }
            else if (arg == McpServerConfiguration.ProjectFlag || arg.StartsWith(McpServerConfiguration.ProjectFlag + "=", StringComparison.Ordinal))
            {
                string? value;
                if (arg == McpServerConfiguration.ProjectFlag)
                {
                    if (i + 1 >= args.Count)
                    {
                        return Fail($"{McpServerConfiguration.ProjectFlag} exige um GUID.");
                    }
                    value = args[++i];
                }
                else
                {
                    value = arg[(McpServerConfiguration.ProjectFlag.Length + 1)..];
                }

                if (!Guid.TryParse(value, out var id))
                {
                    return Fail($"{McpServerConfiguration.ProjectFlag} exige um GUID válido.");
                }
                projects.Add(id);
            }
            else
            {
                return Fail($"Argumento desconhecido: {Truncate(arg, 40)}");
            }
        }

        var fromEnv = environment(McpServerConfiguration.ProjectsEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            foreach (var part in fromEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Guid.TryParse(part, out var id))
                {
                    return Fail($"{McpServerConfiguration.ProjectsEnvVar} contém um valor que não é GUID.");
                }
                projects.Add(id);
            }
        }

        string? database = environment(McpServerConfiguration.DatabaseEnvVar);
        if (string.IsNullOrWhiteSpace(database))
        {
            database = null;
        }
        else if (!Path.IsPathFullyQualified(database))
        {
            return Fail($"{McpServerConfiguration.DatabaseEnvVar} deve ser um caminho absoluto.");
        }

        if (help)
        {
            return new McpParseResult(null, ShowHelp: true, Error: null);
        }

        var config = new McpServerConfiguration(allow, projects, database, migrate);
        return new McpParseResult(config, ShowHelp: false, Error: null);
    }

    private static McpParseResult Fail(string message) => new(null, ShowHelp: false, Error: message);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
