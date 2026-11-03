using System.IO;

namespace TravelCut.Application.Services;

/// <summary>
/// Classificação pura de arquivos de nuvem (OneDrive "sob demanda"). Um arquivo com esses atributos é um placeholder:
/// os bytes não estão no disco. Continuam sendo DESCARTADOS pela importação (não são aceitos); esta classe só decide
/// a MENSAGEM mostrada ao usuário.
/// FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000, FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000, FILE_ATTRIBUTE_OFFLINE = 0x1000.
/// </summary>
public static class CloudPlaceholderClassifier
{
    public const string OneDriveOnDemandMessage =
        "arquivo sob demanda do OneDrive (não baixado) — marque 'Manter sempre neste dispositivo'";

    // System.IO.FileAttributes não tem os flags de recall: valores Win32 fixos.
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;

    private const FileAttributes CloudAttributes =
        RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline;

    /// <summary>True quando o atributo indica conteúdo sob demanda em nuvem (não baixado).</summary>
    public static bool IsOnDemandCloudFile(FileAttributes attributes)
        => (attributes & CloudAttributes) != 0;
}
