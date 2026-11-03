using TravelCut.Application.Planning;

namespace TravelCut.Infrastructure.Repositories;

/// <summary>Verificação de existência no sistema de arquivos local (somente leitura de metadados).</summary>
public sealed class FileSystemFileExistence : IFileExistence
{
    public Task<bool> ExistsAsync(string fullPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath));
    }
}
