using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Infrastructure.Import;

/// <summary>Opções do serviço. <see cref="CopyChunkHook"/> é um gancho de teste (nulo em produção).</summary>
public sealed class MediaImportOptions
{
    public int BufferSize { get; init; } = 1024 * 1024;

    /// <summary>Chamado após cada bloco gravado, com o total já copiado do arquivo atual.</summary>
    public Action<long>? CopyChunkHook { get; init; }
}

/// <summary>
/// Importação de mídia (Tela 07). Regras de integridade:
/// a origem nunca é alterada; a escrita fica restrita a 1-Bruto/2-Assets (subpasta validada);
/// cópia em ".tcpart" com SHA-256 verificado antes do rename final; nunca sobrescreve arquivo existente.
/// </summary>
public sealed class MediaImportService : IMediaImportService
{
    public const string PartialFileExtension = MediaFileExpander.PartialFileExtension;
    private const string SpeechUnavailableNote =
        "Análise de fala (F05) ainda não está conectada nesta versão: nada foi enfileirado.";
    private const int MaxNameAttempts = 10000;

    /// <summary>Estados de <c>speechAnalysis</c> (contrato com a UI).</summary>
    private static class SpeechStates
    {
        public const string Queued = "queued";
        public const string Unavailable = "unavailable";
    }

    private static string DescribeWhisperUnavailable(AnalysisEnvironmentDto env)
    {
        if (!string.IsNullOrWhiteSpace(env.WhisperUnavailableReason)) return env.WhisperUnavailableReason!;
        var reasons = env.Reasons.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        return reasons.Count > 0 ? string.Join("; ", reasons) : "Whisper indisponível neste ambiente.";
    }
    private const long GiB = 1L << 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFreeSpaceProvider _freeSpace;
    private readonly ILogger<MediaImportService> _logger;
    private readonly MediaImportOptions _options;
    private readonly IPipelineJobManager? _pipelineJobs;
    private readonly ConcurrentDictionary<Guid, ImportJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Guid> _activeJobByProject = new();

    public MediaImportService(
        IServiceScopeFactory scopeFactory,
        IFreeSpaceProvider freeSpace,
        ILogger<MediaImportService> logger,
        MediaImportOptions? options = null,
        IPipelineJobManager? pipelineJobs = null)
    {
        _scopeFactory = scopeFactory;
        _freeSpace = freeSpace;
        _logger = logger;
        _options = options ?? new MediaImportOptions();
        _pipelineJobs = pipelineJobs;
    }

    public MediaImportService(
        IServiceScopeFactory scopeFactory,
        IFreeSpaceProvider freeSpace,
        ILogger<MediaImportService> logger,
        IPipelineJobManager? pipelineJobs)
        : this(scopeFactory, freeSpace, logger, null, pipelineJobs)
    {
    }

    private IPipelineJobManager? ResolvePipelineJobManager()
    {
        if (_pipelineJobs != null) return _pipelineJobs;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IPipelineJobManager>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível resolver IPipelineJobManager a partir do IServiceScopeFactory.");
            return null;
        }
    }

    public async Task<ImportStartResult> StartAsync(StartImportRequest request, CancellationToken ct = default)
    {
        if (!ImportPathRules.TryResolveDestination(request.Destination, out var destinationFolder))
            return Fail("INVALID_DESTINATION", "Destino inválido: use 1-Bruto ou 2-Assets.");

        if (!ImportPathRules.TryNormalizeSubfolder(request.Subfolder, out var subfolder))
            return Fail("INVALID_SUBFOLDER", "Subpasta inválida: use um caminho relativo ao destino, sem '..', unidades ou caracteres inválidos.");

        if (request.Paths == null || request.Paths.All(string.IsNullOrWhiteSpace))
            return Fail("NO_FILES", "Nenhum arquivo ou pasta informado.");

        using var scope = _scopeFactory.CreateScope();
        var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var project = await projectRepo.GetByIdAsync(request.ProjectId, ct);
        if (project == null)
            return Fail("PROJECT_NOT_FOUND", $"Projeto com ID {request.ProjectId} não foi encontrado.");

        if (string.IsNullOrWhiteSpace(project.RootPath) || !Directory.Exists(project.RootPath))
            return Fail("ROOT_NOT_FOUND", "Pasta raiz do projeto não existe.");

        string projectRoot = Path.GetFullPath(project.RootPath);
        string destinationRoot = Path.Combine(projectRoot, destinationFolder);
        string destinationDirectory = Path.GetFullPath(Path.Combine(destinationRoot, subfolder));
        if (!IsSameOrInside(destinationRoot, destinationDirectory))
            return Fail("INVALID_SUBFOLDER", "Subpasta inválida: o destino final sairia da pasta de destino.");

        var jobId = Guid.NewGuid();
        var job = new ImportJob(
            jobId,
            project.ProjectId,
            projectRoot,
            destinationRoot,
            destinationDirectory,
            destinationFolder,
            subfolder,
            sourcePaths: request.Paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
            tags: ImportPathRules.NormalizeTags(request.Tags),
            copyFiles: request.CopyFiles,
            generateThumbnails: request.GenerateThumbnails,
            analyzeMetadata: request.AnalyzeMetadata,
            detectSpeech: request.DetectSpeech);

        if (!_activeJobByProject.TryAdd(project.ProjectId, jobId))
            return Fail("IMPORT_IN_PROGRESS", "Já existe uma importação em andamento para este projeto.");

        _jobs[jobId] = job;
        if (request.DetectSpeech)
        {
            // Nunca bloqueia o chamador (IPC): sem probe aqui. Só o cache do ambiente (se válido) antecipa um "indisponível";
            // a decisão real (probe, enfileirar ou mesclar) acontece dentro do job, em segundo plano.
            var pipeline = ResolvePipelineJobManager();
            if (pipeline == null)
            {
                job.Mutate(j =>
                {
                    j.SpeechAnalysis = SpeechStates.Unavailable;
                    j.AddNote("Detecção de fala indisponível: pipeline não está registrado neste host (serviço de análise audiovisual não configurado); nada foi enfileirado.");
                });
            }
            else if (pipeline.PeekEnvironment() is { WhisperAvailable: false } cachedEnv)
            {
                job.Mutate(j =>
                {
                    j.SpeechAnalysis = SpeechStates.Unavailable;
                    j.AddNote($"Detecção de fala indisponível ({DescribeWhisperUnavailable(cachedEnv)}): nada foi enfileirado.");
                });
            }
            else
            {
                job.Mutate(j =>
                {
                    j.SpeechAnalysis = SpeechStates.Queued;
                    j.AddNote("Detecção de fala agendada para após a importação (dependências verificadas em segundo plano).");
                });
            }
        }

        _logger.LogInformation("Importação {JobId} enfileirada para o projeto {ProjectId} (destino {Folder}, cópia={Copy})",
            jobId, project.ProjectId, destinationFolder, request.CopyFiles);

        // Trabalho pesado fora do chamador (IPC): expansão, cópia e registro acontecem no job.
        _ = Task.Run(() => RunJobAsync(job));

        return new ImportStartResult(true, jobId, null, null);
    }

    public ImportJobStatusDto? GetStatus(Guid jobId)
        => _jobs.TryGetValue(jobId, out var job) ? job.Snapshot() : null;

    public CancelImportResponse Cancel(Guid jobId)
        => new(_jobs.TryGetValue(jobId, out var job) && job.RequestCancel());

    private async Task RunJobAsync(ImportJob job)
    {
        try
        {
            await ExecuteJobAsync(job);
        }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested)
        {
            FinishJob(job, ImportJobStates.Canceled, "Importação cancelada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Importação {JobId} interrompida por erro inesperado", job.JobId);
            FinishJob(job, ImportJobStates.Failed, "Erro interno durante a importação. Consulte os logs.");
        }
    }

    private async Task ExecuteJobAsync(ImportJob job)
    {
        var ct = job.Token;
        job.Mutate(j =>
        {
            j.State = ImportJobStates.Running;
            j.Stage = "preflight";
        });

        // 1. Pré-verificação: expansão, limites, espaço livre.
        var expansion = await Task.Run(() => MediaFileExpander.Expand(job.SourcePaths, ImportPathRules.MaxFilesPerImport, ct), ct);

        if (expansion.Errors.Count > 0)
        {
            string ignored = string.Join("; ", expansion.Errors.Take(5));
            job.Mutate(j => j.AddNote($"{expansion.Errors.Count} caminho(s) ignorado(s): {ignored}"));
        }

        if (expansion.Truncated)
        {
            FinishJob(job, ImportJobStates.Failed,
                $"Limite de {ImportPathRules.MaxFilesPerImport} arquivos por importação excedido. Selecione menos arquivos ou pastas.");
            return;
        }

        if (expansion.Files.Count == 0)
        {
            FinishJob(job, ImportJobStates.Failed, "Nenhum arquivo de mídia suportado foi encontrado nos caminhos selecionados.");
            return;
        }

        var items = expansion.Files.Select(f =>
        {
            bool inPlace = ImportPathRules.IsInsideDirectory(job.DestinationDirectory, f.FullPath);
            var item = new ImportItemState
            {
                SourcePath = f.FullPath,
                FileName = f.FileName,
                ByteLength = f.ByteLength,
                Kind = f.Kind,
                InPlace = inPlace,
                NeedsCopy = !inPlace && job.CopyFiles
            };

            if (!inPlace && !job.CopyFiles)
            {
                item.Result = ImportItemResults.Failed;
                item.Message = "Cópia desativada e o arquivo está fora da pasta de destino. Ative a cópia ou selecione arquivos já dentro do destino.";
            }

            return item;
        }).ToList();

        job.Mutate(j => j.Items = items);

        long bytesToCopy = items.Where(i => i.NeedsCopy).Sum(i => i.ByteLength);
        if (bytesToCopy > 0)
        {
            string probe = Directory.Exists(job.DestinationRoot) ? job.DestinationRoot : job.ProjectRoot;
            long free = _freeSpace.GetAvailableFreeSpace(probe);
            long required = bytesToCopy + ImportPathRules.SafetyMarginBytes;
            if (free < required)
            {
                FinishJob(job, ImportJobStates.Failed,
                    $"Espaço insuficiente em {job.DestinationFolder}: necessário {FormatGiB(required)} (cópia de {FormatGiB(bytesToCopy)} + margem de 1 GiB), disponível {FormatGiB(free)}.");
                return;
            }
        }

        job.Mutate(j =>
        {
            j.BytesTotal = bytesToCopy;
            j.Stage = "copy";
        });

        // 2. Cópia item a item. Falha de um item não interrompe os demais; cancelamento sim.
        bool canceled = false;
        foreach (var item in items)
        {
            if (item.Result != ImportItemResults.Pending)
                continue;

            if (ct.IsCancellationRequested)
                canceled = true;

            if (canceled)
            {
                SetItem(job, item, ImportItemResults.Canceled, "Importação cancelada antes deste arquivo.");
                continue;
            }

            if (item.InPlace)
            {
                job.Mutate(_ =>
                {
                    item.Result = ImportItemResults.RegisteredInPlace;
                    item.FinalFullPath = item.SourcePath;
                    item.DestinationRelativePath = ImportPathRules.ToLibraryRelativePath(job.ProjectRoot, item.SourcePath);
                });
                continue;
            }

            try
            {
                await ImportFileAsync(job, item, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                canceled = true;
                SetItem(job, item, ImportItemResults.Canceled, "Cancelado durante a cópia; arquivo temporário removido.");
            }
            catch (Exception ex)
            {
                // Log com detalhe técnico (sem caminho completo); a UI recebe mensagem curta por tipo de erro.
                _logger.LogWarning(ex, "Falha ao importar {File}", item.FileName);
                SetItem(job, item, ImportItemResults.Failed, ImportFailureMessages.Describe(ex));
            }
        }

        // 3. Registro na biblioteca, tags e miniaturas. Roda mesmo após cancelamento: o que já foi copiado fica registrado.
        var registrable = items
            .Where(i => i.FinalFullPath != null && i.Result is ImportItemResults.Imported or ImportItemResults.Renamed
                or ImportItemResults.RegisteredInPlace or ImportItemResults.SkippedDuplicate)
            .ToList();

        bool registrationOk = true;
        if (registrable.Count > 0)
        {
            registrationOk = await RegisterAndDecorateAsync(job, registrable, ct, () => canceled = true);
        }

        // 4. Estado final.
        bool wasCanceled = canceled || ct.IsCancellationRequested;
        int failedCount = items.Count(i => i.Result == ImportItemResults.Failed);
        int canceledCount = items.Count(i => i.Result == ImportItemResults.Canceled);
        string summary =
            $"{items.Count(i => i.Result == ImportItemResults.Imported)} importado(s), " +
            $"{items.Count(i => i.Result == ImportItemResults.Renamed)} renomeado(s), " +
            $"{items.Count(i => i.Result == ImportItemResults.SkippedDuplicate)} duplicado(s) já existente(s), " +
            $"{items.Count(i => i.Result == ImportItemResults.RegisteredInPlace)} já na pasta de destino, " +
            $"{failedCount} com falha, {canceledCount} cancelado(s).";

        string state = wasCanceled
            ? ImportJobStates.Canceled
            : (failedCount > 0 || !registrationOk ? ImportJobStates.CompletedWithErrors : ImportJobStates.Completed);

        FinishJob(job, state, summary);
    }

    /// <summary>Registra os arquivos na biblioteca, aplica tags e gera miniaturas. Retorna false se o registro falhou.</summary>
    private async Task<bool> RegisterAndDecorateAsync(
        ImportJob job,
        List<ImportItemState> registrable,
        CancellationToken ct,
        Action markCanceled)
    {
        job.Mutate(j =>
        {
            j.Stage = job.AnalyzeMetadata ? "metadata" : "register";
            j.CurrentFile = null;
        });

        using var scope = _scopeFactory.CreateScope();
        var scan = scope.ServiceProvider.GetRequiredService<ScanMediaLibraryUseCase>();
        var paths = registrable.Select(i => i.FinalFullPath!).ToList();

        var (ok, _, code, message) = await scan.RegisterFilesAsync(job.ProjectId, paths, job.AnalyzeMetadata, CancellationToken.None);
        if (!ok)
        {
            _logger.LogWarning("Registro de importação {JobId} falhou: {Code}", job.JobId, code);
            job.Mutate(j => j.AddNote($"Falha ao registrar na biblioteca ({code}): {message}"));
            return false;
        }

        var mediaRepo = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        var assets = await mediaRepo.ListByProjectAsync(job.ProjectId, CancellationToken.None);
        var byRelative = new Dictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            byRelative.TryAdd(asset.RelativePath, asset);
        }

        var registeredAssets = registrable
            .Select(i => byRelative.GetValueOrDefault(ImportPathRules.ToLibraryRelativePath(job.ProjectRoot, i.FinalFullPath!)))
            .Where(a => a != null)
            .Select(a => a!)
            .ToList();

        if (job.Tags.Count > 0)
        {
            var changed = new List<MediaAsset>();
            foreach (var asset in registeredAssets)
            {
                var merged = ImportPathRules.NormalizeTags(asset.Tags.Concat(job.Tags)).ToList();
                if (!merged.SequenceEqual(asset.Tags))
                {
                    asset.Tags = merged;
                    changed.Add(asset);
                }
            }

            if (changed.Count > 0)
            {
                await mediaRepo.SaveBatchAsync(changed, CancellationToken.None);
            }
        }

        if (job.GenerateThumbnails)
        {
            await GenerateThumbnailsAsync(job, registrable, byRelative, scope, ct, markCanceled);
        }

        if (job.DetectSpeech && !ct.IsCancellationRequested)
        {
            await EnqueueSpeechAnalysisIfRequestedAsync(job, registeredAssets, ct);
        }

        return true;
    }

    private async Task EnqueueSpeechAnalysisIfRequestedAsync(
        ImportJob job,
        IReadOnlyList<MediaAsset> registeredAssets,
        CancellationToken ct)
    {
        if (!job.DetectSpeech || job.SpeechAnalysis != "queued" || ct.IsCancellationRequested)
        {
            return;
        }

        var mediaIds = registeredAssets
            .Where(a => a.Kind is MediaKind.Video or MediaKind.Audio)
            .Select(a => a.MediaId)
            .ToList();

        if (mediaIds.Count == 0)
        {
            job.Mutate(j =>
            {
                j.SpeechAnalysis = "unavailable";
                j.AddNote("Detecção de fala: nenhum arquivo de áudio ou vídeo elegível importado.");
            });
            return;
        }

        var pipeline = ResolvePipelineJobManager();
        if (pipeline == null)
        {
            job.Mutate(j =>
            {
                j.SpeechAnalysis = "unavailable";
                j.AddNote("Detecção de fala: gerenciador de pipeline de análise indisponível.");
            });
            return;
        }

        // Decisão da fala em segundo plano: o probe de ambiente pode demorar (até o timeout do worker) sem travar o IPC.
        AnalysisEnvironmentDto? env = null;
        try
        {
            env = await pipeline.GetEnvironmentAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao verificar ambiente de análise de fala para o job {JobId}", job.JobId);
            job.Mutate(j =>
            {
                j.SpeechAnalysis = "unavailable";
                j.AddNote("Falha ao consultar dependências de fala: análise indisponível; nada foi enfileirado.");
            });
            return;
        }

        if (env is { WhisperAvailable: false })
        {
            job.Mutate(j =>
            {
                j.SpeechAnalysis = "unavailable";
                j.AddNote($"Detecção de fala indisponível ({DescribeWhisperUnavailable(env)}): nada foi enfileirado.");
            });
            return;
        }

        if (env is { CpuFallback: true })
        {
            job.Mutate(j => j.AddNote("Aviso: sem CUDA, a transcrição Whisper roda em CPU e será muito lenta."));
        }

        try
        {
            var req = new StartPipelineRequest(
                ProjectId: job.ProjectId,
                MediaIds: mediaIds,
                Stages: null,
                Language: null,
                Force: false);

            var pipelineJobId = await pipeline.StartAsync(req, ct);
            job.Mutate(j =>
            {
                j.SpeechAnalysis = "queued";
                j.AddNote($"Detecção de fala enfileirada no pipeline audiovisual ({pipelineJobId}) para {mediaIds.Count} arquivo(s).");
            });
            _logger.LogInformation(
                "Job {JobId}: detecção de fala enfileirada com sucesso no pipeline ({PipelineJobId}) para {Count} mídia(s)",
                job.JobId, pipelineJobId, mediaIds.Count);
        }
        catch (InvalidOperationException ex) when (ex.Message == "PIPELINE_IN_PROGRESS")
        {
            job.Mutate(j => j.AddNote("Detecção de fala agendada: outro processamento já está em andamento no projeto."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enfileirar análise de fala no pipeline para o job de importação {JobId}", job.JobId);
            job.Mutate(j => j.AddNote("Falha ao registrar análise de fala no pipeline."));
        }
    }

    private async Task GenerateThumbnailsAsync(
        ImportJob job,
        List<ImportItemState> registrable,
        Dictionary<string, MediaAsset> byRelative,
        IServiceScope scope,
        CancellationToken ct,
        Action markCanceled)
    {
        job.Mutate(j => j.Stage = "thumbnails");
        var thumbnails = scope.ServiceProvider.GetRequiredService<IThumbnailService>();

        int unavailable = 0;
        int failed = 0;
        string? unavailableReason = null;

        foreach (var item in registrable)
        {
            if (ct.IsCancellationRequested)
            {
                markCanceled();
                break;
            }

            if (item.Kind is not (MediaKind.Video or MediaKind.Image))
                continue;

            string relative = ImportPathRules.ToLibraryRelativePath(job.ProjectRoot, item.FinalFullPath!);
            byRelative.TryGetValue(relative, out var asset);
            var meta = asset?.Metadata;
            double at = item.Kind == MediaKind.Image
                ? 0.0
                : (meta is { MetadataStatus: MediaMetadataStatus.Ok, DurationSeconds: > 0 } ? meta.DurationSeconds * 0.10 : 1.0);

            try
            {
                var result = await thumbnails.GenerateAsync(job.ProjectRoot, item.FinalFullPath!, at, ct);
                switch (result.Status)
                {
                    case ThumbnailStatus.Ok:
                        break;
                    case ThumbnailStatus.FfmpegUnavailable:
                        unavailable++;
                        unavailableReason ??= result.Reason;
                        break;
                    default:
                        failed++;
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                markCanceled();
                break;
            }
            catch (Exception ex)
            {
                // Falha de miniatura não falha o item importado.
                _logger.LogWarning(ex, "Falha ao gerar miniatura de {File}", item.FileName);
                failed++;
            }
        }

        if (unavailable > 0)
        {
            job.Mutate(j => j.AddNote($"Miniaturas não geradas: FFmpeg indisponível. {unavailableReason}"));
        }

        if (failed > 0)
        {
            job.Mutate(j => j.AddNote($"{failed} miniatura(s) não puderam ser geradas."));
        }
    }

    /// <summary>Copia um item com nome livre (ou detecta duplicata). Atualiza o resultado do item.</summary>
    private async Task ImportFileAsync(ImportJob job, ImportItemState item, CancellationToken ct)
    {
        var source = new FileInfo(item.SourcePath);
        if (!source.Exists)
        {
            SetItem(job, item, ImportItemResults.Failed, "Arquivo de origem não encontrado.");
            return;
        }

        job.Mutate(j =>
        {
            j.Stage = "copy";
            j.CurrentFile = item.FileName;
        });
        SetItem(job, item, ImportItemResults.Copying);

        Directory.CreateDirectory(job.DestinationDirectory);

        // Conflito de nome: duplicata idêntica → skipped_duplicate; senão "nome (n).ext". Nunca sobrescreve.
        string? sourceHash = null;
        string? chosenName = null;
        string? chosenPath = null;
        string? duplicatePath = null;

        for (int n = 0; n < MaxNameAttempts; n++)
        {
            ct.ThrowIfCancellationRequested();

            string name = n == 0 ? item.FileName : WithSuffix(item.FileName, n);
            string candidate = Path.Combine(job.DestinationDirectory, name);

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                chosenName = name;
                chosenPath = candidate;
                break;
            }

            if (File.Exists(candidate) && new FileInfo(candidate).Length == source.Length)
            {
                sourceHash ??= await HashFileAsync(item.SourcePath, ct);
                string existingHash = await HashFileAsync(candidate, ct);
                if (string.Equals(existingHash, sourceHash, StringComparison.Ordinal))
                {
                    duplicatePath = candidate;
                    break;
                }
            }
        }

        if (duplicatePath != null)
        {
            job.Mutate(_ =>
            {
                item.Result = ImportItemResults.SkippedDuplicate;
                item.FinalFullPath = duplicatePath;
                item.DestinationRelativePath = ImportPathRules.ToLibraryRelativePath(job.ProjectRoot, duplicatePath);
                item.Message = "Já existe no destino um arquivo idêntico (mesmo tamanho e SHA-256).";
            });
            return;
        }

        if (chosenPath == null || chosenName == null)
            throw new IOException("Não foi possível encontrar um nome livre no destino.");

        await CopyAndVerifyAsync(job, source, chosenPath, ct);

        bool renamed = !string.Equals(chosenName, item.FileName, StringComparison.Ordinal);
        job.Mutate(_ =>
        {
            item.Result = renamed ? ImportItemResults.Renamed : ImportItemResults.Imported;
            item.FinalFullPath = chosenPath;
            item.DestinationRelativePath = ImportPathRules.ToLibraryRelativePath(job.ProjectRoot, chosenPath);
            item.Message = renamed ? $"Já existia um arquivo com este nome; salvo como \"{chosenName}\"." : null;
        });
    }

    /// <summary>
    /// Copia para "&lt;final&gt;.tcpart" (CreateNew), calcula SHA-256 dos bytes lidos, faz Flush(true),
    /// verifica o destino relido, copia datas e só então move para o nome final (overwrite: false).
    /// Em qualquer falha, remove apenas o próprio .tcpart.
    /// </summary>
    private async Task CopyAndVerifyAsync(ImportJob job, FileInfo source, string finalPath, CancellationToken ct)
    {
        string partPath = finalPath + PartialFileExtension;
        if (File.Exists(partPath))
            throw new PartialFileConflictException();

        int bufferSize = _options.BufferSize;
        bool partCreated = false;
        string sourceHash = string.Empty;
        FileStream? destination = null;

        try
        {
            await using (var input = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
            {
                destination = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.None);
                partCreated = true;

                using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[bufferSize];
                long total = 0;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    int read = await input.ReadAsync(buffer.AsMemory(), ct);
                    if (read == 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                    incremental.AppendData(buffer, 0, read);
                    total += read;

                    int chunk = read;
                    job.Mutate(j => j.BytesCopied += chunk);
                    _options.CopyChunkHook?.Invoke(total);
                }

                if (total != source.Length)
                    throw new IOException("O arquivo de origem mudou de tamanho durante a cópia.");

                destination.Flush(true);
                sourceHash = Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant();
            }

            destination.Dispose();
            destination = null;

            job.Mutate(j => j.Stage = "verify");
            string destinationHash = await HashFileAsync(partPath, ct);
            if (!string.Equals(destinationHash, sourceHash, StringComparison.Ordinal))
                throw new IOException("Verificação falhou: o SHA-256 do arquivo copiado difere da origem.");

            File.SetCreationTimeUtc(partPath, source.CreationTimeUtc);
            File.SetLastWriteTimeUtc(partPath, source.LastWriteTimeUtc);

            // overwrite: false — nunca substitui arquivo existente.
            File.Move(partPath, finalPath, overwrite: false);
            partCreated = false;
        }
        finally
        {
            destination?.Dispose();
            if (partCreated)
            {
                TryDeletePartial(partPath);
            }
        }
    }

    private void TryDeletePartial(string partPath)
    {
        try
        {
            if (File.Exists(partPath))
                File.Delete(partPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível remover arquivo temporário {Part}", Path.GetFileName(partPath));
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void FinishJob(ImportJob job, string state, string summary)
    {
        // Libera o projeto antes de publicar o estado final: um novo import pode começar assim que o status indicar conclusão.
        _activeJobByProject.TryRemove(new KeyValuePair<Guid, Guid>(job.ProjectId, job.JobId));
        job.Mutate(j => j.Finish(state, summary));
        _logger.LogInformation("Importação {JobId} finalizada com estado {State}", job.JobId, state);
    }

    private static void SetItem(ImportJob job, ImportItemState item, string result, string? message = null)
    {
        job.Mutate(_ =>
        {
            item.Result = result;
            item.Message = message;
        });
    }

    private static string WithSuffix(string fileName, int n)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        return $"{stem} ({n}){ext}";
    }

    private static bool IsSameOrInside(string root, string candidate)
        => string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(candidate), StringComparison.OrdinalIgnoreCase)
           || ImportPathRules.IsInsideDirectory(root, candidate);

    private static string FormatGiB(long bytes)
        => (bytes / (double)GiB).ToString("0.00", CultureInfo.InvariantCulture) + " GiB";

    private static ImportStartResult Fail(string code, string message)
        => new(false, null, code, message);
}
