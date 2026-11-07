using System.Text;

namespace TravelCut.Application.Audio;

/// <summary>
/// O que foi realmente enviado ao processo do worker. Não é uma reconstrução: os valores são os mesmos
/// repassados ao IProcessRunner. O worker fala NDJSON por stdin; a mídia e o modelo vão em StandardInput,
/// não nos argumentos.
/// </summary>
public sealed record WhisperExecutionInfo(
    string RequestId,
    string PythonExecutable,
    IReadOnlyList<string> Arguments,
    string WorkerScript,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string StandardInput,
    string MediaPath,
    string Model,
    string Language,
    string? Device);

/// <summary>ProcessState: completed | failed | canceled | timed_out | not_started. "completed" só com resposta válida do worker.</summary>
public sealed record WhisperExecutionResult(string ProcessState, int? ExitCode, string? Error);

public interface IWhisperExecutionObserver
{
    void OnStarted(WhisperExecutionInfo info);

    /// <summary>Stream: stdout | stderr. O conteúdo da transcrição nunca passa por aqui.</summary>
    void OnLog(string stream, string text);

    void OnFinished(WhisperExecutionResult result);
}

/// <summary>
/// Script PowerShell que repete a chamada real ao worker. Compatível com Windows PowerShell 5.1 (sem ArgumentList).
/// Todos os valores entram como literais de aspas simples; o JSON é o stdin já serializado da execução.
/// </summary>
public static class WhisperDiagnosticScript
{
    public static string Build(WhisperExecutionInfo info)
    {
        var script = new StringBuilder();
        void Line(string text) => script.Append(text).Append('\n');

        Line("# TravelCut Studio - diagnóstico manual do worker Whisper.");
        Line("# Repete a chamada real: mesmo executável, argumentos, ambiente e stdin NDJSON.");
        Line("# ATENÇÃO: executa a transcrição completa (pode levar muito tempo). Não instala nada, não baixa modelos");
        Line("# e só lê a mídia. Stdout, stderr e o texto transcrito ficam no arquivo indicado em $saveTo.");
        Line("# Contém caminhos locais: revise antes de compartilhar.");
        Line("& {");
        Line("  $ErrorActionPreference = 'Stop'");
        Line("  $saveTo = Join-Path $env:TEMP ('travelcut-whisper-diagnostico-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')");
        Line("  $psi = New-Object System.Diagnostics.ProcessStartInfo");
        Line($"  $psi.FileName = {Literal(info.PythonExecutable)}");
        Line($"  $psi.Arguments = {Literal(BuildCommandLine(info.Arguments))}");
        Line($"  $psi.WorkingDirectory = {Literal(info.WorkingDirectory)}");
        Line("  $psi.UseShellExecute = $false");
        Line("  $psi.RedirectStandardInput = $true");
        Line("  $psi.RedirectStandardOutput = $true");
        Line("  $psi.RedirectStandardError = $true");
        Line("  $psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)");
        Line("  $psi.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)");
        foreach (var variable in info.EnvironmentVariables.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            Line($"  $psi.EnvironmentVariables[{Literal(variable.Key)}] = {Literal(variable.Value)}");
        }

        Line("  # NDJSON enviado ao stdin: uma mensagem por linha; a última encerra o worker.");
        Line("  $stdinLines = @(");
        var lines = info.StandardInput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            Line($"    {Literal(lines[i])}{(i < lines.Length - 1 ? "," : string.Empty)}");
        }
        Line("  )");
        Line("  Write-Host \"Resultado será salvo em: $saveTo\"");
        Line("  $process = [System.Diagnostics.Process]::Start($psi)");
        Line("  $stderrTask = $process.StandardError.ReadToEndAsync()");
        Line("  foreach ($stdinLine in $stdinLines) { $process.StandardInput.WriteLine($stdinLine) }");
        Line("  $process.StandardInput.Close()");
        Line("  Add-Content -LiteralPath $saveTo -Encoding UTF8 -Value '=== STDOUT ==='");
        Line("  while ($null -ne ($line = $process.StandardOutput.ReadLine())) {");
        Line("    Write-Host $line");
        Line("    Add-Content -LiteralPath $saveTo -Encoding UTF8 -Value $line");
        Line("  }");
        Line("  $process.WaitForExit()");
        Line("  $stderrText = $stderrTask.GetAwaiter().GetResult()");
        Line("  Write-Host '=== STDERR ==='");
        Line("  Write-Host $stderrText");
        Line("  Add-Content -LiteralPath $saveTo -Encoding UTF8 -Value '=== STDERR ==='");
        Line("  Add-Content -LiteralPath $saveTo -Encoding UTF8 -Value $stderrText");
        Line("  $summary = \"=== CODIGO DE SAIDA: $($process.ExitCode) ===\"");
        Line("  Write-Host $summary");
        Line("  Add-Content -LiteralPath $saveTo -Encoding UTF8 -Value $summary");
        Line("}");
        return script.ToString();
    }

    /// <summary>
    /// Literal de aspas simples: nada é interpolado. O PowerShell também aceita aspas tipográficas como
    /// delimitador, por isso todas são duplicadas.
    /// </summary>
    public static string Literal(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('\'');
        foreach (char c in value)
        {
            if (c is '\'' or '‘' or '’' or '‚' or '‛') text.Append(c);
            text.Append(c);
        }
        return text.Append('\'').ToString();
    }

    /// <summary>Linha de comando equivalente a ArgumentList, pelas regras de análise de argv do Windows.</summary>
    public static string BuildCommandLine(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(QuoteArgument));

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => c is ' ' or '\t' or '"')) return argument;

        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            // Barras antes de aspas precisam ser dobradas para não escaparem a aspa.
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(c);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
