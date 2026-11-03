using System.ComponentModel;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Import;
using Xunit;

namespace TravelCut.Tests.Import;

public class ImportFailureMessagesTests
{
    [Fact]
    public void AccessDenied_HasOwnMessage()
    {
        Assert.Equal(ImportFailureMessages.AccessDenied, ImportFailureMessages.Describe(new UnauthorizedAccessException("C:\\privado\\x.mp4")));
    }

    [Fact]
    public void SharingViolation_IsFileInUse()
    {
        // HResult Win32 nos 16 bits inferiores: ERROR_SHARING_VIOLATION (32) = 0x80070020.
        var ex = new IOException("O processo não pode acessar o arquivo 'C:\\privado\\a.mp4'", unchecked((int)0x80070020));

        Assert.Equal(ImportFailureMessages.FileInUse, ImportFailureMessages.Describe(ex));
    }

    [Fact]
    public void DiskFull_IsDiskFullMessage()
    {
        var ex = new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));

        Assert.Equal(ImportFailureMessages.DiskFull, ImportFailureMessages.Describe(ex));
    }

    [Fact]
    public void Win32ExceptionWithHandleDiskFull_IsDiskFullMessage()
    {
        var ex = new Win32Exception(39);

        Assert.Equal(ImportFailureMessages.DiskFull, ImportFailureMessages.Describe(ex));
    }

    [Fact]
    public void PartialFileConflict_KeepsActionableMessage_WithoutPath()
    {
        var message = ImportFailureMessages.Describe(new PartialFileConflictException());

        Assert.Equal(ImportFailureMessages.PartialFileExists, message);
        Assert.Contains(".tcpart", message);
    }

    [Fact]
    public void OtherErrors_UseGenericMessage_WithoutRawText()
    {
        var message = ImportFailureMessages.Describe(new IOException("falha bruta em C:\\privado\\clip.mp4"));

        Assert.Equal(ImportFailureMessages.Generic, message);
        Assert.DoesNotContain("privado", message);
        Assert.DoesNotContain("C:\\", message);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    public void UnknownExceptionTypes_UseGenericMessage(Type type)
    {
        var ex = (Exception)Activator.CreateInstance(type, "segredo C:\\privado")!;

        Assert.Equal(ImportFailureMessages.Generic, ImportFailureMessages.Describe(ex));
    }
}

public class ListDirectoryLimitTests
{
    private static DirectoryEntryDto Entry(int i) => new($"f{i}.mp4", $"C:\\x\\f{i}.mp4", false, 1, "Video");

    [Fact]
    public void ListingWithinLimit_IsNotTruncated()
    {
        var entries = Enumerable.Range(0, 5).Select(Entry).ToList();

        var (result, truncated) = ListDirectoryUseCase.LimitEntries(entries, max: 5);

        Assert.False(truncated);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ListingAboveLimit_IsCutAndFlaggedAsTruncated()
    {
        var entries = Enumerable.Range(0, ListDirectoryUseCase.MaxEntries + 1).Select(Entry).ToList();

        var (result, truncated) = ListDirectoryUseCase.LimitEntries(entries);

        Assert.True(truncated);
        Assert.Equal(ListDirectoryUseCase.MaxEntries, result.Count);
    }

    [Fact]
    public void ListDirectoryResponse_SerializesTruncatedFlagInCamelCase()
    {
        var response = new ListDirectoryResponse("C:\\x", "C:\\", new List<DirectoryEntryDto>(), Truncated: true);

        var json = System.Text.Json.JsonSerializer.Serialize(response);

        Assert.Contains("\"truncated\":true", json);
    }
}
