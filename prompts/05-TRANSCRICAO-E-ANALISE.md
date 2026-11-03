# FASE 05 — Whisper large-v3, silêncio, classificação e candidatos a cortes


**Objetivo:** disponibilizar análise audiovisual estruturada, sem executar cortes definitivos.

### Atividades

1. Implementar worker Python com protocolo JSON versionado, ambiente virtual isolado e integração ao gerenciador de jobs. Utilizar Whisper `large-v3` existente onde disponível; validar instalação e recurso real, **nunca substituir modelo sem autorização**.
2. Normalizar `text`, `audio_duration_secs`, `words` com `text,start,end`, preservar mídia/ids e granularidade. Testar com fixture pequena; na VM sem CUDA evitar processar vídeos longos, registrar `host_pending` para validação pesada.
3. Implementar detecção de silêncio com FFmpeg/VAD, candidatos para hesitação e repetição, contexto antes/depois, timestamps e níveis qualitativos de confiança; erro/repetição não é remoção automática de brincadeira.
4. Classificar cenas preliminares (conversa, estrada, paisagem, loja, drone, outro) com heurísticas/frames e indicadores de incerteza; nenhum modelo multimodal deve ser presumido local.
5. Conectar análise e transcrição às telas; busca e salto no player, fila de jobs, cancelamento/restart, cache por versão do modelo.

### Aceite

Worker executa em caso de teste disponível, schema e timestamps válidos, candidates com evidência e revisão, testes de falta de modelo/áudio e sem alucinar transcrições. QA separa teste sintético de host large-v3.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/06-PLANEJAMENTO-NARRATIVO.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
