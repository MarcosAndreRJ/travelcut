# Prompt 02 — Robustez do pipeline (P06–P12, P14, P15)

Referência: tabela de achados em `../workstreams/F14.8-AUTOMATED-PREEDITING.md`. Executor: Sonnet A (dotnet-engineer); Sonnet B só para o worker Python (P08/P10).

Itens, um commit cada, com teste xUnit:
- **P06** — cancelamento no meio da mídia: gravar o cache de análise a cada etapa concluída (ou em `finally` sem token de cancelamento), para `analysis.getMedia` refletir a transcrição já persistida.
- **P07** — retomada: ao abrir o projeto, calcular mídias sem transcrição/análise válidas e enfileirar pelo mesmo `PipelineJobManager`/orquestrador (política explícita e testada; limpar `.tcpart` órfão). Persistir a fila de lotes pendentes e o estado de `postAnalysis` (hoje só em memória).
- **P08** — mídia sem trilha de áudio (`AudioChannels==0`) → `skipped("sem áudio")`, sem iniciar Python.
- **P09** — `TranscriptValidator` exige `audio_duration>0` e compara com a duração dos metadados; divergência grande → `completed_with_issues` e não cachear; registrar `hasSpeech=false` explicitamente quando válido.
- **P10** — carregar o modelo Whisper uma vez por job (worker persistente ou `transcribe_batch`); teste: 3 mídias → 1 `load_model`.
- **P11** — arquivo alterado (fingerprint novo) → marcar análise `stale`, reenfileirar e invalidar o plano dependente.
- **P12** — retry automático 1× em falha transitória e fallback CPU/modelo menor em OOM de CUDA; lista de mídias com motivo + `retryFailed`.
- **P14** — notificação global do fim do pipeline/pós-análise (um canal ou polling no shell) reutilizando a infraestrutura de notificações e sons; reimportar mídia já analisada não cria job vazio.
- **P15** — evicção de `_jobs`, descarte de `Cts`, versão do engine/worker na chave de cache, remover a constante morta `SpeechUnavailableNote`.

Pronto quando: suíte .NET completa verde; relatório com comandos exatos. Não inventar percentuais de progresso.
