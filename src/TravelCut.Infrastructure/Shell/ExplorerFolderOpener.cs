using System.Diagnostics;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Shell;

/// <summary>
/// Abre pastas com explorer.exe. Processo iniciado sem shell (UseShellExecute=false) e com argumentos
/// em ArgumentList: nenhum caminho passa por interpolação de linha de comando.
/// O caminho é validado pelo caso de uso antes de chegar aqui.
/// </summary>
public sealed class ExplorerFolderOpener : IShellFolderOpener
{
    private const string ExplorerExecutable = "explorer.exe";

    public void OpenInExplorer(string fullPath, bool selectFile)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException("Caminho deve ser absoluto.", nameof(fullPath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ExplorerExecutable,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // "/select," + caminho é um único argumento: o explorer seleciona o arquivo na pasta que o contém.
        startInfo.ArgumentList.Add(selectFile ? "/select," + fullPath : fullPath);

        // explorer.exe costuma retornar código != 0 mesmo com sucesso: não se avalia o ExitCode.
        using var process = Process.Start(startInfo);
    }
}
