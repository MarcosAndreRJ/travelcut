using TravelCut.Application.Services;
using Xunit;

namespace TravelCut.Tests.Import;

public class ImportPathRulesTests
{
    [Theory]
    [InlineData("..\\x", false)]
    [InlineData("../x", false)]
    [InlineData("a/../b", false)]
    [InlineData("a/./b", false)]
    [InlineData("/abs", false)]
    [InlineData("\\abs", false)]
    [InlineData("C:\\Windows", false)]
    [InlineData("bad:name", false)]
    [InlineData("bad*name", false)]
    [InlineData("a/b", true)]
    [InlineData("viagem\\2026\\japao", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void SubfolderIsSanitized(string input, bool accepted)
    {
        bool ok = ImportPathRules.TryNormalizeSubfolder(input, out var normalized);

        Assert.Equal(accepted, ok);
        if (accepted && input.Trim().Length > 0)
        {
            Assert.DoesNotContain("..", normalized);
            Assert.DoesNotContain('\\', normalized);
        }
    }

    [Fact]
    public void SubfolderIsNormalizedToForwardSlashes()
    {
        Assert.True(ImportPathRules.TryNormalizeSubfolder("viagem\\2026//japao/", out var normalized));
        Assert.Equal("viagem/2026/japao", normalized);
    }

    [Theory]
    [InlineData("1-Bruto", true, "1-Bruto")]
    [InlineData("2-assets", true, "2-Assets")]
    [InlineData("3-Trabalho", false, "")]
    [InlineData("4-Final", false, "")]
    [InlineData(null, false, "")]
    public void DestinationIsRestrictedToBrutoOrAssets(string? input, bool accepted, string canonical)
    {
        bool ok = ImportPathRules.TryResolveDestination(input, out var folder);

        Assert.Equal(accepted, ok);
        if (accepted)
        {
            Assert.Equal(canonical, folder);
        }
    }

    [Fact]
    public void TagsAreTrimmedDeduplicatedCaseInsensitiveAndCapped()
    {
        var input = new List<string> { "  Praia ", "praia", "", "   ", "Trilha" };
        for (int i = 0; i < 30; i++)
        {
            input.Add("tag" + i);
        }

        var tags = ImportPathRules.NormalizeTags(input);

        Assert.Equal(ImportPathRules.MaxTags, tags.Count);
        Assert.Equal("Praia", tags[0]);
        Assert.Equal("Trilha", tags[1]);
        Assert.Equal(1, tags.Count(t => string.Equals(t, "praia", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void InsideDirectoryDetectsChildrenButNotSiblingPrefixOrSameFolder()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tc_rules", "bruto"));
        string child = Path.Combine(root, "sub", "clip.mp4");
        string sibling = root + "X" + Path.DirectorySeparatorChar + "clip.mp4";

        Assert.True(ImportPathRules.IsInsideDirectory(root, child));
        Assert.False(ImportPathRules.IsInsideDirectory(root, sibling));
        Assert.False(ImportPathRules.IsInsideDirectory(root, root));
        Assert.False(ImportPathRules.IsInsideDirectory(root, Path.Combine(root, "..", "fora.mp4")));
    }

    [Fact]
    public void LibraryRelativePathUsesForwardSlashes()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tc_rules_rel"));
        string file = Path.Combine(root, "1-Bruto", "viagem", "DJI.MP4");

        Assert.Equal("1-Bruto/viagem/DJI.MP4", ImportPathRules.ToLibraryRelativePath(root, file));
    }
}
