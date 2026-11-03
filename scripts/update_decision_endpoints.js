const fs = require('fs');

// 1. Atualizar MockEditPlanRepository em IpcDispatcherTests.cs
let ipcTests = fs.readFileSync('tests/TravelCut.UnitTests/Application/IpcDispatcherTests.cs', 'utf8');
const oldMockRepo = `        public Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default)
        {
            SavedDecisions.Add(decision);
            return Task.CompletedTask;
        }
        public Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(SavedDecisions);`;

const newMockRepo = `        public Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default)
        {
            SavedDecisions.RemoveAll(d => d.DecisionId == decision.DecisionId);
            SavedDecisions.Add(decision);
            return Task.CompletedTask;
        }
        public Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(SavedDecisions);
        public Task<EditDecision?> GetLatestDecisionBySuggestionAsync(Guid suggestionId, CancellationToken ct = default)
        {
            var item = SavedDecisions.Where(d => d.SuggestionId == suggestionId).OrderByDescending(d => d.TimestampUtc).FirstOrDefault();
            return Task.FromResult(item);
        }
        public Task DeleteDecisionsBySuggestionIdAsync(Guid suggestionId, CancellationToken ct = default)
        {
            SavedDecisions.RemoveAll(d => d.SuggestionId == suggestionId);
            return Task.CompletedTask;
        }`;

if (ipcTests.includes(oldMockRepo)) {
    ipcTests = ipcTests.replace(oldMockRepo, newMockRepo);
    fs.writeFileSync('tests/TravelCut.UnitTests/Application/IpcDispatcherTests.cs', ipcTests, 'utf8');
    console.log("MockEditPlanRepository em IpcDispatcherTests.cs atualizado.");
} else {
    console.log("Aviso: oldMockRepo não encontrado em IpcDispatcherTests.cs.");
}

// 2. Atualizar InMemoryEditPlanRepository em OpenOrCreateProjectUseCaseTests.cs
let openTests = fs.readFileSync('tests/TravelCut.UnitTests/Application/OpenOrCreateProjectUseCaseTests.cs', 'utf8');
const oldInMemoryRepo = `        public Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(new List<EditDecision>());`;

const newInMemoryRepo = `        public Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(new List<EditDecision>());
        public Task<EditDecision?> GetLatestDecisionBySuggestionAsync(Guid suggestionId, CancellationToken ct = default) => Task.FromResult<EditDecision?>(null);
        public Task DeleteDecisionsBySuggestionIdAsync(Guid suggestionId, CancellationToken ct = default) => Task.CompletedTask;`;

if (openTests.includes(oldInMemoryRepo)) {
    openTests = openTests.replace(oldInMemoryRepo, newInMemoryRepo);
    fs.writeFileSync('tests/TravelCut.UnitTests/Application/OpenOrCreateProjectUseCaseTests.cs', openTests, 'utf8');
    console.log("InMemoryEditPlanRepository em OpenOrCreateProjectUseCaseTests.cs atualizado.");
} else {
    console.log("Aviso: oldInMemoryRepo não encontrado em OpenOrCreateProjectUseCaseTests.cs.");
}

// 3. Atualizar IpcDispatcher.cs
let dispatcher = fs.readFileSync('src/TravelCut.Application/Ipc/IpcDispatcher.cs', 'utf8');
const oldDecisionBlock = `                case "decision.submit":
                {
                    var req = DeserializePayload<SubmitDecisionRequest>(request.Payload);
                    if (req == null)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Dados de decisão inválidos.");
                    }

                    var decision = new Domain.Entities.EditDecision
                    {
                        DecisionId = Guid.NewGuid(),
                        SuggestionId = req.SuggestionId,
                        Action = req.Action,
                        Reviewer = req.Reviewer,
                        Notes = req.Notes ?? string.Empty,
                        TimestampUtc = DateTime.UtcNow,
                        Version = 1
                    };
                    await _planRepo.SaveDecisionAsync(decision, ct);
                    return IpcResponse.Success(request.RequestId, new { decisionId = decision.DecisionId, status = "recorded" });
                }`;

const newDecisionBlock = `                case "decision.submit":
                {
                    var req = DeserializePayload<SubmitDecisionRequest>(request.Payload);
                    if (req == null || req.SuggestionId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Dados de decisão inválidos.");
                    }

                    var previous = await _planRepo.GetLatestDecisionBySuggestionAsync(req.SuggestionId, ct);
                    var decision = new Domain.Entities.EditDecision
                    {
                        DecisionId = Guid.NewGuid(),
                        SuggestionId = req.SuggestionId,
                        Action = req.Action,
                        Reviewer = req.Reviewer,
                        Notes = req.Notes ?? string.Empty,
                        TimestampUtc = DateTime.UtcNow,
                        Version = previous != null ? previous.Version + 1 : 1
                    };
                    await _planRepo.SaveDecisionAsync(decision, ct);
                    return IpcResponse.Success(request.RequestId, new
                    {
                        decisionId = decision.DecisionId,
                        suggestionId = decision.SuggestionId,
                        action = decision.Action,
                        version = decision.Version,
                        status = "recorded"
                    });
                }
                case "decision.undo":
                {
                    var req = DeserializePayload<UndoDecisionRequest>(request.Payload);
                    if (req == null || req.SuggestionId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_UNDO", "ID de sugestão inválido para desfazer decisão.");
                    }
                    var latest = await _planRepo.GetLatestDecisionBySuggestionAsync(req.SuggestionId, ct);
                    if (latest == null)
                    {
                        return IpcResponse.Fail(request.RequestId, "NO_DECISION_TO_UNDO", "Nenhuma decisão encontrada para esta sugestão.");
                    }
                    await _planRepo.DeleteDecisionsBySuggestionIdAsync(req.SuggestionId, ct);
                    return IpcResponse.Success(request.RequestId, new
                    {
                        undone = true,
                        suggestionId = req.SuggestionId,
                        previousAction = latest.Action
                    });
                }
                case "decision.list":
                {
                    var projectId = ExtractProjectId(request.Payload);
                    if (projectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }
                    var decisions = await _planRepo.ListDecisionsByProjectAsync(projectId, ct);
                    var dtos = decisions.Select(d => new EditDecisionDto(
                        d.DecisionId,
                        d.SuggestionId,
                        d.Action,
                        d.Reviewer,
                        d.Notes,
                        d.TimestampUtc,
                        d.Version
                    )).ToList();
                    return IpcResponse.Success(request.RequestId, new { decisions = dtos });
                }
                case "suggestions.batchDecide":
                {
                    var req = DeserializePayload<BatchDecideRequest>(request.Payload);
                    if (req == null || req.SuggestionIds == null || req.SuggestionIds.Count == 0)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_BATCH", "Nenhuma sugestão informada para decisão em lote.");
                    }
                    int count = 0;
                    foreach (var suggestionId in req.SuggestionIds)
                    {
                        if (suggestionId == Guid.Empty) continue;
                        var prev = await _planRepo.GetLatestDecisionBySuggestionAsync(suggestionId, ct);
                        var decision = new Domain.Entities.EditDecision
                        {
                            DecisionId = Guid.NewGuid(),
                            SuggestionId = suggestionId,
                            Action = req.Action,
                            Reviewer = req.Reviewer,
                            Notes = req.Notes ?? string.Empty,
                            TimestampUtc = DateTime.UtcNow,
                            Version = prev != null ? prev.Version + 1 : 1
                        };
                        await _planRepo.SaveDecisionAsync(decision, ct);
                        count++;
                    }
                    return IpcResponse.Success(request.RequestId, new { recordedCount = count, action = req.Action });
                }`;

if (dispatcher.includes(oldDecisionBlock)) {
    dispatcher = dispatcher.replace(oldDecisionBlock, newDecisionBlock);
    fs.writeFileSync('src/TravelCut.Application/Ipc/IpcDispatcher.cs', dispatcher, 'utf8');
    console.log("IpcDispatcher.cs atualizado com sucesso.");
} else {
    console.log("Aviso: oldDecisionBlock não encontrado em IpcDispatcher.cs.");
}
