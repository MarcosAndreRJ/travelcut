# FASE 07 — Revisão de cortes, player e timeline planejada


**Objetivo:** transformar o plano em experiência visual interativa, sem tentar implementar um NLE concorrente.

### Atividades

1. Implementar fila de sugestões, filtro por categoria/confiança, before/after via proxies/preview quando viável, waveform e destaques de transcrição no intervalo de origem.
2. Aprovar/rejeitar/adiar/modificar cortes, persistir decisões com versão e vínculo ao clip/MediaID; suporte undo/redo de decisão, snapshots antes de alterar plano.
3. Construir timeline de planejamento em tracks V1/V2/A1/A2 com scroll/zoom, selection, scrub e conversão de timecodes; exibir quando visualização é apenas aproximada.
4. Player de prévia: avaliar LibVLCSharp/WPF vs WebView2; teste de codecs, seeks, sincronização e overlay. Não declarar frame-accurate sem medição. Fallback de FFmpeg para proxies.
5. Integrar conflitos de atualização, mídia ausente, mudanças de plano e importações. QA antes/depois, decisões de humor preservadas e testes de UI.

### Aceite

Cada decisão altera plano persistido, estado da revisão sobrevive restart, undo funcional, preview real para fixtures, timeline consistente com plano, testes temporais e UI.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/08-INTEGRACAO-DAVINCI.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
