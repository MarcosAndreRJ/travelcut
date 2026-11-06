using TravelCut.Application.Motion;
using TravelCut.Infrastructure.Motion;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Editor;
using TravelCut.Application.Ipc;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Infrastructure.Audio;
using TravelCut.Infrastructure.Cache;
using TravelCut.Infrastructure.Editor;
using TravelCut.Infrastructure.Import;
using TravelCut.Infrastructure.Jobs;
using TravelCut.Infrastructure.Locks;
using TravelCut.Infrastructure.Media.Thumbnails;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using TravelCut.Infrastructure.Settings;
using TravelCut.Infrastructure.Shell;
using TravelCut.Infrastructure.Tooling;
using TravelCut.ResolveBridge;

namespace TravelCut.Infrastructure.Composition;

/// <summary>
/// Registro do Application Core compartilhado por todos os hosts (Desktop WPF e servidor MCP).
/// Não inclui logging (cada host configura o seu: o MCP envia logs só para stderr), nem itens de WPF/WebView2
/// (janela, diálogos, host virtual de mídia) nem a prévia de mídia do Desktop (<c>AddTravelCutPreview</c>).
/// Como <c>IpcDispatcher</c> recebe <c>PreviewIpcHandler</c> e <c>EditorIpcHandler</c> como parâmetros opcionais,
/// hosts que registram a prévia depois deste método mantêm o mesmo roteamento IPC.
/// Requer <c>ILogger&lt;T&gt;</c> registrado pelo host.
/// </summary>
public static class TravelCutServiceCollectionExtensions
{
    /// <summary>Segundos que o SQLite espera por um bloqueio de outro processo antes de falhar.</summary>
    public const int SqliteBusyTimeoutSeconds = 15;

    public static IServiceCollection AddTravelCutCore(this IServiceCollection services, TravelCutHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AppBaseDirectory);

        // SQLite DbContext (o diretório é criado aqui, como antes no Desktop).
        var dbDir = Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath));
        if (!string.IsNullOrEmpty(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }
        var dbPath = options.DatabasePath;

        // Default Timeout = espera (busy timeout) quando outro processo (Desktop ou MCP) mantém o banco ocupado.
        // journal_mode não é alterado aqui.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            DefaultTimeout = SqliteBusyTimeoutSeconds,
        }.ToString();

        services.AddDbContext<TravelCutDbContext>(opts =>
        {
            opts.UseSqlite(connectionString);
        });

        // Repositories
        services.AddScoped<IProjectRepository, SqliteProjectRepository>();
        services.AddScoped<IMediaRepository, SqliteMediaRepository>();
        services.AddScoped<ITranscriptRepository, SqliteTranscriptRepository>();
        services.AddScoped<IEditPlanRepository, SqliteEditPlanRepository>();
        services.AddScoped<IPlanRevisionRepository, SqlitePlanRevisionRepository>();

        // Trava entre processos (Desktop e MCP sobre o mesmo projeto): editor-apply e analysis.
        services.AddSingleton<IProjectOperationLock, FileProjectOperationLock>();

        // Plano editorial (F06): artefatos em 3-Trabalho/plans; assistente narrativo ainda não configurado.
        services.AddSingleton<IPlanArtifactStore, PlanArtifactStore>();
        services.AddSingleton<IFileExistence, FileSystemFileExistence>();
        services.AddSingleton<INarrativeAssistant, NullNarrativeAssistant>();
        services.AddSingleton(TimeProvider.System);

        // Jobs
        services.AddScoped<IJobQueue, InMemoryJobQueue>();

        // Editor (F08). TRAVELCUT_EDITOR_ADAPTER=mock → simulador (SIMULATED, sem .drp).
        // Qualquer outro valor ou ausente → DaVinci real; na VM sem Resolve ele responde NOT_INSTALLED (sem fallback silencioso ao mock).
        services.AddSingleton(ResolveBridgeOptions.FromEnvironment(options.AppBaseDirectory));
        services.AddSingleton<MockResolveAdapter>();
        services.AddSingleton<DaVinciResolveAdapter>();
        var useMockEditor = options.UseMockEditor;
        services.AddSingleton<IEditorAdapter>(sp => useMockEditor
            ? sp.GetRequiredService<MockResolveAdapter>()
            : sp.GetRequiredService<DaVinciResolveAdapter>());
        services.AddSingleton<IEditorArtifactStore, FileEditorArtifactStore>();
        services.AddScoped<EditorUseCases>();
        services.AddScoped<EditorIpcHandler>();

        // Animações (F09). TRAVELCUT_MOTION_ADAPTER=mock → simulador (SIMULATED, nenhum vídeo).
        // Qualquer outro valor ou ausente → HyperFrames real (TRAVELCUT_HYPERFRAMES_CLI); sem Node/CLI responde NODE_UNAVAILABLE/NOT_INSTALLED.
        services.AddSingleton(HyperFramesOptions.FromCliSetting(Environment.GetEnvironmentVariable(HyperFramesOptions.CliEnvironmentVariable)));
        var useMockMotion = string.Equals(
            Environment.GetEnvironmentVariable("TRAVELCUT_MOTION_ADAPTER"), "mock", StringComparison.OrdinalIgnoreCase);
        services.AddSingleton<IMotionRenderer>(sp => useMockMotion
            ? new MockMotionRenderer(sp.GetRequiredService<TimeProvider>())
            : new HyperFramesRenderer(sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<HyperFramesOptions>(), sp.GetRequiredService<TimeProvider>()));
        // Singleton: o semáforo de serialização de render só vale com uma instância.
        // Trava entre processos (operação motion-render) é a mesma instância usada por editor e análise.
        services.AddSingleton(sp => new MotionUseCases(
            sp.GetRequiredService<IMotionRenderer>(),
            sp.GetRequiredService<IProjectOperationLock>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<MotionIpcHandler>();

        // Media Services
        services.AddScoped<IMediaScannerService, MediaScannerService>();
        // Singleton: cache de disponibilidade do ffprobe compartilhado. Caminho absoluto vindo do localizador (TRAVELCUT_FFPROBE_PATH / pasta do ffmpeg / PATH).
        services.AddSingleton<IFfprobeExtractor>(_ => new FfprobeMetadataExtractor(TravelCut.Infrastructure.Media.FfprobeLocator.Locate()));
        services.AddScoped<IChronologyService, ChronologyService>();
        services.AddScoped<IMediaCacheManager, MediaCacheManager>();

        // Análise audiovisual F05: serviços sem estado (ou com estado de cache próprio) como singletons.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISilenceDetectionService, SilenceDetectionService>();
        services.AddSingleton<IWhisperTranscriptionService, WhisperTranscriptionService>();
        services.AddSingleton<ISceneClassifierService, SceneClassifierService>();
        services.AddSingleton<ICutCandidateService, CutCandidateService>();
        // Singleton: guarda jobs em memória e cria escopos próprios (DbContext) por mídia.
        services.AddSingleton<IPipelineJobManager, PipelineJobManager>();

        // Importação de mídia (Tela 07)
        services.AddSingleton<IFreeSpaceProvider, DriveFreeSpaceProvider>();
        services.AddSingleton<IThumbnailService>(sp =>
            new FfmpegThumbnailService(sp.GetRequiredService<ILogger<FfmpegThumbnailService>>()));
        // Singleton: guarda jobs em memória e cria escopos próprios (DbContext) por job.
        services.AddSingleton<IMediaImportService>(sp => new MediaImportService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IFreeSpaceProvider>(),
            sp.GetRequiredService<ILogger<MediaImportService>>(),
            sp.GetService<IPipelineJobManager>()));


        // Configurações persistentes e Assistente de Ambiente (F12)
        services.AddSingleton<ITravelCutSettingsService, TravelCutSettingsService>();
        services.AddSingleton<IModelDownloader, HttpModelDownloader>();
        services.AddSingleton<INativeInstallationAuthorizer, DenyAllNativeInstallationAuthorizer>();
        services.AddSingleton<IEnvironmentAssistantService, EnvironmentAssistantService>();


        // Use Cases
        services.AddScoped<OpenOrCreateProjectUseCase>();
        services.AddScoped<ScanMediaLibraryUseCase>();
        services.AddScoped<ListMediaAssetsUseCase>();
        services.AddScoped<GetMediaDetailsUseCase>();
        services.AddScoped<GetMediaChronologyUseCase>();
        services.AddScoped<ListDirectoryUseCase>();
        services.AddScoped<InspectCandidatesUseCase>();
        services.AddScoped<GetThumbnailUseCase>();
        services.AddScoped<OpenFolderUseCase>();
        services.AddScoped<PlanningUseCases>();
        services.AddScoped<IpcDispatcher>();

        // Abertura de pastas no Explorer (system.openFolder): processo sem shell.
        services.AddSingleton<IShellFolderOpener, ExplorerFolderOpener>();

        return services;
    }
}
