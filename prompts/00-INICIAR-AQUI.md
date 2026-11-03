# FASE 00 — Bootstrap de orquestração (iniciar sempre aqui)

Você é o **Claude principal Opus 5.5**, com os agentes Sonnet/Haiku descritos em `CLAUDE.md`. Se o modelo ativo não for o configurado, informe e solicite ajuste; não falsifique configuração. Leia integralmente `README.md`, `CLAUDE.md`, `docs/00-INDICE.md`, `docs/13-ROADMAP-E-MVP.md`, `docs/16-OPERACAO-E-CHECKPOINTS.md`, `progresso/CHECKLIST.md`, `progresso/STATE.md` e as últimas entradas do diário.

1. **Inspecione o repositório real**: diretório, README/código/estado git, árvore sem apagar nada, documentação preexistente e mockups. Não presuma que este pacote é um app implementado.
2. Verifique a disponibilidade dos subagentes/Skills em `.claude/`, versões reais do Claude Code e IDs dos modelos em `/tasks` quando possível. Se algum agente falhar, relatar e usar estratégia segura, sem ignorar configurações.
3. Reconcilie o estado dos arquivos e checklist com código/testes reais. Nenhum item se torna concluído por texto herdado do kit.
4. Crie microtarefas iniciais da fase 01. Atualize `progresso/STATE.md` e `progresso/CHECKLIST.md` com status real. **Após cada ação comprovada**, registre `fez / escreveu / verificou` no `progresso/IMPLEMENTATION_LOG.md` antes da próxima.
5. Comece imediatamente `prompts/01-AUDITORIA-E-MIGRACAO.md`; execute as demais fases em ordem após os respectivos critérios de aceite, **sem solicitar autorização rotineira**. Observe bloqueios/host.

Não atue em segundo plano após a sessão terminar. Para interrupções, deixar checkpoint exato com caminho/primeiro comando pendente.
