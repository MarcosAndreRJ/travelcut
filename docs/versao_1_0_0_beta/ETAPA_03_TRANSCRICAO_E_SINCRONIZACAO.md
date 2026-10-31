# Prompt de Desenvolvimento - Etapa 03: Transcricao e Sincronizacao

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 03 - Transcricao de Audio Real via Whisper, VAD e Sincronizacao Palavra por Palavra
- **Objetivo Central:** Conectar o pipeline de reconhecimento de voz e deteccao de atividade vocal (VAD) desenvolvido em `research/transcription/` a execucao real no aplicativo. Extrair faixas de audio dos videos em formato WAV 16kHz mono, processar com modelo Whisper local (com suporte a GPU/CPU), gerar transcricoes ricas com timestamps no nivel de palavra e sincronizar a leitura do texto com a reproducao do video no `TranscriptInspector`.
- **Resultado Esperado:** Ao selecionar um video na biblioteca e clicar em "Transcrever", o aplicativo extrai o audio, detecta as partes faladas, gera o texto com inicio e fim exatos de cada palavra pronunciada e exibe o texto no painel lateral. Clicar em qualquer palavra move a agulha do player para o instante exato da fala, e durante o play as palavras sao destacadas em tempo real.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `research/transcription/`:
  - `audio.py`: Normalizacao de audio, extracao via FFmpeg em PCM 16kHz mono e leitura de cabeçalhos WAV.
  - `vad.py`: Deteccao de atividade de voz com analise de fluxo espectral e estimativa adaptativa de piso de ruido.
  - `silence_detector.py`: Taxonomia de pausas (`PAUSE_WORD` < 400ms, `PAUSE_SENTENCE` 400-1200ms, `REMOVABLE_GAP` >= 1200ms).
  - `engine.py`: Wrapper para execucao do `faster-whisper` com modelos int8/float16 e controle de temperatura.
  - `alignment.py`: Alinhamento fino de bordas de palavras e identificacao de marcadores de hesitacao (*"eh"*, *"hum"*).
  - `contracts.py`: Dataclasses `TranscriptionDocument`, `TranscriptionSegment`, `TranscriptionWord`, `SilenceInterval`.
- `crates/travelcut-media/src/transcode/`:
  - Modulo de execucao de FFmpeg para extracao de faixas de audio em lote sem travar a interface.
- `src/features/library/components/TranscriptInspector.tsx`:
  - Interface rica com busca de termos, abas de transcricao e cortes, e estilizacao de palavras clicaveis.
- `src/features/editing/components/IntegratedTranscript.tsx`:
  - Editor textual integrado que permite marcar palavras e trechos para corte ou destaque.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** utilize textos de transcricao ficticios (como os mocks de Roraima Expedition) quando a transcricao for acionada em arquivos reais.
- **NAO** bloqueie a thread principal do React ou do Tauri durante o processamento de IA. A transcricao deve rodar em processo assincrono (sidecar Python, worker em background ou thread Rust) com notificacoes de progresso percentual via eventos IPC.
- **NAO** perca os timestamps no nivel de palavra (word-level timestamps); a transcricao apenas por blocos ou frases e insuficiente para cortes de precisao cirurgica.

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 3.1: Extracao Padronizada de Audio (WAV 16kHz Mono)
1. Crie ou integre o comando nativo no Rust (`travelcut_media` / `commands.rs`):
   ```rust
   pub async fn extract_audio_for_transcription(
       video_path: String,
       output_wav_path: String
   ) -> Result<String, String>;
   ```
   - Executa internamente o comando FFmpeg:
     `ffmpeg -y -i "{video_path}" -vn -acodec pcm_s16le -ar 16000 -ac 1 "{output_wav_path}"`
   - O arquivo gerado deve ser alocado temporariamente em `.travelcut/cache/audio/{hash}.wav`.

### Tarefa 3.2: Integracao do Motor Whisper (Sidecar ou Executavel Local)
1. Configure o backend para executar o pipeline de transcricao:
   - **Opcao Recomendada (Producao):** Executar o script `research/transcription/cli.py` como processo filho (sidecar gerenciado pelo Tauri) passando o caminho do WAV extraido e opcoes de modelo (`--model base` ou `small`, `--language pt`, `--word-timestamps true`).
   - O script deve emitir progresso formatado em stdout (ex.: `PROGRESS: 45%`) e gerar como saida um arquivo JSON estruturado aderente a `contracts.py`.
2. Em `src-tauri/src/commands.rs`, crie o comando `run_transcription`:
   ```rust
   pub async fn run_transcription(
       app_handle: tauri::AppHandle,
       audio_path: String,
       language: Option<String>,
       model_size: Option<String>
   ) -> Result<TranscriptionDocumentPayload, String>;
   ```
3. Emita eventos Tauri `transcription-progress` (`{ file_id, percent, current_segment }`) para manter a barra de progresso do `ProcessingQueue.tsx` atualizada.

### Tarefa 3.3: Salvar Transcricao no Projeto e Modelo TypeScript
1. Ao concluir a transcricao, armazene o resultado em `.travelcut/transcripts/{asset_id}.json`.
2. No TypeScript (`src/features/media/types.ts`), garanta que o formato retornado corresponda a:
   ```typescript
   export interface TranscriptSegment {
     id: string;
     startSec: number;
     endSec: number;
     text: string;
     words: TranscriptWord[];
     speakerTag?: string;
   }
   
   export interface TranscriptWord {
     id: string;
     word: string;
     startSec: number;
     endSec: number;
     confidence: number;
     isHesitation?: boolean;
     isSilence?: boolean;
   }
   ```

### Tarefa 3.4: Sincronizar o `TranscriptInspector` com a Reproducao do Video
1. Conecte o estado de tempo corrente (`currentTimeSec`) do player de video ao componente `TranscriptInspector.tsx`:
   - Encontre a palavra ativa onde `currentTimeSec >= word.startSec && currentTimeSec <= word.endSec`.
   - Aplique a classe CSS de destaque luminoso (glow/amber) na palavra em reproducao e realize scroll suave para mante-la visivel.
2. Ao clicar em qualquer palavra:
   - Dispare o callback `onSeek(word.startSec)` para mover o video diretamente para o instante em que aquela palavra comeca a ser dita.
3. Adicione o campo de busca textual no topo do inspetor:
   - Digitar uma palavra deve filtrar os trechos e permitir navegar entre as ocorrencias do termo ao longo do video.

---

## 5. Procedimentos de Teste e Validacao
Realize os seguintes testes:

1. **Teste de Extracao e Transcricao de Video Real:**
   - Selecione um video curto (30 a 60 segundos) com fala clara em portugues.
   - Clique em "Transcrever Clipe".
   - Verifique se a barra de processamento avanca de 0% a 100%.
   - Inspecione se o texto exibido no `TranscriptInspector` reproduz fielmente as falas do video.

2. **Teste de Sincronismo Bidirecional (Click-to-Seek):**
   - No inspetor de texto, localize uma palavra no meio do texto e clique nela.
   - O elemento de video deve saltar imediatamente para o ponto exato da palavra.
   - De play no video e observe as palavras sendo destacadas no ritmo da fala humana.

3. **Inspecao do Arquivo Gerado:**
   - Verifique se o arquivo JSON de transcricao foi gravado em `.travelcut/transcripts/` contendo todas as palavras com seus timestamps (`startSec`, `endSec`, `confidence`).

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] O comando de extracao de audio gera um arquivo WAV 16kHz mono valido sem corrupcao de cabecalho.
- [ ] O motor Whisper processa o audio e retorna transcricao real com timestamps por palavra.
- [ ] O progresso da transcricao e emitido via eventos e exibido de forma fluida na interface.
- [ ] Clicar em qualquer palavra no `TranscriptInspector` reposiciona a agulha do video no milissegundo correto.
- [ ] A palavra correspondente e iluminada visualmente durante a reproducao do video.
- [ ] Os dados de transcricao sao persistidos e recarregados automaticamente ao reabrir o projeto.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da:
`ETAPA_04_CLASSIFICACAO_AUDIOVISUAL.md`.
