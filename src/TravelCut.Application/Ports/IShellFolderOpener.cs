namespace TravelCut.Application.Ports;

/// <summary>
/// Abre pastas no explorador de arquivos do SO. Implementação em Infrastructure (processo seguro, sem shell).
/// O caminho já deve ter sido validado pelo caso de uso (absoluto, dentro de um projeto conhecido).
/// </summary>
public interface IShellFolderOpener
{
    /// <summary>
    /// Se <paramref name="selectFile"/> for false, abre a pasta <paramref name="fullPath"/>.
    /// Se for true, abre a pasta que contém o arquivo com o item selecionado.
    /// </summary>
    void OpenInExplorer(string fullPath, bool selectFile);
}
