using TravelCut.Domain.Entities;
using Xunit;

namespace TravelCut.Tests.Domain;

public class ProjectStructureTests
{
    [Fact]
    public void HasValidFourFoldersStructure_ReturnsFalse_WhenFolderMissing()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "1-Bruto"));
            Directory.CreateDirectory(Path.Combine(tempDir, "2-Assets"));
            // Falta 3-Trabalho e 4-Final

            var project = new Project { RootPath = tempDir };
            bool valid = project.HasValidFourFoldersStructure(out string missing);

            Assert.False(valid);
            Assert.Equal("3-Trabalho", missing);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void HasValidFourFoldersStructure_ReturnsTrue_WhenAllPresent()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "1-Bruto"));
            Directory.CreateDirectory(Path.Combine(tempDir, "2-Assets"));
            Directory.CreateDirectory(Path.Combine(tempDir, "3-Trabalho"));
            Directory.CreateDirectory(Path.Combine(tempDir, "4-Final"));

            var project = new Project { RootPath = tempDir };
            bool valid = project.HasValidFourFoldersStructure(out string missing);

            Assert.True(valid);
            Assert.Empty(missing);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
