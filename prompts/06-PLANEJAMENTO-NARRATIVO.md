# FASE 06 — Plano editorial reversível, estilo de viagem e sugestões


**Objetivo:** reunir análises em EditPlan com começo/meio/fim coerentes e sugestões de cortes, sem mutação no DaVinci.

### Atividades

1. Construir `EditPlan`, cenas, decisões, tracks e transições como dados imutáveis/versionados; timebase racional, timestamps origem/timeline separados, suporte a VFR e conversão determinística.
2. Serviço de planejamento usa cronologia + classificação + fala + regras em `docs/07`; sugestões motivadas: corte de hesitação, tomar versão corrigida, B-roll 2×/4×, áudio/música, zoom suave, piadas preservadas.
3. Salvar evidências de cada decisão; confidence qualitativa (não porcentagem fictícia), `needsHumanReview`, perfis versionados. Gerar plano validado JSON e preview técnico simples quando viável.
4. Para decisões criativas complexas, chamar arquitetura de assistente Claude via contratos futuros, sem acoplar regras básicas a sessão online; testes determinísticos com fixture de viagem.
5. Verificar planos inválidos, sobreposição inadequada, clips fora da mídia, VFR, aceleração sobre fala e conflito de versões. Solicitar Sonnet para pontos temporais difíceis.

### Aceite

Plano reproduzível, reversível, evidências de origem, testes de frame/timebase e narrativa, sem alterações nos originais ou DaVinci.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/07-REVISAO-E-TIMELINE.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
