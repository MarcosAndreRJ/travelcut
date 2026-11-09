using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.PostAnalysis;

/// <summary>
/// Processamento automático após a análise (F14.8/I2b, P01). Singleton; escopo de DI próprio por execução.
/// Etapas independentes (falha numa não impede a outra): 1) plano via PlanningUseCases.GenerateAsync (perfil persistido →
/// política aplica cortes seguros); 2) proxies para vídeos que o Chromium não toca (PreviewUseCases.StartProxyAsync,
/// que já deduplica job ativo e proxy existente). Originais nunca são tocados. Idempotente: o plano é idempotente
/// por fingerprint e proxies prontos/em andamento não são refeitos.
/// Estado por projeto em memória (efêmero como o job de pipeline); exposto em pipeline.status.postAnalysis.
/// </summary>
public sealed class PostAnalysisOrchestrator : IPostAnalysisOrchestrator, IDisposable
{
    public const string ReasonAutoProcessDisabled = "auto_process_disabled";
    public const string ReasonNoAnalysisResults = "no_analysis_results";

    public const string IssuePlanFailed = "PLAN_FAILED";
    public const string IssueProxyFailed = "PROXY_FAILED";
    public const string IssueProxyUnavailable = "PROXY_FFMPEG_UNAVAILABLE";
    public const string IssueProxyLimit = "PROXY_LIMIT_EXCEEDED";
    public const string IssueUnexpected = "POST_ANALYSIS_ERROR";
    public const string IssueZoomFailed = "ZOOM_SUGGESTIONS_FAILED";
    public const string IssueTransitionFailed = "TRANSITION_SUGGESTIONS_FAILED";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<PostAnalysisOrchestrator> _logger;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, PostAnalysisStatusDto> _status = new();
    private readonly Dictionary<Guid, Task> _loops = new();
    private readonly HashSet<Guid> _rerun = new();
    private readonly CancellationTokenSource _shutdown = new();

    public PostAnalysisOrchestrator(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<PostAnalysisOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    public Task ScheduleAsync(Guid projectId, Guid jobId)
    {
        if (projectId == Guid.Empty) return Task.CompletedTask;

        lock (_gate)
        {
            if (_loops.TryGetValue(projectId, out var active))
            {
                // Já em execução: uma nova rodada depois (entradas podem ter mudado); nunca em paralelo.
                _rerun.Add(projectId);
                return active;
            }

            _status[projectId] = Empty("running", "plan", _clock.GetUtcNow().UtcDateTime);
            var loop = Task.Run(() => LoopAsync(projectId));
            _loops[projectId] = loop;
            return loop;
        }
    }

    public PostAnalysisStatusDto GetStatus(Guid projectId)
    {
        lock (_gate)
        {
            return _status.TryGetValue(projectId, out var s) ? s : Empty("idle", null, null);
        }
    }

    public void Dispose()
    {
        try { _shutdown.Cancel(); } catch (ObjectDisposedException) { }
        _shutdown.Dispose();
    }

    private async Task LoopAsync(Guid projectId)
    {
        try
        {
            while (true)
            {
                await ExecuteAsync(projectId, _shutdown.Token);

                lock (_gate)
                {
                    if (!_rerun.Remove(projectId))
                    {
                        _loops.Remove(projectId);
                        return;
                    }
                    _status[projectId] = Empty("running", "plan", _clock.GetUtcNow().UtcDateTime);
                }
            }
        }
        catch (Exception ex)
        {
            // ExecuteAsync não lança; defesa para nunca deixar o estado preso em running.
            _logger.LogError(ex, "Falha inesperada no laço pós-análise do projeto {ProjectId}", projectId);
            lock (_gate)
            {
                _rerun.Remove(projectId);
                _loops.Remove(projectId);
            }
        }
    }

    private void Set(Guid projectId, PostAnalysisStatusDto status)
    {
        lock (_gate) { _status[projectId] = status; }
    }

    private async Task ExecuteAsync(Guid projectId, CancellationToken ct)
    {
        var startedAt = GetStatus(projectId).StartedAtUtc ?? _clock.GetUtcNow().UtcDateTime;
        var issues = new List<string>();
        int autoCuts = 0, pending = 0, queued = 0, failures = 0;
        int? revision = null;
        bool planOk = false, planFailed = false, proxyOk = false;

        PostAnalysisStatusDto Running(string stage) => new("running", stage, null, autoCuts, pending, queued, failures,
            new List<string>(issues), revision, startedAt, null);

        PostAnalysisStatusDto Final(string state, string? reason = null) => new(state, null, reason, autoCuts, pending, queued,
            failures, new List<string>(issues), revision, startedAt, _clock.GetUtcNow().UtcDateTime);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var project = await sp.GetRequiredService<IProjectRepository>().GetByIdAsync(projectId, ct);
            if (project is null)
            {
                Set(projectId, Final("skipped", "project_not_found"));
                return;
            }
            if (project.AutoProcess == false)
            {
                Set(projectId, Final("skipped", ReasonAutoProcessDisabled));
                _logger.LogInformation("Projeto {ProjectId}: processamento pós-análise desligado (AutoProcess=false)", projectId);
                return;
            }

            // ---- Etapa 1: plano
            Set(projectId, Running("plan"));
            bool hasInputs = await HasAnalysisResultsAsync(sp, projectId, ct);
            if (hasInputs)
            {
                // E04b: sugestões de zoom pelo recomendador único (idempotente), ANTES do plano para que ele as considere.
                // Falha aqui não impede o plano (cortes seguem valendo).
                try
                {
                    var zooms = sp.GetService<Editorial.IZoomSuggestionGenerator>();
                    if (zooms is not null) await zooms.GenerateAsync(projectId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    issues.Add(IssueZoomFailed);
                    _logger.LogWarning(ex, "Projeto {ProjectId}: falha ao gerar sugestões de zoom (plano segue)", projectId);
                }

                try
                {
                    var outcome = await sp.GetRequiredService<PlanningUseCases>()
                        .GenerateAsync(new GeneratePlanRequest(projectId, null, null), ct);
                    if (outcome.Success && outcome.Value is { } plan)
                    {
                        planOk = true;
                        revision = plan.Revision;
                        autoCuts = plan.Summary.AutoCutsApplied;
                        pending = plan.Summary.PendingSuggestions;

                        // E05: sugestões de transição sobre as bordas reais dos clipes do plano (como o zoom). Se surgirem sugestões
                        // novas, o plano é regerado para considerá-las (idempotente por fingerprint). Falha aqui não invalida o plano.
                        try
                        {
                            var transitions = sp.GetService<Editorial.ITransitionSuggestionGenerator>();
                            if (transitions is not null && await transitions.GenerateAsync(projectId, plan, ct) > 0)
                            {
                                var again = await sp.GetRequiredService<PlanningUseCases>()
                                    .GenerateAsync(new GeneratePlanRequest(projectId, null, null), ct);
                                if (again.Success && again.Value is { } next)
                                {
                                    revision = next.Revision;
                                    autoCuts = next.Summary.AutoCutsApplied;
                                    pending = next.Summary.PendingSuggestions;
                                }
                            }
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception ex)
                        {
                            issues.Add(IssueTransitionFailed);
                            _logger.LogWarning(ex, "Projeto {ProjectId}: falha ao gerar sugestões de transição (plano segue)", projectId);
                        }
                    }
                    else
                    {
                        planFailed = true;
                        failures++;
                        issues.Add($"{IssuePlanFailed}:{outcome.ErrorCode ?? "UNKNOWN"}");
                        _logger.LogWarning("Projeto {ProjectId}: geração automática do plano falhou ({Code})", projectId, outcome.ErrorCode);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    planFailed = true;
                    failures++;
                    issues.Add($"{IssuePlanFailed}:EXCEPTION");
                    _logger.LogError(ex, "Projeto {ProjectId}: exceção na geração automática do plano", projectId);
                }
            }

            // ---- Etapa 2: proxies (independente do plano)
            Set(projectId, Running("proxy"));
            bool proxyAttempted = false;
            try
            {
                var preview = sp.GetService<PreviewUseCases>();
                if (preview is not null)
                {
                    var needing = await FindMediaNeedingProxyAsync(sp, preview, projectId, issues, ct);
                    if (needing.Count > 0)
                    {
                        proxyAttempted = true;
                        if (needing.Count > PreviewPaths.MaxProxyMediaPerRequest)
                        {
                            issues.Add($"{IssueProxyLimit}:{needing.Count - PreviewPaths.MaxProxyMediaPerRequest}");
                            needing = needing.Take(PreviewPaths.MaxProxyMediaPerRequest).ToList();
                        }

                        var start = await preview.StartProxyAsync(new StartProxyRequest(projectId, needing), ct);
                        if (start.Ok && start.Value is { } job)
                        {
                            proxyOk = true;
                            queued = job.Items.Count(i => i.State is "pending" or "running");
                            int bad = job.Items.Count(i => i.State is "failed" or "skipped");
                            if (bad > 0)
                            {
                                failures += bad;
                                issues.Add(job.State == "ffmpeg_unavailable" ? IssueProxyUnavailable : $"{IssueProxyFailed}:{bad}");
                            }
                        }
                        else if (start.ErrorCode == "PROXY_IN_PROGRESS")
                        {
                            proxyOk = true; // já há job ativo no projeto: nada duplicado, nada a reportar como erro
                        }
                        else
                        {
                            failures++;
                            issues.Add($"{IssueProxyFailed}:{start.ErrorCode ?? "UNKNOWN"}");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                proxyAttempted = true;
                failures++;
                issues.Add($"{IssueProxyFailed}:EXCEPTION");
                _logger.LogError(ex, "Projeto {ProjectId}: exceção ao enfileirar proxies", projectId);
            }

            bool anyWork = planOk || planFailed || proxyAttempted || issues.Count > 0;
            if (!anyWork)
            {
                Set(projectId, Final("skipped", ReasonNoAnalysisResults));
                return;
            }

            bool anySuccess = planOk || proxyOk;
            string state = !anySuccess ? "failed" : issues.Count > 0 || failures > 0 ? "completed_with_issues" : "completed";
            Set(projectId, Final(state));
            _logger.LogInformation(
                "Projeto {ProjectId}: pós-análise {State} (cortes auto {Auto}, pendentes {Pending}, proxies {Queued}, falhas {Failures})",
                projectId, state, autoCuts, pending, queued, failures);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Set(projectId, Final("failed"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Projeto {ProjectId}: falha inesperada no pós-análise", projectId);
            issues.Add(IssueUnexpected);
            failures++;
            Set(projectId, Final("failed"));
        }
    }

    /// <summary>Há ao menos uma sugestão ou uma transcrição com palavras (entrada válida para o planner).</summary>
    private static async Task<bool> HasAnalysisResultsAsync(IServiceProvider sp, Guid projectId, CancellationToken ct)
    {
        var suggestions = await sp.GetRequiredService<IEditPlanRepository>().ListSuggestionsByProjectAsync(projectId, ct);
        if (suggestions.Count > 0) return true;

        var transcripts = sp.GetService<ITranscriptRepository>();
        if (transcripts is null) return false;
        foreach (var media in await sp.GetRequiredService<IMediaRepository>().ListByProjectAsync(projectId, ct))
        {
            var t = await transcripts.GetByMediaIdAsync(media.MediaId, ct);
            if (t is { Words.Count: > 0 }) return true;
        }
        return false;
    }

    /// <summary>Vídeos tocados pelo original que o player não garante reproduzir e sem proxy/job (proxyStatus none).</summary>
    private static async Task<List<Guid>> FindMediaNeedingProxyAsync(
        IServiceProvider sp, PreviewUseCases preview, Guid projectId, List<string> issues, CancellationToken ct)
    {
        var result = new List<Guid>();
        bool ffmpegMissing = false;
        foreach (var media in await sp.GetRequiredService<IMediaRepository>().ListByProjectAsync(projectId, ct))
        {
            if (media.Kind != MediaKind.Video) continue;

            var source = await preview.GetPlaybackSourceAsync(new PlaybackSourceRequest(projectId, media.MediaId), ct);
            if (!source.Ok || source.Value is not { } s) continue;
            if (s.Status != PreviewPaths.PlaybackStatusOriginal || s.LikelyPlayable == true) continue;

            if (s.ProxyStatus == PreviewPaths.ProxyStatusNone) result.Add(media.MediaId);
            else if (s.ProxyStatus == PreviewPaths.ProxyStatusFfmpegUnavailable) ffmpegMissing = true;
        }

        if (ffmpegMissing && result.Count == 0) issues.Add(IssueProxyUnavailable);
        return result;
    }

    private static PostAnalysisStatusDto Empty(string state, string? stage, DateTime? startedAt) =>
        new(state, stage, null, 0, 0, 0, 0, new List<string>(), null, startedAt, null);
}
