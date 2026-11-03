# FASE 09 — MCP TravelCut e HyperFrames opcional


**Objetivo:** permitir que Claude Code use o mesmo Application Core e incorporar HyperFrames apenas quando necessário para animações.

### Atividades

1. Implementar MCP local em C# com SDK oficial; ferramentas `get_inventory`, `get_transcript`, `get_suggestions`, `get_edit_plan`, `job_status`, `get_editor_capabilities`; depois mutações `submit_review`, `queue_analysis`, `apply_plan` com validação/idempotência e controle de versão.
2. Configurar instalação/documentação MCP por projeto; não sobrescrever MCP do DaVinci existente nem configurações globais. Testar cliente MCP de forma local, read-only primeiro, capability 'mock'.
3. Adaptar Node.js/HyperFrames para renderização isolada de gráficos/títulos usando assets autorizados e contratos; se kit não existir na VM, documentar invocação host e usar mock com outputs demonstrativos sem alegar render real.
4. Confirmar que UI e MCP acionam os mesmos casos de uso, não rotas paralelas; testar permissões, dados inválidos, concorrência e log.
5. Documentar Skill de edição em produção apenas como integração planejada, não confundir com subagentes de desenvolvimento.

### Aceite

MCP responde testes read-only, mutações guardadas, documentação de setup e testes, nenhuma exposição de shell/arquivos, integração HyperFrames declarada por capacidade real/simulada.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/10-QUALIDADE-E-EMPACOTAMENTO.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
