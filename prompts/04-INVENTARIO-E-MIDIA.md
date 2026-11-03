# FASE 04 — Inventário real, FFprobe, cronologia e cache incremental


**Objetivo:** substituir a biblioteca demo por dados reais sem alterar mídias originais.

### Atividades

1. Criar serviço de escolha de pasta e reconhecimento de `1-Bruto`, `2-Assets`, `3-Trabalho`, `4-Final`; validar path, symlink/junction, ACL e raízes, sem criar/escrever em originais.
2. Implementar varredura, MediaAsset, FFprobe com timeouts e parser JSON, metadados de gravação com proveniência/fuso, detecção de origem por aparelho quando possível, SHA-256 incremental e comparação.
3. Gerar thumbnails e waveforms pequenos em Trabalho usando FFmpeg, cache com key hash+versão, jobs persistentes com progresso por quantidade real, cancelamento e retentativa idempotente.
4. Regras de cronologia: creation metadata de câmera, referência temporal na mídia quando disponível, fallback de FS marcado explicitamente incerto; UI permite corrigir ordenação.
5. Integrar à biblioteca React; testes com arquivos sintéticos, nomes Unicode, ausência de áudio, mídia corrompida, cache hit e invalidation.

### Aceite

Dados reais visíveis, 1-Bruto/2-Assets intocados e protegidos por teste, reabertura mantém cache, erros úteis e cobertura de tests. VM sem FFmpeg → registrar bloqueio e solicitar instalação autorizada; não inventar resultados.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/05-TRANSCRICAO-E-ANALISE.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
