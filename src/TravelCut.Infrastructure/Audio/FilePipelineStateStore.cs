using System.Text.Json;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Audio;

/// <summary>Implementação de <see cref="IPipelineStateStore"/>: um JSON por assunto em <c>3-Trabalho/.state</c>, gravação atômica (tmp + rename).</summary>
public sealed class FilePipelineStateStore : IPipelineStateStore
{
    public const string StateFolder = ".state";
    public const string PendingFile = "pipeline-pending.json";
    public const string PostAnalysisFile = "post-analysis.json";

    private static readonly object Gate = new();

    public static string StatePath(string projectRoot, string fileName) =>
        Path.Combine(projectRoot, "3-Trabalho", StateFolder, fileName);

    public IReadOnlyList<PersistedPendingBatch> LoadPending(string projectRoot) =>
        Read<List<PersistedPendingBatch>>(StatePath(projectRoot, PendingFile)) ?? new List<PersistedPendingBatch>();

    public void SavePending(string projectRoot, IReadOnlyList<PersistedPendingBatch> batches)
    {
        string path = StatePath(projectRoot, PendingFile);
        if (batches.Count == 0)
        {
            lock (Gate)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            return;
        }
        Write(path, batches);
    }

    public PostAnalysisStatusDto? LoadPostAnalysis(string projectRoot) =>
        Read<PostAnalysisStatusDto>(StatePath(projectRoot, PostAnalysisFile));

    public void SavePostAnalysis(string projectRoot, PostAnalysisStatusDto status) =>
        Write(StatePath(projectRoot, PostAnalysisFile), status);

    private static T? Read<T>(string path) where T : class
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return null; // estado ilegível = sem estado (o cálculo por cache refaz o que faltar)
            }
        }
    }

    private static void Write<T>(string path, T value)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
