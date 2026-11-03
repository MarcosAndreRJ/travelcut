using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Import;

/// <summary>Espaço livre do volume (DriveInfo.AvailableFreeSpace) do caminho informado.</summary>
public sealed class DriveFreeSpaceProvider : IFreeSpaceProvider
{
    public long GetAvailableFreeSpace(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? full;
        return new DriveInfo(root).AvailableFreeSpace;
    }
}
