---
name: travelcut-media
description: "Procedimentos técnicos seguros para FFprobe/FFmpeg, Whisper, scene detection, VFR, cache e áudio."
---

# Procedimento


1. Ler `docs/06-PIPELINE-AUDIOVISUAL.md` e `docs/07-REGRAS-EDITORIAIS.md`.
2. Originais em 1-Bruto/2-Assets somente leitura; saída em 3-Trabalho. Paths seguros, streams grandes, spawn sem shell, timeouts/cancelamento.
3. Diferenciar metadata creationTime, modificação, timezone; não inventar cronologia.
4. Whisper **large-v3** local, se instalado; `text,audio_duration_secs,words[]` com `text,start,end`; sem inventar timestamps; guardar modelo/versão.
5. VFR/PTS/timebase: source vs timeline. Silêncios expressivos e piadas não são automaticamente descartados.
6. Na VM 4 vCPUs/sem CUDA, usar fixtures curtas; registrar testes CPU e host pendentes.
