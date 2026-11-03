# FASE 11 — Homologação no host, correções e fechamento do MVP


**Objetivo:** atingir um MVP **real**, não apenas simulado, usando o DaVinci do host e uma viagem de teste aprovada.

### Atividades

1. Ler `progresso/HOST_TESTS.md` e roteiro gerado. Preparar instruções claras para o usuário instalar/executar no host. **Não afirmar acesso ao host a partir da VM.**
2. Solicitar ao usuário resultados reais de Windows/DaVinci/Whisper/FFmpeg/HyperFrames, logs e capturas (somente arquivos necessários); marcar `waiting_for_host` enquanto não houver evidência. Não continuar trabalho em segundo plano.
3. Ao receber falhas reais, reproduzir com fixtures na VM, corrigir, executar regressão e entregar nova versão; iterar até critérios de aceite.
4. Validar: timeline editável no Resolve com vários clipes, áudio alinhado, projeto reabre, report de cortes, dados verdadeiros, original intacto, entrega aprovado→4-Final; UI e MCP coerentes.
5. Finalizar documentação da versão, limitações conhecidas, requisitos host e release; QA Opus/Sonnet. `HOST_VALIDADO` apenas com evidência real.

### Aceite

MVP funcional só se testes host e critérios em `docs/13` passaram. Caso contrário, entregar status **MVP SIMULADO / HOST PENDENTE** com relatório e próximos passos concretos.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** `o fechamento do MVP documentado` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
