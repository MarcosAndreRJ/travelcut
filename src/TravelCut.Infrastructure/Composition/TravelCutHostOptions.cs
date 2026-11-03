namespace TravelCut.Infrastructure.Composition;

/// <summary>
/// Opções de composição compartilhadas pelos hosts (Desktop WPF e servidor MCP).
/// Nenhum item aqui é específico de WPF/WebView2.
/// </summary>
public sealed record TravelCutHostOptions
{
    /// <summary>Variável que seleciona o simulador do editor (sem .drp). Qualquer outro valor → DaVinci real.</summary>
    public const string EditorAdapterEnvVar = "TRAVELCUT_EDITOR_ADAPTER";

    /// <summary>Caminho do arquivo SQLite (ex.: %LOCALAPPDATA%\TravelCutStudio\travelcut.db).</summary>
    public required string DatabasePath { get; init; }

    /// <summary>Diretório base do executável; a bridge DaVinci procura <c>workers\resolve_bridge.py</c> a partir dele.</summary>
    public required string AppBaseDirectory { get; init; }

    /// <summary>true = MockResolveAdapter (SIMULATED); false = DaVinciResolveAdapter (sem fallback silencioso ao mock).</summary>
    public bool UseMockEditor { get; init; }

    /// <summary>Caminho padrão do banco do usuário: %LOCALAPPDATA%\TravelCutStudio\travelcut.db.</summary>
    public static string DefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TravelCutStudio",
            "travelcut.db");

    /// <summary>
    /// Monta as opções a partir do ambiente. <paramref name="databasePath"/> nulo usa o banco padrão do usuário.
    /// </summary>
    public static TravelCutHostOptions FromEnvironment(string appBaseDirectory, string? databasePath = null) => new()
    {
        DatabasePath = string.IsNullOrWhiteSpace(databasePath) ? DefaultDatabasePath() : databasePath,
        AppBaseDirectory = appBaseDirectory,
        UseMockEditor = string.Equals(
            Environment.GetEnvironmentVariable(EditorAdapterEnvVar), "mock", StringComparison.OrdinalIgnoreCase),
    };
}
