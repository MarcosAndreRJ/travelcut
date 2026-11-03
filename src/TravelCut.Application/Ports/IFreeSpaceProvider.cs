namespace TravelCut.Application.Ports;

/// <summary>Espaço livre disponível no volume de um caminho (injetável para testes).</summary>
public interface IFreeSpaceProvider
{
    long GetAvailableFreeSpace(string path);
}
