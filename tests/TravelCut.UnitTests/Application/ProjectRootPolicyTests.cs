using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Application;

/// <summary>
/// M4 (backend): raiz de unidade, pastas de sistema, AppData e perfil do usuário não viram projeto (INVALID_PROJECT_ROOT).
/// Testes de regra com pastas simuladas (Windows); o caso real usa as pastas do ambiente e não cria nada no disco.
/// </summary>
public class ProjectRootPolicyTests
{
    private static readonly ProjectRootSystemFolders Folders = new(
        ContainingFolders: new[] { @"C:\Windows", @"C:\Program Files", @"C:\ProgramData" },
        ExactFolders: new[]
        {
            @"C:\Users\Ana",
            @"C:\Users\Ana\AppData",
            @"C:\Users\Ana\AppData\Local",
            @"C:\Users\Ana\AppData\Roaming",
            @"C:\Users\Ana\AppData\LocalLow",
        });

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"\\servidor\compartilhado")]
    public void RaizDeUnidadeOuCompartilhamento_ehRecusada(string root)
    {
        Assert.NotNull(ProjectRootPathRules.FindViolation(ProjectRootPathRules.Normalize(root), Folders));
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\drivers")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\Algo\Dentro")]
    [InlineData(@"C:\ProgramData\Algo")]
    public void PastasDeSistemaEDescendentes_saoRecusadas(string root)
    {
        Assert.NotNull(ProjectRootPathRules.FindViolation(root, Folders));
    }

    [Theory]
    [InlineData(@"C:\Users\Ana")]
    [InlineData(@"C:\Users\Ana\AppData")]
    [InlineData(@"C:\Users\Ana\AppData\Local")]
    [InlineData(@"C:\Users\Ana\AppData\Roaming")]
    public void PerfilEPastasAppDataExatas_saoRecusadas(string root)
    {
        Assert.NotNull(ProjectRootPathRules.FindViolation(root, Folders));
    }

    [Theory]
    [InlineData(@"C:\WindowsApps\Projeto")]          // prefixo parecido, mas não é descendente de C:\Windows
    [InlineData(@"C:\Users\Ana\Documentos\Viagem")]
    [InlineData(@"C:\Users\Ana\AppData\Local\Temp\tc_x")] // descendente de AppData não é recusado (temp do usuário)
    [InlineData(@"K:\Trabalho\Viagem")]
    public void PastasComuns_saoAceitas(string root)
    {
        Assert.Null(ProjectRootPathRules.FindViolation(root, Folders));
    }

    [Fact]
    public void Normalize_RemoveBarraFinalEMantemRaizDeUnidade()
    {
        Assert.Equal(@"C:\Trabalho\Viagem", ProjectRootPathRules.Normalize(@"C:\Trabalho\Viagem\"));
        Assert.Equal(@"C:\Trabalho\Viagem", ProjectRootPathRules.Normalize(@"C:\Trabalho\Viagem"));
        Assert.Equal(@"c:\", ProjectRootPathRules.Normalize(@"c:\"));  // caixa da unidade preservada
    }

    [Fact]
    public void PastasDoAmbiente_incluemPerfilEAppData()
    {
        var env = ProjectRootPathRules.CurrentSystemFolders();
        string profile = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd(Path.DirectorySeparatorChar);

        Assert.Contains(profile, env.ExactFolders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(profile, "AppData"), env.ExactFolders, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseCase_RecusaPerfilDoUsuario_SemCriarNada()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var useCase = NewUseCase();

        var outcome = await useCase.ExecuteAsync(profile, autoCreateFolders: true);

        Assert.False(outcome.Success);
        Assert.Equal("INVALID_PROJECT_ROOT", outcome.ErrorCode);
    }

    [Fact]
    public async Task UseCase_RecusaRaizDeUnidade()
    {
        string driveRoot = Path.GetPathRoot(Path.GetTempPath())!;
        var useCase = NewUseCase();

        var outcome = await useCase.ExecuteAsync(driveRoot, autoCreateFolders: true);

        Assert.False(outcome.Success);
        Assert.Equal("INVALID_PROJECT_ROOT", outcome.ErrorCode);
    }

    [Fact]
    public async Task UseCase_RecusaPastaDoWindows()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var useCase = NewUseCase();

        var outcome = await useCase.ExecuteAsync(windows);

        Assert.False(outcome.Success);
        Assert.Equal("INVALID_PROJECT_ROOT", outcome.ErrorCode);
    }

    [Fact]
    public async Task UseCase_SemAutoCreate_PastaExistenteSemQuatroPastas_DevolveInvalidFourFolders()
    {
        // Código devolvido para o frontend pedir confirmação: pasta existe, faltam pastas canônicas, autoCreateFolders=false.
        string dir = Path.Combine(Path.GetTempPath(), "tc_policy_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var outcome = await NewUseCase().ExecuteAsync(dir, autoCreateFolders: false);

            Assert.False(outcome.Success);
            Assert.Equal("INVALID_FOUR_FOLDERS", outcome.ErrorCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task UseCase_SemAutoCreate_PastaAusente_DevolveDirectoryNotFound()
    {
        string missing = Path.Combine(Path.GetTempPath(), "tc_policy_missing_" + Guid.NewGuid().ToString("N"));

        var outcome = await NewUseCase().ExecuteAsync(missing, autoCreateFolders: false);

        Assert.False(outcome.Success);
        Assert.Equal("DIRECTORY_NOT_FOUND", outcome.ErrorCode);
        Assert.False(Directory.Exists(missing));
    }

    private static OpenOrCreateProjectUseCase NewUseCase()
        => new(new MemoryProjectRepository(), new MemoryMediaRepository(), new MemoryPlanRepository(),
            NullLogger<OpenOrCreateProjectUseCase>.Instance);

    private sealed class MemoryProjectRepository : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<Project?>(null);
        public Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default) => Task.FromResult<Project?>(null);
        public Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<Project>());
        public Task SaveAsync(Project project, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MemoryMediaRepository : IMediaRepository
    {
        public Task<MediaAsset?> GetByIdAsync(Guid mediaId, CancellationToken ct = default) => Task.FromResult<MediaAsset?>(null);
        public Task<List<MediaAsset>> ListByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(new List<MediaAsset>());
        public Task SaveAsync(MediaAsset asset, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveBatchAsync(IEnumerable<MediaAsset> assets, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MemoryPlanRepository : IEditPlanRepository
    {
        public Task<EditPlan?> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<EditPlan?>(null);
        public Task SavePlanAsync(EditPlan plan, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<EditSuggestion>> ListSuggestionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(new List<EditSuggestion>());
        public Task SaveSuggestionsBatchAsync(IEnumerable<EditSuggestion> suggestions, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default) => Task.CompletedTask;
        public Task<EditDecision?> SaveDecisionIfAsync(Guid suggestionId, Func<IReadOnlyList<EditDecision>, EditDecision?> build, CancellationToken ct = default) =>
            Task.FromResult(build(new List<EditDecision>()));
        public Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(new List<EditDecision>());
        public Task<EditDecision?> GetLatestDecisionBySuggestionAsync(Guid suggestionId, CancellationToken ct = default) => Task.FromResult<EditDecision?>(null);
        public Task<List<Guid>> FindExistingSuggestionIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default) => Task.FromResult(new List<Guid>());
        public Task<List<EditDecision>> ListDecisionsBySuggestionAsync(Guid suggestionId, CancellationToken ct = default) => Task.FromResult(new List<EditDecision>());
        public Task SaveDecisionsAtomicAsync(IReadOnlyList<EditDecision> decisions, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<EditSuggestion>> ListSuggestionsByIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default) => Task.FromResult(new List<EditSuggestion>());
    }
}
