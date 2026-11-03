using System.Diagnostics;

namespace TravelCut.Tests;

/// <summary>Junções de diretório para testes de reparse points. mklink /J não exige elevação.</summary>
internal static class TestJunction
{
    /// <summary>Cria a junção <paramref name="link"/> apontando para <paramref name="target"/>. Falha explícita se não for possível.</summary>
    public static void Create(string link, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("mklink");
        psi.ArgumentList.Add("/J");
        psi.ArgumentList.Add(link);
        psi.ArgumentList.Add(target);

        using var process = Process.Start(psi)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link))
        {
            throw new InvalidOperationException("Não foi possível criar a junção de teste (mklink /J).");
        }
    }

    /// <summary>Remove apenas o link (sem recursão): o conteúdo do alvo permanece.</summary>
    public static void Remove(string link)
    {
        if (Directory.Exists(link))
        {
            Directory.Delete(link);
        }
    }
}
