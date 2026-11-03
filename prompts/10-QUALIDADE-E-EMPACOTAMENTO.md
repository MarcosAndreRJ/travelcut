# FASE 10 — QA end-to-end, build distribuível e documentação de versão


**Objetivo:** estabilizar e produzir um build para testar no host.

### Atividades

1. Integrar pipeline ponta a ponta com mídias sintéticas: projeto→inventário→transcrição fixture/real→candidatos→revisão→EditPlan→MockResolve; testar reinício, cancelamento, cache e migração.
2. Corrigir falhas de segurança, paths, IO, consistência SQLite, Unicode, VFR, WebView2, player e estados. QA em todos os módulos com Sonnet na auditoria crítica.
3. Build Release .NET x64 + bundle React; empacotar WPF e dependências necessárias com manifesto, hash, instruções de runtime. Não embutir mídias nem pesos large-v3 indevidamente.
4. Teste de compatibilidade/instalação Windows host e caminho compartilhado VM→host; se host não acessível, preparar roteiro manual e pacote no local esperado, registrar pendência.
5. Atualizar README de usuário, notas de versão, checklist de smoke test e lista de limitações e riscos; commits locais pequenos quando autorizado.

### Aceite

Build reproduzível, suíte automatizada sem falhas bloqueantes, UI sem mock falso, pacote de teste com manifesto e README, pendências host explicitadas.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/11-HOMOLOGACAO-E-MVP.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
