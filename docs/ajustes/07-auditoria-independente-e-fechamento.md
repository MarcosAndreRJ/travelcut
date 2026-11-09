# Prompt 07 — Auditoria independente e fechamento da F14.8

Executar só depois dos prompts 01–06 (o 04 se a F14.7 estiver integrada).

1. **Auditoria independente**: `qa-auditor` (Haiku, somente leitura) + um Sonnet que não implementou o componente; Opus `senior-analyst` só para integridade de dados e divergência preview × plano. Classificar achados BLOQUEANTE/ALTA/MÉDIA/BAIXA; bloqueantes e altos de integridade, perda de dados ou divergência preview × plano impedem "concluído".
2. **Testes finais**: `dotnet test tests/TravelCut.UnitTests`, `tests/TravelCut.Mcp.Tests`, vitest completo, `tsc --noEmit`, build de produção do frontend, build WPF Release. Não usar binários antigos.
3. **Métricas reais**: candidatos, aplicados, pendentes, restaurados, tempo removido, efeitos, conflitos.
4. **Documentação**: `docs/workstreams/F14.8-AUTOMATED-PREEDITING.md` (mapa da arquitetura, achados, correções, política, testes, limitações, riscos, evidências, próximas melhorias) e `progresso/`.
5. **Relatório final F14.8** (seções 1–17 do prompt original), separando `mock_pass`, `integration_pass`, `host_pending` e `host_pass`. Não declarar homologação Windows sem evidência real. Não sobrescrever pacote em `/dist`.
6. **Git**: árvore limpa, revisar o diff, commits por incremento, push da branch `feat/f14.8-automated-preediting` e confirmar o SHA remoto. Não fazer merge na main sem autorização.

Encerrar após o relatório; não iniciar outra fase.
