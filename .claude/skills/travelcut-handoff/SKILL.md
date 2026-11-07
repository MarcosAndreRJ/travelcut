---
name: travelcut-handoff
description: "Padroniza delegacoes hierarquicas aos agentes e integracao sem conflitos no repositorio."
---
# Procedimento de Delegacao e Handoff

## Hierarquia de Agentes
1. **Nivel 1 — Orquestrador Principal (Sessao Principal - Claude Fable 5.1):** Recebe solicitacoes do usuario, planeja incrementos delimitados, delega com contexto minimo, avalia relatorios e encerra no limite autorizado. Nao delega tarefas triviais onde a delegacao custaria mais que a execucao direta.
2. **Nivel 2 — Expert Arquitetural (Claude Opus 5.5 - senior-analyst):** Pareceres tecnicos objetivos para problemas excepcionais, falhas entre subsistemas, concorrencia critica, integridade de dados/midia e decisoes irreversiveis. Nao executa tarefas rotineiras.
3. **Nivel 3 — Especialistas Sonnet 5.5:**
   - **dotnet-engineer (Sonnet A):** Integracao e Backend (.NET 10, WPF, WebView2, IPC, SQLite/EF Core, Jobs, autorizacao nativa).
   - **media-engineer (Sonnet B):** Engenharia Audiovisual (Whisper/Python, CUDA, FFmpeg/FFprobe, codecs, proxies, timestamps, VFR).
4. **Nivel 4 — Executores Haiku 5.5:** frontend-engineer, mcp-engineer, davinci-integrator, qa-auditor (somente leitura), documentation-engineer. Tarefas delimitadas, componentes isolados, testes e ajustes pontuais.

## Regras de Delegacao (Fable -> Agentes)
Ao delegar, enviar exclusivamente:
- `taskId` e objetivo delimitado.
- Paths permitidos e paths proibidos.
- Entradas, saidas e criterios de aceite objetivos.
- Contexto minimo indispensavel (sem reproduzir arquivos inteiros).

## Regras de Retorno (Agentes -> Fable)
No relatorio compacto de retorno exigir:
- Status objetivo (concluido, parcial ou bloqueado).
- Resumo conciso da intervencao.
- Arquivos modificados/criados.
- Testes executados (comando exato, aprovado/falha/nao rodado).
- Riscos, pendencias e contratos afetados.

## Governanca de Concorrencia
- No maximo 2 subagentes ativos simultaneamente.
- Nenhum par de agentes pode editar o mesmo arquivo ao mesmo tempo.
- Nao executar builds pesados concorrentes que disputem recursos da maquina.
