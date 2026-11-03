using TravelCut.Domain.Enums;

namespace TravelCut.Domain.Entities;

public class Project
{
    public Guid ProjectId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAtUtc { get; set; } = DateTime.UtcNow;
    public int ProfileVersion { get; set; } = 1;

    public static readonly string[] RequiredFourFolders = ["1-Bruto", "2-Assets", "3-Trabalho", "4-Final"];

    public bool HasValidFourFoldersStructure(out string missingFolder)
    {
        missingFolder = string.Empty;
        if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
        {
            missingFolder = "Diretório raiz não existe";
            return false;
        }

        foreach (var folder in RequiredFourFolders)
        {
            string fullPath = Path.Combine(RootPath, folder);
            if (!Directory.Exists(fullPath))
            {
                missingFolder = folder;
                return false;
            }
        }

        return true;
    }

    public List<string> EnsureFourFoldersStructure()
    {
        var created = new List<string>();
        if (string.IsNullOrWhiteSpace(RootPath)) return created;

        if (!Directory.Exists(RootPath))
        {
            Directory.CreateDirectory(RootPath);
            created.Add(RootPath);
        }

        foreach (var folder in RequiredFourFolders)
        {
            string fullPath = Path.Combine(RootPath, folder);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                created.Add(folder);
            }
        }

        return created;
    }

    public string GetSubfolderPath(string subfolderName) => Path.Combine(RootPath, subfolderName);
}
