using Microsoft.Data.Sqlite;
using TravelCut.Infrastructure.Composition;

namespace TravelCut.Infrastructure.Persistence;

/// <summary>
/// Resultado da verificação de schema: pronto, ou o motivo genérico (sem caminhos) e um código estável
/// (<see cref="SchemaReadinessCodes.NotReady"/> ou <see cref="SchemaReadinessCodes.TooNew"/>).
/// </summary>
public sealed record SchemaReadiness(bool IsReady, string? Reason, string? Code = null)
{
    public static SchemaReadiness Ready { get; } = new(true, null, null);
}

public static class SchemaReadinessCodes
{
    /// <summary>Banco ausente, incompleto, ilegível ou inacessível: abrir o Desktop (ou migrar) resolve.</summary>
    public const string NotReady = "NOT_READY";

    /// <summary>Banco com marcador de versão maior que a conhecida por este build: não migrar para trás.</summary>
    public const string TooNew = "TOO_NEW";
}

/// <summary>
/// Verificação barata e somente leitura do schema do TravelCut. Usada pelo servidor MCP, que NÃO migra o banco por padrão
/// (a migração é do Desktop; o MCP só migra com <c>--migrate-database</c>). Nunca cria o arquivo nem altera o journal_mode.
/// Nunca lança: qualquer falha de leitura vira resultado não pronto, sem stack trace.
/// </summary>
public static class TravelCutSchemaInspector
{
    /// <summary>Tabelas exigidas pelo servidor: criadas por <see cref="TravelCutDbContext.EnsureSchemaUpdated"/>.</summary>
    public static readonly string[] RequiredTables = ["Projects", "MediaAssets", "PlanRevisions", "EditDecisions", "SchemaMarkers"];

    /// <summary>Coluna adicionada em versões posteriores; sua ausência indica banco antigo.</summary>
    public const string RequiredEditDecisionColumn = "IsRedo";

    public static SchemaReadiness Inspect(string databasePath)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
            if (!File.Exists(databasePath))
            {
                return NotReady("banco inexistente");
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                DefaultTimeout = TravelCutServiceCollectionExtensions.SqliteBusyTimeoutSeconds,
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();

            var tables = ReadNames(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");
            foreach (var table in RequiredTables)
            {
                if (!tables.Contains(table, StringComparer.OrdinalIgnoreCase))
                {
                    return NotReady($"tabela {table} ausente");
                }
            }

            var columns = ReadNames(connection, "SELECT name FROM pragma_table_info('EditDecisions')");
            if (!columns.Contains(RequiredEditDecisionColumn, StringComparer.OrdinalIgnoreCase))
            {
                return NotReady($"coluna EditDecisions.{RequiredEditDecisionColumn} ausente");
            }

            return CheckSchemaVersion(connection);
        }
        catch (SqliteException ex)
        {
            return NotReady($"banco ilegível (código {ex.SqliteErrorCode})");
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or IOException)
        {
            // Sem detalhes do sistema de arquivos: o log/diagnóstico fica com quem chama; aqui só o motivo genérico.
            return NotReady("banco inacessível (permissão negada, arquivo em uso ou caminho inválido)");
        }
    }

    /// <summary>
    /// Marcador "schema/N" obrigatório (gravado como último passo da migração). Ausente → NOT_READY; maior que
    /// <see cref="TravelCutDbContext.CurrentSchemaVersion"/> → TOO_NEW.
    /// </summary>
    private static SchemaReadiness CheckSchemaVersion(SqliteConnection connection)
    {
        int? highest = null;
        foreach (var name in ReadNames(connection,
                     $"SELECT Name FROM SchemaMarkers WHERE Name LIKE '{TravelCutDbContext.SchemaMarkerPrefix}%'"))
        {
            if (TryParseVersion(name, out var version) && (highest is null || version > highest))
            {
                highest = version;
            }
        }

        if (highest is null)
        {
            return NotReady("marcador de versão do schema ausente (abra o TravelCut Desktop para atualizar o banco)");
        }
        if (highest > TravelCutDbContext.CurrentSchemaVersion)
        {
            return new SchemaReadiness(false,
                "banco de versão mais nova que a suportada por este aplicativo (atualize o TravelCut).",
                SchemaReadinessCodes.TooNew);
        }
        return SchemaReadiness.Ready;
    }

    private static bool TryParseVersion(string markerName, out int version)
    {
        version = 0;
        return markerName.StartsWith(TravelCutDbContext.SchemaMarkerPrefix, StringComparison.Ordinal)
            && int.TryParse(markerName.AsSpan(TravelCutDbContext.SchemaMarkerPrefix.Length),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out version);
    }

    private static SchemaReadiness NotReady(string reason) => new(false, reason, SchemaReadinessCodes.NotReady);

    private static List<string> ReadNames(SqliteConnection connection, string sql)
    {
        var names = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0)) names.Add(reader.GetString(0));
        }
        return names;
    }
}
