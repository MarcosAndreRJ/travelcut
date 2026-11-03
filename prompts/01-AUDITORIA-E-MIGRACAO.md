# FASE 01 — Auditoria do repositório e migração arquitetural Rust/Tauri → .NET


**Objetivo:** inspecionar o código já existente, identificar módulos Rust/Tauri e documentos reaproveitáveis, migrar a especificação técnica para .NET sem excluir trabalho útil prematuramente.

### Atividades

1. Ler todos os `docs/` existentes e comparar com o kit; listar duplicações/decisões conflitantes, preservando decisões do usuário. Gerar relatório em `docs/17-AUDITORIA-LEGADO-E-MIGRACAO.md`.
2. Inventariar arquivos e git (branches, status, commits, sem cleanup destrutivo). Classificar componentes como reutilizável, a portar, obsoleto apenas após teste, desconhecido. **Não apagar Rust/Tauri nesta fase**.
3. Identificar VM real Windows/build, dotnet SDK, workloads/WPF, Node, VS/MSBuild, WebView2 e espaço C/K/Z. .NET 10 no Windows 10 Pro 22H2: **teste experimental, não oficialmente suportado**. Se a compilação for impossível, documentar solução e bloquear a etapa dependente.
4. Definir plano de portas por contratos, ordem de migração, matriz de dependências, dados/artefatos que devem ser preservados e fallback. Solicitar Sonnet para revisar riscos de migração e versionamento.
5. Validar IDs e arquivos de agentes; nenhum conflito de permissões ou Skills de Rust antigos carregados como ativos. Preparar plano de limpeza posterior sem executar.

### Aceite

Relatório e mapeamento de legado completos, migração sem perda de dados, estado de SDK e risco registrado, plano aprovado pelo Opus, QA sem falha crítica. Se faltam ferramentas: instalar **apenas com autorização para mudanças globais** ou marcar bloqueio real e preparar instruções.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/02-FUNDACAO-DOTNET.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
