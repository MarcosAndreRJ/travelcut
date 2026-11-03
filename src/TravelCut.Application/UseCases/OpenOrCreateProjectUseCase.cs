using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.UseCases;

/// <summary>
/// Resultado de project.open/project.create.
/// CreatedFolders: somente as pastas canônicas efetivamente criadas nesta chamada.
/// IsNewProject: true quando o registro do projeto foi criado agora (status "created"); false quando já existia ("opened_existing").
/// </summary>
public sealed record OpenProjectOutcome(
    bool Success,
    ProjectSummaryDto? Summary,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<string> CreatedFolders,
    bool IsNewProject)
{
    public static OpenProjectOutcome Fail(string code, string message) =>
        new(false, null, code, message, Array.Empty<string>(), false);
}

public class OpenOrCreateProjectUseCase
{
    private readonly IProjectRepository _projectRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IEditPlanRepository _planRepo;
    private readonly ILogger<OpenOrCreateProjectUseCase> _logger;

    public OpenOrCreateProjectUseCase(
        IProjectRepository projectRepo,
        IMediaRepository mediaRepo,
        IEditPlanRepository planRepo,
        ILogger<OpenOrCreateProjectUseCase> logger)
    {
        _projectRepo = projectRepo;
        _mediaRepo = mediaRepo;
        _planRepo = planRepo;
        _logger = logger;
    }

    public async Task<OpenProjectOutcome> ExecuteAsync(
        string rootPath,
        bool autoCreateFolders = false,
        string? projectName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return OpenProjectOutcome.Fail("INVALID_PATH", "O caminho raiz do projeto não pode ser vazio.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = ProjectRootPathRules.Normalize(rootPath);
        }
        catch (Exception)
        {
            return OpenProjectOutcome.Fail("INVALID_PATH", "Caminho inválido.");
        }

        // Antes de qualquer criação de pasta: raiz de unidade, pasta de sistema ou perfil do usuário nunca vira projeto.
        string? violation = ProjectRootPathRules.FindViolation(normalizedPath, ProjectRootPathRules.CurrentSystemFolders());
        if (violation != null)
        {
            _logger.LogWarning("Raiz de projeto recusada: {Reason}", violation);
            return OpenProjectOutcome.Fail(
                "INVALID_PROJECT_ROOT",
                "Escolha uma pasta dedicada ao projeto. " + violation);
        }

        // Códigos quando a estrutura não existe: pasta raiz ausente e autoCreateFolders=false → DIRECTORY_NOT_FOUND;
        // pasta existe mas faltam 1-Bruto/2-Assets/3-Trabalho/4-Final e autoCreateFolders=false → INVALID_FOUR_FOLDERS
        // (mensagem com a pasta ausente). Com autoCreateFolders=true, as pastas são criadas e não há falha.
        if (!Directory.Exists(normalizedPath))
        {
            if (!autoCreateFolders)
            {
                return OpenProjectOutcome.Fail("DIRECTORY_NOT_FOUND", "O diretório informado não foi encontrado.");
            }

            try
            {
                Directory.CreateDirectory(normalizedPath);
                _logger.LogInformation("Diretório raiz do projeto criado: {Path}", normalizedPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao criar diretório raiz {Path}", normalizedPath);
                return OpenProjectOutcome.Fail("DIRECTORY_CREATE_FAILED", "Não foi possível criar o diretório raiz do projeto.");
            }
        }

        var tempProject = new Project { RootPath = normalizedPath };
        var createdFolders = new List<string>();

        if (autoCreateFolders)
        {
            try
            {
                // Retorna os nomes efetivamente criados nesta chamada (a raiz já foi tratada acima).
                createdFolders = tempProject.EnsureFourFoldersStructure()
                    .Where(name => Project.RequiredFourFolders.Contains(name, StringComparer.Ordinal))
                    .ToList();
                if (createdFolders.Count > 0)
                {
                    _logger.LogInformation("Pastas canônicas criadas em {Path}: {Folders}", normalizedPath, string.Join(", ", createdFolders));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao criar estrutura das 4 pastas em {Path}", normalizedPath);
                return OpenProjectOutcome.Fail("FOLDER_CREATION_FAILED", "Falha ao criar a estrutura de pastas do projeto.");
            }
        }

        if (!tempProject.HasValidFourFoldersStructure(out string missingFolder))
        {
            _logger.LogWarning("Estrutura das 4 pastas inválida em {Path}. Pasta ausente: {Folder}", normalizedPath, missingFolder);
            return OpenProjectOutcome.Fail("INVALID_FOUR_FOLDERS",
                $"A pasta obrigatória '{missingFolder}' não foi encontrada no projeto. Estrutura exigida: 1-Bruto, 2-Assets, 3-Trabalho, 4-Final.");
        }

        var existing = await _projectRepo.GetByRootPathAsync(normalizedPath, ct);
        Project project;
        bool isNewProject = existing == null;
        if (existing == null)
        {
            string dirName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string effectiveName = !string.IsNullOrWhiteSpace(projectName)
                ? projectName.Trim()
                : (string.IsNullOrWhiteSpace(dirName) ? "Projeto Sem Nome" : dirName);

            project = new Project
            {
                ProjectId = Guid.NewGuid(),
                Name = effectiveName,
                RootPath = normalizedPath,
                CreatedAtUtc = DateTime.UtcNow,
                LastModifiedAtUtc = DateTime.UtcNow,
                ProfileVersion = 1
            };
            await _projectRepo.SaveAsync(project, ct);
            _logger.LogInformation("Novo projeto criado com ID {ProjectId} ('{Name}') em {RootPath}", project.ProjectId, project.Name, project.RootPath);
        }
        else
        {
            project = existing;
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                project.Name = projectName.Trim();
            }
            project.LastModifiedAtUtc = DateTime.UtcNow;
            await _projectRepo.SaveAsync(project, ct);
            _logger.LogInformation("Projeto existente aberto com ID {ProjectId} ('{Name}')", project.ProjectId, project.Name);
        }

        var mediaList = await _mediaRepo.ListByProjectAsync(project.ProjectId, ct);

        // Descoberta inicial de mídias brutas se o banco ainda não as tiver registradas.
        // Usa a mesma expansão segura da importação: não segue reparse points, ocultos nem arquivos *.tcpart.
        if (mediaList.Count == 0)
        {
            string brutoPath = Path.Combine(normalizedPath, ImportPathRules.BrutoFolder);
            if (Directory.Exists(brutoPath))
            {
                try
                {
                    var expansion = MediaFileExpander.Expand(new[] { brutoPath }, int.MaxValue, ct);
                    var newAssets = new List<MediaAsset>();
                    foreach (var file in expansion.Files)
                    {
                        string relative = ImportPathRules.ToLibraryRelativePath(normalizedPath, file.FullPath);
                        if (!ImportPathRules.IsSafeLibraryRelativePath(relative))
                        {
                            continue;
                        }

                        var fi = new FileInfo(file.FullPath);
                        var asset = new MediaAsset
                        {
                            MediaId = Guid.NewGuid(),
                            ProjectId = project.ProjectId,
                            RelativePath = relative,
                            ByteLength = fi.Length,
                            LastWriteUtc = fi.LastWriteTimeUtc,
                            DiscoveredAtUtc = DateTime.UtcNow,
                            Kind = file.Kind,
                            // Sem metadados reais: nenhum valor estimado. Análise (ffprobe) pendente.
                            Metadata = new MediaMetadata
                            {
                                Codec = "unknown",
                                Width = 0,
                                Height = 0,
                                FrameRate = 0.0,
                                DurationSeconds = 0.0,
                                DurationTicks = 0,
                                TimeBase = new RationalTimeBase(0, 0),
                                MetadataStatus = MediaMetadataStatus.NotRequested
                            }
                        };
                        asset.Metadata.MediaId = asset.MediaId;
                        newAssets.Add(asset);
                    }

                    if (newAssets.Count > 0)
                    {
                        await _mediaRepo.SaveBatchAsync(newAssets, ct);
                        mediaList = await _mediaRepo.ListByProjectAsync(project.ProjectId, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Não foi possível verificar automaticamente os arquivos em 1-Bruto de {Path}", brutoPath);
                }
            }
        }

        var suggestions = await _planRepo.ListSuggestionsByProjectAsync(project.ProjectId, ct);
        var decisions = await _planRepo.ListDecisionsByProjectAsync(project.ProjectId, ct);

        var summary = ProjectSummaryBuilder.Build(project, mediaList, suggestions, decisions);
        return new OpenProjectOutcome(true, summary, null, null, createdFolders, isNewProject);
    }
}
