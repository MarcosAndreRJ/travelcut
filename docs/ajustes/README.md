# Ajustes pós-F14.8 — prompts para finalizar implementações e correções

Base: branch `feat/f14.8-automated-preediting` (I1, I2, I4, I5 entregues; ver `../workstreams/F14.8-AUTOMATED-PREEDITING.md`).

Regras comuns a todos os prompts: seguir `CLAUDE.md` (Fable orquestra; Sonnet A/B; Haiku; máx. 2 agentes simultâneos; sem push sem autorização; nunca tocar mídia original; registrar em `progresso/IMPLEMENTATION_LOG.md`; Git Bash, sem cmdlets PowerShell). Cada subagente tem cerca de 20 turnos: pedir commit por item e relatório compacto.

| Ordem | Arquivo | Assunto | Pré-requisito |
|---|---|---|---|
| 1 | `01-transicoes-no-plano.md` | Fade/dissolve chegarem ao plano (E05) | — |
| 2 | `02-robustez-pipeline.md` | P06–P12, P14, P15 | — |
| 3 | `03-polimento-preview-e-revisao.md` | Limites do I5 | — |
| 4 | `04-edicao-manual-vs-automacao.md` | E01–E03, E11 (timeline) | **F14.7 integrada na main** |
| 5 | `05-cenario-de-aceite.md` | Importar → Processar → Pré-editar → Reproduzir → Desfazer | 1–3 (4 desejável) |
| 6 | `06-pendencias-menores.md` | tsc, MCP/UI do perfil, E10 borda, teste instável, docs | — |
| 7 | `07-auditoria-independente-e-fechamento.md` | Reauditoria, métricas, relatório final, push | 1–6 |

Estado verificado até aqui: `dotnet test tests/TravelCut.UnitTests` 1301/1301; vitest `components/review` 56/56; `tsc` só com erros pré-existentes. Homologação host: `host_pending`.
