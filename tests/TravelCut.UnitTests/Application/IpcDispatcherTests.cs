using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ipc;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Cache;
using TravelCut.Infrastructure.Repositories;
using TravelCut.ResolveBridge;
using TravelCut.UnitTests.Audio;
using TravelCut.UnitTests.Planning;
using Xunit;

namespace TravelCut.Tests.Application;

public class IpcDispatcherTests : IDisposable
{
    private readonly MockProjectRepository _projectRepo = new();
    private readonly MockMediaRepository _mediaRepo = new();
    private readonly MockEditPlanRepository _editPlanRepo = new();
    private readonly MockResolveAdapter _editorAdapter = new(NullLogger<MockResolveAdapter>.Instance);
    private readonly PipelineHarness _pipeline = new();
    private readonly RecordingFolderOpener _opener = new();
    private readonly IpcDispatcher _dispatcher;

    public void Dispose() => _pipeline.Dispose();

    public IpcDispatcherTests()
    {
        var openUseCase = new OpenOrCreateProjectUseCase(
            _projectRepo,
            _mediaRepo,
            _editPlanRepo,
            NullLogger<OpenOrCreateProjectUseCase>.Instance
        );

        var scanner = new MediaScannerService();
        var ffprobe = new FfprobeMetadataExtractor();
        var chronology = new ChronologyService();
        var cacheManager = new MediaCacheManager();

        var scanUseCase = new ScanMediaLibraryUseCase(
            _projectRepo,
            _mediaRepo,
            scanner,
            ffprobe,
            NullLogger<ScanMediaLibraryUseCase>.Instance
        );

        var listUseCase = new ListMediaAssetsUseCase(_mediaRepo);
        var detailsUseCase = new GetMediaDetailsUseCase(_projectRepo, _mediaRepo);
        var chronologyUseCase = new GetMediaChronologyUseCase(_mediaRepo, chronology, NullLogger<GetMediaChronologyUseCase>.Instance);
        var listDirectoryUseCase = new ListDirectoryUseCase(NullLogger<ListDirectoryUseCase>.Instance);
        var inspectUseCase = new InspectCandidatesUseCase(_projectRepo, _mediaRepo, ffprobe, NullLogger<InspectCandidatesUseCase>.Instance);
        var thumbnailUseCase = new GetThumbnailUseCase(_projectRepo, _mediaRepo, new NoThumbnailService(), NullLogger<GetThumbnailUseCase>.Instance);

        var openFolderUseCase = new OpenFolderUseCase(_projectRepo, _opener, NullLogger<OpenFolderUseCase>.Instance);

        _planning = CreatePlanning(_projectRepo, _mediaRepo, _editPlanRepo, _pipeline.Manager, _planRevisions, _artifacts);

        _dispatcher = new IpcDispatcher(
            openUseCase,
            scanUseCase,
            listUseCase,
            detailsUseCase,
            chronologyUseCase,
            _editorAdapter,
            _pipeline.Manager,
            _projectRepo,
            _mediaRepo,
            _editPlanRepo,
            listDirectoryUseCase,
            inspectUseCase,
            thumbnailUseCase,
            new NoImportService(),
            openFolderUseCase,
            _planning,
            NullLogger<IpcDispatcher>.Instance
        );
    }

    private readonly FakePlanRevisionRepository _planRevisions = new();
    private readonly InMemoryPlanArtifactStore _artifacts = new();
    private readonly PlanningUseCases _planning;

    private static PlanningUseCases CreatePlanning(
        IProjectRepository projects,
        IMediaRepository media,
        IEditPlanRepository suggestions,
        IPipelineJobManager analysis,
        IPlanRevisionRepository revisions,
        IPlanArtifactStore artifacts) =>
        new(projects, media, suggestions, analysis, new ChronologyService(), revisions, artifacts,
            new FakeFileExistence(), TimeProvider.System, NullLogger<PlanningUseCases>.Instance);

    /// <summary>Registra as aberturas pedidas sem tocar no Explorer.</summary>
    private sealed class RecordingFolderOpener : IShellFolderOpener
    {
        public List<(string Path, bool SelectFile)> Calls { get; } = new();

        public void OpenInExplorer(string fullPath, bool selectFile) => Calls.Add((fullPath, selectFile));
    }

    /// <summary>Repositório de projetos que sempre falha: simula erro interno sem mensagem para o front.</summary>
    private sealed class ThrowingProjectRepository : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default) => throw new InvalidOperationException("segredo interno C:\\privado");
        public Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default) => throw new InvalidOperationException("segredo interno C:\\privado");
        public Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default) => throw new InvalidOperationException("segredo interno C:\\privado");
        public Task SaveAsync(Project project, CancellationToken ct = default) => throw new InvalidOperationException("segredo interno C:\\privado");
    }

    [Fact]
    public async Task DispatchAsync_InternalError_ReturnsGenericMessageWithRequestIdAndNoDetails()
    {
        var failing = new OpenOrCreateProjectUseCase(new ThrowingProjectRepository(), _mediaRepo, _editPlanRepo, NullLogger<OpenOrCreateProjectUseCase>.Instance);
        var dispatcher = new IpcDispatcher(
            failing, new ScanMediaLibraryUseCase(_projectRepo, _mediaRepo, new MediaScannerService(), new FfprobeMetadataExtractor(), NullLogger<ScanMediaLibraryUseCase>.Instance),
            new ListMediaAssetsUseCase(_mediaRepo), new GetMediaDetailsUseCase(_projectRepo, _mediaRepo),
            new GetMediaChronologyUseCase(_mediaRepo, new ChronologyService(), NullLogger<GetMediaChronologyUseCase>.Instance),
            _editorAdapter, _pipeline.Manager, new ThrowingProjectRepository(), _mediaRepo, _editPlanRepo,
            new ListDirectoryUseCase(NullLogger<ListDirectoryUseCase>.Instance),
            new InspectCandidatesUseCase(_projectRepo, _mediaRepo, new FfprobeMetadataExtractor(), NullLogger<InspectCandidatesUseCase>.Instance),
            new GetThumbnailUseCase(_projectRepo, _mediaRepo, new NoThumbnailService(), NullLogger<GetThumbnailUseCase>.Instance),
            new NoImportService(),
            new OpenFolderUseCase(_projectRepo, _opener, NullLogger<OpenFolderUseCase>.Instance),
            _planning,
            NullLogger<IpcDispatcher>.Instance);

        var response = await dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-internal",
            Type = "project.recent",
            Payload = null
        });

        Assert.False(response.Ok);
        Assert.Equal("INTERNAL_ERROR", response.Error!.Code);
        Assert.Equal("Erro interno (ref req-internal)", response.Error.Message);
        Assert.Null(response.Error.Details);
        var json = JsonSerializer.Serialize(response);
        Assert.DoesNotContain("segredo", json);
        Assert.DoesNotContain("StackTrace", json);
    }

    [Fact]
    public async Task DispatchAsync_SystemOpenFolder_OutsideKnownProject_IsRejectedAndNothingOpens()
    {
        string root = Path.Combine(Path.GetTempPath(), "tc_ipc_open_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string outside = Path.Combine(Path.GetTempPath(), "tc_ipc_outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            _projectRepo.Projects.Add(new Project { ProjectId = Guid.NewGuid(), Name = "P", RootPath = root });

            var response = await _dispatcher.DispatchAsync(new IpcRequest
            {
                RequestId = "req-open-out",
                Type = "system.openFolder",
                Payload = new { path = outside }
            });

            Assert.False(response.Ok);
            Assert.Equal("PATH_NOT_ALLOWED", response.Error!.Code);
            Assert.Empty(_opener.Calls);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task DispatchAsync_SystemOpenFolder_InsideKnownProject_OpensThroughPort()
    {
        string root = Path.Combine(Path.GetTempPath(), "tc_ipc_open_ok_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "1-Bruto"));
        try
        {
            _projectRepo.Projects.Add(new Project { ProjectId = Guid.NewGuid(), Name = "P", RootPath = root });

            var response = await _dispatcher.DispatchAsync(new IpcRequest
            {
                RequestId = "req-open-ok",
                Type = "system.openFolder",
                Payload = new { folderPath = Path.Combine(root, "1-Bruto") }
            });

            Assert.True(response.Ok);
            var single = Assert.Single(_opener.Calls);
            Assert.False(single.SelectFile);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "1-Bruto")), single.Path);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DispatchAsync_ProjectCreate_ReportsCreatedFoldersAndStatusFromRealResult()
    {
        string root = Path.Combine(Path.GetTempPath(), "tc_ipc_status_" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = await _dispatcher.DispatchAsync(new IpcRequest
            {
                RequestId = "req-status-1",
                Type = "project.create",
                Payload = new CreateProjectRequest(RootPath: root, Name: "Status", CreateFoldersIfNotExists: true)
            });

            Assert.True(first.Ok);
            var firstJson = JsonSerializer.Serialize(first.Payload);
            Assert.Contains("\"status\":\"created\"", firstJson);
            Assert.Contains("\"createdFolders\":[\"1-Bruto\",\"2-Assets\",\"3-Trabalho\",\"4-Final\"]", firstJson);

            var second = await _dispatcher.DispatchAsync(new IpcRequest
            {
                RequestId = "req-status-2",
                Type = "project.create",
                Payload = new CreateProjectRequest(RootPath: root, Name: "Status", CreateFoldersIfNotExists: true)
            });

            Assert.True(second.Ok);
            var secondJson = JsonSerializer.Serialize(second.Payload);
            Assert.Contains("\"status\":\"opened_existing\"", secondJson);
            Assert.Contains("\"createdFolders\":[]", secondJson);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DispatchAsync_ProjectRecent_EstimateIsNullAndTotalsOnlyCountOkMetadata()
    {
        var projectId = Guid.NewGuid();
        await _projectRepo.SaveAsync(new Project { ProjectId = projectId, RootPath = Path.Combine(Path.GetTempPath(), "tc_recent_" + projectId.ToString("N")), Name = "Recente" });
        await _mediaRepo.SaveBatchAsync(new[]
        {
            new MediaAsset
            {
                MediaId = Guid.NewGuid(), ProjectId = projectId, RelativePath = "1-Bruto/ok.mp4", ByteLength = 1, Kind = MediaKind.Video,
                Metadata = new MediaMetadata { DurationSeconds = 10.0, MetadataStatus = MediaMetadataStatus.Ok }
            },
            new MediaAsset
            {
                MediaId = Guid.NewGuid(), ProjectId = projectId, RelativePath = "1-Bruto/sem.mp4", ByteLength = 1, Kind = MediaKind.Video,
                Metadata = new MediaMetadata { DurationSeconds = 0.0, MetadataStatus = MediaMetadataStatus.NotRequested }
            }
        });

        var response = await _dispatcher.DispatchAsync(new IpcRequest { RequestId = "req-recent-sum", Type = "project.recent", Payload = null });

        Assert.True(response.Ok);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"estimatedCutDurationSeconds\":null", json);
        Assert.Contains("\"totalOriginalDurationSeconds\":10", json);
        Assert.Contains("\"mediaWithoutMetadataCount\":1", json);
    }

    [Fact]
    public async Task DispatchAsync_FsListDirectory_ReturnsOnlyMediaAndFolders()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tc_ipc_list_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "Sub"));
            File.WriteAllBytes(Path.Combine(tempDir, "clip.mp4"), new byte[16]);
            File.WriteAllText(Path.Combine(tempDir, "leia-me.txt"), "x");

            var request = new IpcRequest
            {
                RequestId = "req-list-dir",
                Type = "fs.listDirectory",
                Payload = new ListDirectoryRequest(tempDir)
            };

            var response = await _dispatcher.DispatchAsync(request);

            Assert.True(response.Ok);
            var json = JsonSerializer.Serialize(response.Payload);
            Assert.Contains("\"name\":\"Sub\"", json);
            Assert.Contains("\"name\":\"clip.mp4\"", json);
            Assert.DoesNotContain("leia-me.txt", json);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task DispatchAsync_MediaImportStatus_UnknownJobReturnsNotFound()
    {
        var request = new IpcRequest
        {
            RequestId = "req-import-status",
            Type = "media.import.status",
            Payload = new ImportJobRequest(Guid.NewGuid())
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.False(response.Ok);
        Assert.Equal("IMPORT_NOT_FOUND", response.Error!.Code);
    }

    [Fact]
    public async Task DispatchAsync_PipelineStatus_WithoutJob_ReturnsIdleWithoutFixedValues()
    {
        var projectId = Guid.NewGuid();

        var response = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-pipeline-status",
            Type = "pipeline.status",
            Payload = new ProjectRequest(projectId)
        });

        Assert.True(response.Ok);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"state\":\"idle\"", json);
        Assert.Contains("\"stages\":[]", json);
        // Valores fixos do antigo pipeline (progresso 10%, 48 arquivos) não podem voltar.
        Assert.DoesNotContain("totalFiles", json);
        Assert.DoesNotContain("progressPercent", json);
        Assert.DoesNotContain("Indexando", json);
    }

    [Fact]
    public async Task DispatchAsync_PipelineStart_UnknownProject_ReturnsProjectNotFoundCode()
    {
        var response = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-pipeline-start",
            Type = "pipeline.start",
            Payload = new StartPipelineRequest(Guid.NewGuid(), null, null, null, false)
        });

        Assert.False(response.Ok);
        Assert.Equal("PROJECT_NOT_FOUND", response.Error!.Code);
    }

    [Fact]
    public async Task DispatchAsync_PipelineRetryFailed_WithoutJob_ReturnsNoMediaCode_AndInvalidIdIsRejected()
    {
        var none = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-retry-none", Type = "pipeline.retryFailed", Payload = new ProjectRequest(Guid.NewGuid())
        });
        Assert.False(none.Ok);
        Assert.Equal("NO_MEDIA_TO_ANALYZE", none.Error!.Code);

        var invalid = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-retry-invalid", Type = "pipeline.retryFailed", Payload = new ProjectRequest(Guid.Empty)
        });
        Assert.False(invalid.Ok);
        Assert.Equal("INVALID_PROJECT_ID", invalid.Error!.Code);
    }

    [Fact]
    public async Task DispatchAsync_PipelineRetryFailed_ReenqueuesFailedMediaOfLastJob()
    {
        _pipeline.Whisper.Available = true; // fake sem resultado configurado = transcrição "failed"
        var project = _pipeline.CreateProject();
        _pipeline.AddMedia(project, "1-Bruto/a.mp4");
        await _pipeline.Manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false));
        await _pipeline.WaitForFinishAsync(project.ProjectId);
        int callsBefore = _pipeline.Whisper.TranscribeCalls;

        var response = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-retry", Type = "pipeline.retryFailed", Payload = new ProjectRequest(project.ProjectId)
        });

        Assert.True(response.Ok);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"outcome\":\"started\"", json);
        await _pipeline.WaitForFinishAsync(project.ProjectId);
        Assert.True(_pipeline.Whisper.TranscribeCalls > callsBefore);
    }

    [Fact]
    public async Task DispatchAsync_PipelineCancel_UnknownJob_ReturnsCanceledFalse()
    {
        var response = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-pipeline-cancel",
            Type = "pipeline.cancel",
            Payload = new PipelineJobRequest(Guid.NewGuid())
        });

        Assert.True(response.Ok);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"canceled\":false", json);
    }

    [Fact]
    public async Task DispatchAsync_AnalysisEnvironment_ReportsLargeV3AndHonestAvailability()
    {
        var response = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-analysis-env",
            Type = "analysis.environment",
            Payload = null
        });

        Assert.True(response.Ok);
        var json = JsonSerializer.Serialize(response.Payload);
        var env = JsonSerializer.Deserialize<AnalysisEnvironmentDto>(json);
        Assert.NotNull(env);
        Assert.Equal("large-v3", env!.WhisperModel);
        Assert.False(env.WhisperAvailable);
        Assert.NotEmpty(env.Reasons);
    }

    [Fact]
    public async Task DispatchAsync_AnalysisGetMedia_NeverAnalyzed_ReturnsNotRun()
    {
        var response = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-analysis-media",
            Type = "analysis.getMedia",
            Payload = new MediaAnalysisRequest(Guid.NewGuid(), Guid.NewGuid())
        });

        Assert.True(response.Ok);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"transcriptionStatus\":\"not_run\"", json);
        Assert.Contains("\"silenceStatus\":\"not_run\"", json);
        Assert.Contains("\"cutCandidates\":[]", json);
    }

    [Fact]
    public async Task DispatchAsync_SuggestionsList_ReadsRepositoryWithStoredKindAndSeconds()
    {
        _editPlanRepo.Suggestions.Add(new EditSuggestion
        {
            SuggestionId = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            SourceInterval = new TimeRange(1000, 2500),
            Kind = SuggestionKind.Silence,
            EvidenceRefs = "{\"kind\":\"pause\"}",
            Rationale = "Pausa de 1,5 s",
            ConfidenceBand = "medium",
            NeedsHumanReview = true,
            SuggestedAction = "trim",
            Status = SuggestionStatus.Proposed,
            Author = "vad-analyzer"
        });

        var response = await _dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "req-suggestions",
            Type = "suggestions.list",
            Payload = new ProjectRequest(Guid.NewGuid())
        });

        Assert.True(response.Ok);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"kind\":\"pause\"", json);
        Assert.Contains("\"startSeconds\":1", json);
        Assert.Contains("\"endSeconds\":2.5", json);
        Assert.DoesNotContain("\"Silence\"", json);
    }

    private sealed class NoThumbnailService : TravelCut.Application.Ports.IThumbnailService
    {
        public Task<TravelCut.Application.Ports.ThumbnailResult> GenerateAsync(string projectRoot, string mediaPath, double atSeconds, CancellationToken ct = default) =>
            Task.FromResult(new TravelCut.Application.Ports.ThumbnailResult(TravelCut.Application.Ports.ThumbnailStatus.FfmpegUnavailable, null, "sem ffmpeg nos testes"));
    }

    private sealed class NoImportService : TravelCut.Application.Ports.IMediaImportService
    {
        public Task<TravelCut.Application.Ports.ImportStartResult> StartAsync(StartImportRequest request, CancellationToken ct = default) =>
            Task.FromResult(new TravelCut.Application.Ports.ImportStartResult(false, null, "NOT_AVAILABLE", "sem serviço nos testes"));

        public ImportJobStatusDto? GetStatus(Guid jobId) => null;

        public CancelImportResponse Cancel(Guid jobId) => new(false);
    }

    [Fact]
    public async Task DispatchAsync_Ping_ReturnsPong()
    {
        var request = new IpcRequest
        {
            RequestId = "req-1",
            Type = "system.ping",
            Payload = null
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.Equal("req-1", response.RequestId);
        Assert.NotNull(response.Payload);

        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"status\":\"ok\"", json);
    }

    [Fact]
    public async Task DispatchAsync_UnknownMethod_ReturnsError()
    {
        var request = new IpcRequest
        {
            RequestId = "req-2",
            Type = "unknown.action",
            Payload = null
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.False(response.Ok);
        Assert.Equal("req-2", response.RequestId);
        Assert.NotNull(response.Error);
        Assert.Equal("UNKNOWN_METHOD", response.Error.Code);
    }

    [Fact]
    public async Task DispatchAsync_EditorCapabilities_ReturnsMockCapabilities()
    {
        var request = new IpcRequest
        {
            RequestId = "req-3",
            Type = "editor.capabilities",
            Payload = null
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.NotNull(response.Payload);

        var json = JsonSerializer.Serialize(response.Payload);
        var caps = JsonSerializer.Deserialize<EditorCapabilitiesDto>(json);

        Assert.NotNull(caps);
        Assert.Equal("mock", caps.AdapterMode);
        Assert.True(caps.IsAvailable);
    }

    [Fact]
    public async Task DispatchAsync_DecisionsSubmit_SavesDecisionSuccessfully()
    {
        var suggestionId = Guid.NewGuid();
        _editPlanRepo.Suggestions.Add(new EditSuggestion { SuggestionId = suggestionId, MediaId = Guid.NewGuid(), Author = "vad" });
        var decisionDto = new SubmitDecisionRequest(
            SuggestionId: suggestionId,
            Action: "approve",
            Reviewer: "editor1",
            Notes: "Approved for timeline insertion"
        );

        var request = new IpcRequest
        {
            RequestId = "req-4",
            Type = "decision.submit",
            Payload = decisionDto
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.Single(_editPlanRepo.SavedDecisions);
        Assert.Equal("approve", _editPlanRepo.SavedDecisions[0].Action);
        Assert.Equal("editor1", _editPlanRepo.SavedDecisions[0].Reviewer);
    }

    [Fact]
    public async Task DispatchAsync_ProjectCreate_AutoCreatesFoldersAndReturnsSuccess()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tc_ipc_create_" + Guid.NewGuid().ToString("N"));
        try
        {
            var req = new CreateProjectRequest(
                RootPath: tempDir,
                Name: "Projeto Teste IPC",
                CreateFoldersIfNotExists: true
            );

            var request = new IpcRequest
            {
                RequestId = "req-create",
                Type = "project.create",
                Payload = req
            };

            var response = await _dispatcher.DispatchAsync(request);

            Assert.True(response.Ok);
            Assert.NotNull(response.Payload);
            Assert.True(Directory.Exists(Path.Combine(tempDir, "1-Bruto")));
            Assert.True(Directory.Exists(Path.Combine(tempDir, "2-Assets")));
            Assert.True(Directory.Exists(Path.Combine(tempDir, "3-Trabalho")));
            Assert.True(Directory.Exists(Path.Combine(tempDir, "4-Final")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task DispatchAsync_ProjectRecent_ReturnsProjectsList()
    {
        var request = new IpcRequest
        {
            RequestId = "req-recents",
            Type = "project.recent",
            Payload = null
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.NotNull(response.Payload);
    }

    [Fact]
    public async Task DispatchAsync_MediaList_ReturnsAssets()
    {
        var projectId = Guid.NewGuid();
        var asset = new MediaAsset
        {
            MediaId = Guid.NewGuid(),
            ProjectId = projectId,
            RelativePath = "1-Bruto/DJI_0001.MP4",
            ByteLength = 1024 * 1024,
            Kind = MediaKind.Video,
            Metadata = new MediaMetadata
            {
                Codec = "h264",
                Width = 3840,
                Height = 2160,
                FrameRate = 29.97,
                DurationSeconds = 12.5,
                TimeBase = RationalTimeBase.Fps2997,
                CreationTimeUtc = DateTime.UtcNow
            }
        };
        await _mediaRepo.SaveAsync(asset);

        var request = new IpcRequest
        {
            RequestId = "req-media-list",
            Type = "media.list",
            Payload = new { projectId }
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.NotNull(response.Payload);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("DJI_0001.MP4", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var list = JsonSerializer.Deserialize<IReadOnlyList<MediaAssetDto>>(doc.RootElement.GetRawText());
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("1-Bruto/DJI_0001.MP4", list[0].RelativePath);
    }

    [Fact]
    public async Task DispatchAsync_MediaList_EmptyProject_ReturnsEmptyArray()
    {
        var emptyProjectId = Guid.NewGuid();
        var request = new IpcRequest
        {
            RequestId = "req-media-list-empty",
            Type = "media.list",
            Payload = new { projectId = emptyProjectId }
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.NotNull(response.Payload);
        var json = JsonSerializer.Serialize(response.Payload);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task DispatchAsync_MediaList_NullPayload_ReturnsEmptyArrayOrHandlesGracefully()
    {
        var request = new IpcRequest
        {
            RequestId = "req-media-list-null",
            Type = "media.list",
            Payload = null
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.NotNull(response.Payload);
        var json = JsonSerializer.Serialize(response.Payload);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task DispatchAsync_MediaGetDetails_ReturnsDetailedInfo()
    {
        var projectId = Guid.NewGuid();
        await _projectRepo.SaveAsync(new Project
        {
            ProjectId = projectId,
            RootPath = "C:/fake/project",
            Name = "Test Project"
        });

        var mediaId = Guid.NewGuid();
        var asset = new MediaAsset
        {
            MediaId = mediaId,
            ProjectId = projectId,
            RelativePath = "1-Bruto/GOPR0002.MP4",
            ByteLength = 2048 * 1024,
            Kind = MediaKind.Video,
            Metadata = new MediaMetadata
            {
                Codec = "hevc",
                Width = 1920,
                Height = 1080,
                FrameRate = 60.0,
                DurationSeconds = 25.0,
                TimeBase = RationalTimeBase.Fps60,
                CreationTimeUtc = new DateTime(2026, 5, 10, 14, 30, 0, DateTimeKind.Utc)
            }
        };
        await _mediaRepo.SaveAsync(asset);

        var request = new IpcRequest
        {
            RequestId = "req-media-details",
            Type = "media.getDetails",
            Payload = new { mediaId }
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.NotNull(response.Payload);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("GOPR0002.MP4", json);
        Assert.Contains("hevc", json);
    }

    [Fact]
    public async Task DispatchAsync_MediaGetChronology_ReturnsReport()
    {
        var projectId = Guid.NewGuid();
        var baseDate = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        var asset1 = new MediaAsset
        {
            MediaId = Guid.NewGuid(),
            ProjectId = projectId,
            RelativePath = "1-Bruto/clip1.mp4",
            ByteLength = 1000,
            Kind = MediaKind.Video,
            Metadata = new MediaMetadata
            {
                DurationSeconds = 60,
                CreationTimeUtc = baseDate
            }
        };

        var asset2 = new MediaAsset
        {
            MediaId = Guid.NewGuid(),
            ProjectId = projectId,
            RelativePath = "1-Bruto/clip2.mp4",
            ByteLength = 1000,
            Kind = MediaKind.Video,
            Metadata = new MediaMetadata
            {
                DurationSeconds = 60,
                CreationTimeUtc = baseDate.AddMinutes(5)
            }
        };

        await _mediaRepo.SaveBatchAsync(new[] { asset1, asset2 });

        var request = new IpcRequest
        {
            RequestId = "req-media-chronology",
            Type = "media.getChronology",
            Payload = new { projectId, sessionGapMinutes = 30.0 }
        };

        var response = await _dispatcher.DispatchAsync(request);

        Assert.True(response.Ok);
        Assert.NotNull(response.Payload);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("totalClips\":2", json);
        Assert.Contains("sessions\":[", json);
    }

    // ------------------------------------------------------------------ Plano editorial (profile.get, plan.*)

    private Task<IpcResponse> Dispatch(string type, object? payload) =>
        _dispatcher.DispatchAsync(new IpcRequest { RequestId = "req-" + Guid.NewGuid().ToString("N"), Type = type, Payload = payload });

    private void SeedPlanProject(bool withMedia = true)
    {
        _projectRepo.Projects.Add(new Project { ProjectId = TravelFixture.ProjectId, Name = "Viagem", RootPath = @"C:\fake\viagem" });
        if (withMedia) _mediaRepo.Assets.AddRange(TravelFixture.Media());
    }

    [Fact]
    public async Task DispatchAsync_ProfileGet_ReturnsViagensDefaultWithReviewAmbiguousTrue()
    {
        var response = await Dispatch("profile.get", null);

        Assert.True(response.Ok);
        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"profileId\":\"viagens\"", json);
        Assert.Contains("\"reviewAmbiguous\":true", json);
    }

    [Fact]
    public async Task DispatchAsync_AutomationProfile_GetDefaultThenSetThenGet()
    {
        SeedPlanProject();

        var first = await Dispatch("automation.profile.get", new { projectId = TravelFixture.ProjectId });
        Assert.True(first.Ok);
        var firstJson = JsonSerializer.Serialize(first.Payload);
        Assert.Contains("\"profile\":\"balanced\"", firstJson);
        Assert.Contains("\"isDefault\":true", firstJson);

        var set = await Dispatch("automation.profile.set", new { projectId = TravelFixture.ProjectId, profile = "aggressive" });
        Assert.True(set.Ok);

        var again = await Dispatch("automation.profile.get", new { projectId = TravelFixture.ProjectId });
        var againJson = JsonSerializer.Serialize(again.Payload);
        Assert.Contains("\"profile\":\"aggressive\"", againJson);
        Assert.Contains("\"isDefault\":false", againJson);
    }

    [Fact]
    public async Task DispatchAsync_AutomationProfile_SetInvalidProfile_FailsWithInvalidArgument()
    {
        SeedPlanProject();

        var response = await Dispatch("automation.profile.set", new { projectId = TravelFixture.ProjectId, profile = "turbo" });

        Assert.False(response.Ok);
        Assert.Equal("INVALID_ARGUMENT", response.ErrorCode);
    }

    [Fact]
    public async Task DispatchAsync_PlanGet_WithoutPlan_ReturnsNullPayload()
    {
        SeedPlanProject();

        var response = await Dispatch("plan.get", new { projectId = TravelFixture.ProjectId });

        Assert.True(response.Ok);
        Assert.Null(response.Payload);
    }

    [Fact]
    public async Task DispatchAsync_PlanGenerate_UnknownProject_ReturnsProjectNotFound()
    {
        var response = await Dispatch("plan.generate", new { projectId = Guid.NewGuid() });

        Assert.False(response.Ok);
        Assert.Equal("PROJECT_NOT_FOUND", response.Error!.Code);
    }

    [Fact]
    public async Task DispatchAsync_PlanGenerate_WithoutEligibleMedia_ReturnsNoMediaToPlan()
    {
        SeedPlanProject(withMedia: false);

        var response = await Dispatch("plan.generate", new { projectId = TravelFixture.ProjectId });

        Assert.False(response.Ok);
        Assert.Equal("NO_MEDIA_TO_PLAN", response.Error!.Code);
    }

    [Fact]
    public async Task DispatchAsync_PlanGenerateGetValidateListRevert_RoundTrip()
    {
        SeedPlanProject();

        var generated = await Dispatch("plan.generate", new { projectId = TravelFixture.ProjectId });
        Assert.True(generated.Ok, generated.Error?.Message);
        var generatedJson = JsonSerializer.Serialize(generated.Payload);
        Assert.Contains("\"planFingerprint\":", generatedJson);
        Assert.Contains("\"revision\":1", generatedJson);

        var get = await Dispatch("plan.get", new { projectId = TravelFixture.ProjectId, revision = 1 });
        Assert.True(get.Ok);
        Assert.NotNull(get.Payload);

        var validate = await Dispatch("plan.validate", new { projectId = TravelFixture.ProjectId, revision = 1 });
        Assert.True(validate.Ok);
        Assert.Contains("\"isValid\":true", JsonSerializer.Serialize(validate.Payload));

        var list = await Dispatch("plan.list", new { projectId = TravelFixture.ProjectId });
        Assert.True(list.Ok);
        Assert.Contains("\"revisions\":[", JsonSerializer.Serialize(list.Payload));

        var reverted = await Dispatch("plan.revert", new { projectId = TravelFixture.ProjectId, toRevision = 1 });
        Assert.True(reverted.Ok);
        Assert.Contains("\"author\":\"revert\"", JsonSerializer.Serialize(reverted.Payload));
    }

    [Fact]
    public async Task DispatchAsync_PlanGet_UnknownRevision_ReturnsPlanNotFound()
    {
        SeedPlanProject();
        await Dispatch("plan.generate", new { projectId = TravelFixture.ProjectId });

        var response = await Dispatch("plan.get", new { projectId = TravelFixture.ProjectId, revision = 99 });

        Assert.False(response.Ok);
        Assert.Equal("PLAN_NOT_FOUND", response.Error!.Code);
    }

    [Fact]
    public async Task DispatchAsync_PlanGet_MissingArtifact_ReturnsPlanFileMissing()
    {
        SeedPlanProject();
        await Dispatch("plan.generate", new { projectId = TravelFixture.ProjectId });
        var (planId, revision) = _artifacts.Files.Keys.Single();
        _artifacts.Remove(planId, revision);

        var response = await Dispatch("plan.get", new { projectId = TravelFixture.ProjectId, revision });

        Assert.False(response.Ok);
        Assert.Equal("PLAN_FILE_MISSING", response.Error!.Code);
    }

    [Fact]
    public async Task DispatchAsync_PlanRevert_UnknownRevision_ReturnsPlanNotFound()
    {
        SeedPlanProject();

        var response = await Dispatch("plan.revert", new { projectId = TravelFixture.ProjectId, toRevision = 1 });

        Assert.False(response.Ok);
        Assert.Equal("PLAN_NOT_FOUND", response.Error!.Code);
    }

    [Fact]
    public async Task DispatchAsync_PlanList_EmptyProjectId_ReturnsInvalidArgument()
    {
        var response = await Dispatch("plan.list", new { projectId = Guid.Empty });

        Assert.False(response.Ok);
        Assert.Equal("INVALID_ARGUMENT", response.Error!.Code);
    }

    private class MockJobQueue : IJobQueue
    {
        public Task<ProcessingJob> EnqueueAsync(Guid projectId, JobType type, CancellationToken ct = default) =>
            Task.FromResult(new ProcessingJob { JobId = Guid.NewGuid(), ProjectId = projectId, Type = type });

        public Task UpdateProgressAsync(Guid jobId, int completed, int total, string stepDescription, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<ProcessingJob?> GetJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<ProcessingJob?>(null);

        public Task<List<ProcessingJob>> ListJobsByProjectAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult(new List<ProcessingJob>());
    }

    private class MockProjectRepository : IProjectRepository
    {
        public readonly List<Project> Projects = [];
        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult(Projects.FirstOrDefault(p => p.ProjectId == projectId));
        public Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default) =>
            Task.FromResult(Projects.FirstOrDefault(p => p.RootPath == rootPath));
        public Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default) =>
            Task.FromResult(Projects.Take(limit).ToList());
        public Task SaveAsync(Project project, CancellationToken ct = default)
        {
            Projects.RemoveAll(p => p.ProjectId == project.ProjectId);
            Projects.Add(project);
            return Task.CompletedTask;
        }
    }

    private class MockMediaRepository : IMediaRepository
    {
        public readonly List<MediaAsset> Assets = [];

        public Task<MediaAsset?> GetByIdAsync(Guid mediaId, CancellationToken ct = default) =>
            Task.FromResult(Assets.FirstOrDefault(a => a.MediaId == mediaId));

        public Task<List<MediaAsset>> ListByProjectAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult(Assets.Where(a => a.ProjectId == projectId).ToList());

        public Task SaveAsync(MediaAsset asset, CancellationToken ct = default)
        {
            Assets.RemoveAll(a => a.MediaId == asset.MediaId);
            Assets.Add(asset);
            return Task.CompletedTask;
        }

        public Task SaveBatchAsync(IEnumerable<MediaAsset> assets, CancellationToken ct = default)
        {
            foreach (var asset in assets)
            {
                Assets.RemoveAll(a => a.MediaId == asset.MediaId);
                Assets.Add(asset);
            }
            return Task.CompletedTask;
        }
    }

    private class MockEditPlanRepository : IEditPlanRepository
    {
        public readonly List<EditDecision> SavedDecisions = [];
        public readonly List<EditSuggestion> Suggestions = [];

        public Task<EditPlan?> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<EditPlan?>(null);
        public Task SavePlanAsync(EditPlan plan, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<EditSuggestion>> ListSuggestionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(Suggestions);
        public Task SaveSuggestionsBatchAsync(IEnumerable<EditSuggestion> suggestions, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default)
        {
            // Mesmo contrato do repositório real: append-only com Version = última + 1 por sugestão.
            decision.Version = SavedDecisions.Where(d => d.SuggestionId == decision.SuggestionId).Select(d => d.Version).DefaultIfEmpty(0).Max() + 1;
            SavedDecisions.Add(decision);
            return Task.CompletedTask;
        }
        public Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(SavedDecisions);
        public Task<List<EditDecision>> ListDecisionsBySuggestionAsync(Guid suggestionId, CancellationToken ct = default) =>
            Task.FromResult(SavedDecisions.Where(d => d.SuggestionId == suggestionId).OrderBy(d => d.Version).ToList());
        public async Task SaveDecisionsAtomicAsync(IReadOnlyList<EditDecision> decisions, CancellationToken ct = default)
        {
            foreach (var decision in decisions) await SaveDecisionAsync(decision, ct);
        }
        public async Task<EditDecision?> SaveDecisionIfAsync(Guid suggestionId, Func<IReadOnlyList<EditDecision>, EditDecision?> build, CancellationToken ct = default)
        {
            var decision = build(SavedDecisions.Where(d => d.SuggestionId == suggestionId).OrderBy(d => d.Version).ToList());
            if (decision is null) return null;
            await SaveDecisionAsync(decision, ct);
            return decision;
        }
        public Task<List<EditSuggestion>> ListSuggestionsByIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default) =>
            Task.FromResult(Suggestions.Where(s => suggestionIds.Contains(s.SuggestionId)).ToList());
        public Task<EditDecision?> GetLatestDecisionBySuggestionAsync(Guid suggestionId, CancellationToken ct = default)
        {
            var item = SavedDecisions.Where(d => d.SuggestionId == suggestionId).OrderByDescending(d => d.Version).FirstOrDefault();
            return Task.FromResult(item);
        }
        public Task<List<Guid>> FindExistingSuggestionIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default) =>
            Task.FromResult(Suggestions.Select(s => s.SuggestionId).Where(suggestionIds.Contains).Distinct().ToList());
    }
}
