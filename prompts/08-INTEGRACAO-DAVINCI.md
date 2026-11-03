# FASE 08 — Bridge DaVinci com adaptador mock/real e pacote host


**Objetivo:** construir integração sem DaVinci na VM, produzindo código e scripts de validação real para host.

### Atividades

1. Implementar `IEditorAdapter` e `MockResolveAdapter` com capability matrix; simular criar projeto, importar mídia, criar timeline, aplicar plano, salvar/snapshot, exportar. Gravar resultado `SIMULATED`, sem `.drp` fake.
2. Implementar `DaVinciResolveAdapter`/bridge Python isolada, validação de versão/capacidades API/MCP documentadas, erro real para operação não suportada; especial cuidado com frame/timebase e mídia no host.
3. Criar harness de integração com projeto sintético novo e mídia sintética; gerar `docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md` detalhando pré-condições, comandos, resultados, reversão e logs para testar no host.
4. Nunca mexer no Resolve da VM. Testes unit/contract/mock na VM. Entregar build para host quando executável; marcar `HOST_PENDENTE` até o usuário retornar evidências.
5. Se operação não é suportada, oferecer fallback seguro (plano/arquivo de importação, quando confirmado), sem prometer zoom/transição avançados.

### Aceite

Mock passa testes de contrato, bridge valida indisponibilidade, capabilities e erros, pacote host pronto. **A homologação real não pode ser declarada completa nesta fase sem teste manual no host.** Prosseguir com tarefas independentes, mantendo gate host aberto.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/09-MCP-CLAUDE-E-HYPERFRAMES.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
