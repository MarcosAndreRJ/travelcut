# Prompt 06 — Pendências menores

Executor: Haiku (frontend, MCP, docs); Sonnet A quando tocar backend.

- **`tsc`**: 12 erros pré-existentes em `TranscriptPanel.test.tsx` (tipos `TranscriptWordDto`, `HesitationCandidateDto`) e `PlanTimelineCanvas.tsx` (`ROW_HEIGHT_PX`, prop `color` de `TimelineWaveformProps`). Confirmar se vêm da main e coordenar com a F14.7.
- **UI do perfil**: seleção Conservador/Equilibrado/Agressivo e do `AutoProcess` nas preferências do projeto, usando `automation.profile.*` e `automation.options.*`. Trocar o perfil deixa a revisão `PLAN_OUTDATED` até novo `plan.generate`: mostrar isso e oferecer regenerar.
- **MCP**: ferramenta local para ler/definir o perfil (`mcp-engineer`, SDK C# oficial).
- **E10**: a guarda de ênfase só atua com palavra depois do par; "não não" no fim da transcrição cai em `repetition`. Cobrir com teste e corrigir.
- **Teste instável**: `OrchestratorThatThrows_DoesNotChangeAnalysisState` falhou uma vez; rodar 20× e eliminar a dependência de timing.
- **`postAnalysis`** é lido uma vez no estado terminal: acompanhar (polling ou evento) enquanto `running`; tratar a janela `idle` entre o fim do pipeline e o orquestrador.
- **Importação**: `ImportProgressOverlay` com aviso de CPU lenta; fila só em memória (ver prompt 02).
- **Atribuição**: os commits do I5 e do I2c usam o trailer `Claude Haiku 5.5`; decidir se padroniza.
- Atualizar `progresso/IMPLEMENTATION_LOG.md` a cada entrega.
