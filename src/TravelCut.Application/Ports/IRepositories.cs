using TravelCut.Domain.Entities;

namespace TravelCut.Application.Ports;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default);
    Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default);
    Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default);
    Task SaveAsync(Project project, CancellationToken ct = default);
}

public interface IMediaRepository
{
    Task<MediaAsset?> GetByIdAsync(Guid mediaId, CancellationToken ct = default);
    Task<List<MediaAsset>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task SaveAsync(MediaAsset asset, CancellationToken ct = default);
    Task SaveBatchAsync(IEnumerable<MediaAsset> assets, CancellationToken ct = default);
}

public interface ITranscriptRepository
{
    Task<Transcript?> GetByMediaIdAsync(Guid mediaId, CancellationToken ct = default);
    Task SaveAsync(Transcript transcript, CancellationToken ct = default);
}

public interface IEditPlanRepository
{
    Task<EditPlan?> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task SavePlanAsync(EditPlan plan, CancellationToken ct = default);
    Task<List<EditSuggestion>> ListSuggestionsByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task SaveSuggestionsBatchAsync(IEnumerable<EditSuggestion> suggestions, CancellationToken ct = default);

    /// <summary>
    /// Grava uma decisão NOVA (append-only). A implementação atribui <see cref="EditDecision.Version"/> = última versão
    /// da sugestão + 1 dentro de uma transação; o valor informado pelo chamador é substituído.
    /// Decisões nunca são alteradas nem apagadas.
    /// </summary>
    Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default);
    Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Última decisão (maior Version) da sugestão, ou null.</summary>
    Task<EditDecision?> GetLatestDecisionBySuggestionAsync(Guid suggestionId, CancellationToken ct = default);

    /// <summary>Histórico completo de uma sugestão, em ordem de Version (nunca apagado).</summary>
    Task<List<EditDecision>> ListDecisionsBySuggestionAsync(Guid suggestionId, CancellationToken ct = default);

    /// <summary>
    /// Grava várias decisões NOVAS em UMA transação: tudo ou nada. Version é atribuída por sugestão dentro da transação.
    /// Qualquer falha (inclusive de banco) desfaz o lote inteiro e é relançada.
    /// </summary>
    Task SaveDecisionsAtomicAsync(IReadOnlyList<EditDecision> decisions, CancellationToken ct = default);

    /// <summary>
    /// Grava UMA decisão nova somente se <paramref name="build"/> devolver uma decisão, com o histórico da sugestão
    /// lido DENTRO da mesma transação da gravação (revalidação contra corrida). Se build devolver null, nada é gravado
    /// e o método retorna null. Em colisão de Version, a transação é refeita e build é chamado de novo com o histórico atual.
    /// </summary>
    Task<EditDecision?> SaveDecisionIfAsync(
        Guid suggestionId,
        Func<IReadOnlyList<EditDecision>, EditDecision?> build,
        CancellationToken ct = default);

    /// <summary>Sugestões existentes entre os ids informados (ordem não garantida, sem duplicatas).</summary>
    Task<List<EditSuggestion>> ListSuggestionsByIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default);

    /// <summary>Dos ids informados, devolve os que existem como sugestão (ordem não garantida, sem duplicatas).</summary>
    Task<List<Guid>> FindExistingSuggestionIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default);
}
