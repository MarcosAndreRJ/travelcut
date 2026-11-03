using System.IO;
using TravelCut.Application.Services;
using Xunit;

namespace TravelCut.UnitTests.Application;

/// <summary>
/// A4 (somente mensagem): atributos de arquivo de nuvem do OneDrive (sob demanda, não baixado) são reconhecidos
/// para trocar a mensagem de descarte. Reparse point comum continua sendo apenas "reparse".
/// </summary>
public class CloudPlaceholderClassifierTests
{
    [Theory]
    [InlineData(0x400000)] // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    [InlineData(0x40000)]  // FILE_ATTRIBUTE_RECALL_ON_OPEN
    [InlineData(0x1000)]   // FILE_ATTRIBUTE_OFFLINE
    public void AtributoDeNuvem_ehReconhecido(int attributeValue)
    {
        Assert.True(CloudPlaceholderClassifier.IsOnDemandCloudFile((FileAttributes)attributeValue));
    }

    [Theory]
    [InlineData(FileAttributes.Normal)]
    [InlineData(FileAttributes.ReparsePoint)]          // link/junção comum: não é nuvem
    [InlineData(FileAttributes.Hidden | FileAttributes.System)]
    public void AtributoComum_naoEhNuvem(FileAttributes attributes)
    {
        Assert.False(CloudPlaceholderClassifier.IsOnDemandCloudFile(attributes));
    }

    [Fact]
    public void Mensagem_mencionaOneDriveEManterSempreNoDispositivo()
    {
        Assert.Contains("OneDrive", CloudPlaceholderClassifier.OneDriveOnDemandMessage, StringComparison.Ordinal);
        Assert.Contains("Manter sempre neste dispositivo", CloudPlaceholderClassifier.OneDriveOnDemandMessage, StringComparison.Ordinal);
    }
}
