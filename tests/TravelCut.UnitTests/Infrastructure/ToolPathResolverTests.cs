using TravelCut.Infrastructure.Tooling;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

public class ToolPathResolverTests
{
    [Fact]
    public void FindInPath_WithEmptyOrNull_ReturnsNull()
    {
        Assert.Null(ToolPathResolver.FindInPath(""));
        Assert.Null(ToolPathResolver.FindInPath("   "));
        Assert.Null(ToolPathResolver.FindInPath("non_existent_binary_12345_xyz.exe"));
    }

    [Fact]
    public void FindAllInPath_WithNonExistentBinary_ReturnsEmptyList()
    {
        var results = ToolPathResolver.FindAllInPath("definitely_not_a_binary_12345.exe");
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void FindPythonCandidates_WithConfiguredPath_IncludesConfiguredFirst()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var candidates = ToolPathResolver.FindPythonCandidates(tempFile);
            Assert.NotEmpty(candidates);
            Assert.Equal(Path.GetFullPath(tempFile), candidates[0], ignoreCase: true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FindPythonCandidates_WithNonExistentPath_DoesNotIncludeNonExistent()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "does_not_exist_python_" + Guid.NewGuid().ToString("N") + ".exe");
        var candidates = ToolPathResolver.FindPythonCandidates(nonExistent);
        Assert.DoesNotContain(nonExistent, candidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindNodeCandidates_WithConfiguredPath_IncludesConfiguredFirst()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var candidates = ToolPathResolver.FindNodeCandidates(tempFile);
            Assert.NotEmpty(candidates);
            Assert.Equal(Path.GetFullPath(tempFile), candidates[0], ignoreCase: true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FindHyperFramesCandidates_WithConfiguredPath_IncludesConfiguredFirst()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var candidates = ToolPathResolver.FindHyperFramesCandidates(tempFile);
            Assert.NotEmpty(candidates);
            Assert.Equal(Path.GetFullPath(tempFile), candidates[0], ignoreCase: true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FindFfmpegCandidates_WithConfiguredPath_IncludesConfiguredFirst()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var candidates = ToolPathResolver.FindFfmpegCandidates(tempFile);
            Assert.NotEmpty(candidates);
            Assert.Equal(Path.GetFullPath(tempFile), candidates[0], ignoreCase: true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Candidates_DoNotContainDuplicates()
    {
        var candidates = ToolPathResolver.FindNodeCandidates(null);
        var distinct = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(distinct.Count, candidates.Count);
    }

    [Fact]
    public void OfficialWhisperLargeV3Sha256_MatchesOfficialOpenAiHash()
    {
        // Conforme definido no repositório oficial da OpenAI:
        // https://github.com/openai/whisper/blob/main/whisper/__init__.py
        const string expectedOfficialHash = "e5b1a55b89c1367dacf97e3e19bfd829a01529dbfdeefa8caeb59b3f1b81dadb";
        Assert.Equal(expectedOfficialHash, ToolPathResolver.OfficialWhisperLargeV3Sha256);
        Assert.Contains(expectedOfficialHash, ToolPathResolver.OfficialWhisperLargeV3Url);
    }

    [Fact]
    public void PersistentToolPaths_AreUnderLocalAppData()
    {
        var toolsDir = ToolPathResolver.GetToolsDirectory();
        var venvDir = ToolPathResolver.GetPersistentPythonVenvDirectory();
        var venvExe = ToolPathResolver.GetPersistentPythonVenvExecutable();
        var hfDir = ToolPathResolver.GetPersistentHyperFramesDirectory();
        var hfCli = ToolPathResolver.GetPersistentHyperFramesCliPath();

        Assert.NotEmpty(toolsDir);
        Assert.Contains("TravelCutStudio", toolsDir);
        Assert.Contains("tools", toolsDir);

        Assert.StartsWith(toolsDir, venvDir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(venvDir, venvExe, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("python.exe", venvExe, StringComparison.OrdinalIgnoreCase);

        Assert.StartsWith(toolsDir, hfDir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(hfDir, hfCli, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("cli.js", hfCli, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyFileSha256_WithValidAndInvalidHash_ReturnsExpectedResult()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "TravelCut Studio SHA256 Test Content");
            var computed = ToolPathResolver.ComputeFileSha256(tempFile);
            Assert.NotEmpty(computed);

            Assert.True(ToolPathResolver.VerifyFileSha256(tempFile, computed));
            Assert.True(ToolPathResolver.VerifyFileSha256(tempFile, computed.ToUpperInvariant()));
            Assert.False(ToolPathResolver.VerifyFileSha256(tempFile, "0000000000000000000000000000000000000000000000000000000000000000"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
