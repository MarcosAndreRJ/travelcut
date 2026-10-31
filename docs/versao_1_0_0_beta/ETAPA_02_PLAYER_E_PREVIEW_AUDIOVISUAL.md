# Prompt de Desenvolvimento - Etapa 02: Player de Video e Preview Audiovisual

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 02 - Player de Video Nativo, Controles de Transporte NLE e Waveform Audiovisual
- **Objetivo Central:** Implementar a reproducao fluida, precisa e de alto desempenho de videos reais armazenados localmente no computador, sem bloqueios de seguranca do navegador ou travamentos de codec. Sincronizar os controles de transporte (Play, Pause, Seek, Timecode SMPTE, avanco/recuo de frames) e gerar visualizacoes graficas de forma de onda (waveform de picos de audio) e filmstrips de miniaturas a partir do motor nativo Rust.
- **Resultado Esperado:** Ao clicar em qualquer clipe da biblioteca ou da timeline, o usuario consegue assistir ao video com audio sincronizado dentro do TravelCut Studio, navegar suavemente pela barra de tempo, conferir o timecode em formato `HH:MM:SS:FF` e visualizar a forma de onda do audio real.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `src/features/library/components/VideoPlayerPreview.tsx`:
  - Componente de player contendo barra de transporte, botoes de reproducao, seletor de faixa de audio e marcadores de tempo. Atualmente simula timecode via intervalo em JavaScript.
- `src/features/editing/components/IntegratedVideoPlayer.tsx`:
  - Player integrado ao workspace de revisao e edicao de cortes, preparado para receber `assetId`, `currentTime` e manipular selecoes de trecho.
- `crates/travelcut-media/src/waveform/generator.rs`:
  - Gerador de forma de onda em Rust que extrai dados PCM via FFmpeg e calcula picos normalizados (min/max) com resolucao configuravel para desenho em Canvas ou SVG.
- `crates/travelcut-media/src/thumbnails/generator.rs`:
  - Extrator de miniaturas e gerador de filmstrips sequenciais em Rust para pre-visualizacao rapida no hover da linha do tempo.
- Tauri 2 Custom Asset Protocol:
  - Suporte ao protocolo `asset://` ou a funcao `convertFileSrc` de `@tauri-apps/api/core` para carregar arquivos de midia locais diretamente na tag HTML5 `<video>` com suporte a HTTP Range requests para seek instantaneo.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** utilize `setInterval` artificial para simular o progresso do video. O timecode e a posicao da agulha de reproducao devem ser derivados diretamente dos eventos nativos do elemento de video (`timeupdate`, `seeked`, `requestVideoFrameCallback`).
- **NAO** recodifique o video completo em memoria antes de tocar. Utilize o protocolo nativo de streaming de arquivo local do Tauri para permitir carregamento sob demanda.
- **NAO** implemente edicao de cortes ou transcricao aqui; foque exclusivamente na precisao e confiabilidade do motor de reproducao audiovisual e sua telemetria.

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 2.1: Habilitar o Protocolo de Ativos Locais no Tauri 2
1. No arquivo `src-tauri/tauri.conf.json`, configure a permissao de seguranca para leitura de midia via protocolo de assets:
   - Garanta que `app.security.assetProtocol.scope` permita caminhos de arquivos de video ou configure o escopo adequado para pastas de projeto.
2. Em `src/services/desktopBridge.ts`, crie a funcao utilitaria:
   ```typescript
   export function getMediaAssetUrl(absoluteFilePath: string): string;
   ```
   - Em ambiente Tauri, utilize `convertFileSrc(absoluteFilePath)`.
   - Em fallback web (caso necessario para testes mockados), retorne uma URL publica ou blob.

### Tarefa 2.2: Refatorar os Componentes de Player para o Elemento `<video>` Real
1. Em `src/features/library/components/VideoPlayerPreview.tsx` e `src/features/editing/components/IntegratedVideoPlayer.tsx`:
   - Substitua qualquer placeholder estatico pelo elemento nativo `<video ref={videoRef} src={mediaUrl} preload="metadata" />`.
   - Adicione ouvintes de eventos:
     - `onLoadedMetadata`: le a duracao exata em segundos e a resolucao nativa.
     - `onTimeUpdate`: atualiza o estado de tempo corrente no contexto sem engasgos de re-renderizacao desnecessarios.
     - `onPlay` / `onPause`: reflete o estado dos botoes de transporte.
2. Implemente controles de transporte profissionais NLE:
   - Barra espaco: alterna Play/Pause.
   - Setas Esquerda/Direita: recua/avanca 1 frame (calculado com base no framerate do clipe, ex.: `1 / fps`).
   - Teclas J / K / L (navegacao padrao de edicao): J = retroceder rapido (-2x), K = pausar (1x/0), L = reproduzir rapido (+2x).
   - Botoes de salto rapido: -5 segundos e +5 segundos.

### Tarefa 2.3: Calculo e Formatacao de Timecode SMPTE
1. Utilize a funcao utilitaria `secondsToTimecode(currentTimeSec, fps)` ja existente em `src/features/editing/utils/davinciExporter.ts`.
2. Exiba o timecode em formato profissional `HH:MM:SS:FF` na interface do player, com atualizacao precisa a cada mudanca de quadro.
3. Permita edicao direta do campo de timecode: quando o usuario digitar um timecode valido, a agulha de reproducao deve saltar imediatamente para o ponto correspondente (`video.currentTime = targetSec`).

### Tarefa 2.4: Integracao da Forma de Onda (Waveform Real)
1. Conecte o comando nativo `media_extract_waveform` via `desktopBridge.ts`:
   ```typescript
   export async function loadMediaWaveform(filePath: string): Promise<WaveformData>;
   ```
2. Ao selecionar um clipe na biblioteca, dispare a extracao da forma de onda em segundo plano (com armazenamento em cache na pasta `.travelcut/cache/waveforms/`).
3. Desenhe os picos de amplitude min/max em um elemento `<canvas>` subjacente a barra de transporte e na timeline, permitindo ao usuario identificar visualmente trechos de fala e pausas silenciosas.

### Tarefa 2.5: Miniaturas e Pre-visualizacao no Hover (Filmstrip)
1. Conecte o comando `media_generate_thumbnail` ou `media_generate_filmstrip` para criar miniaturas em instantes regulares do clipe.
2. No hover do cursor sobre a barra de tempo do player, exiba um tooltip flutuante contendo o frame correspondente a posicao do mouse e o timecode exato.

---

## 5. Procedimentos de Teste e Validacao
Execute os seguintes testes praticos:

1. **Teste de Reproducao de Arquivos Reais:**
   - Selecione um video real de 4K ou 1080p na biblioteca.
   - Pressione Play e verifique a reproducao continua de video e audio.
   - Pressione Barra de Espaco e verifique a pausa imediata.

2. **Teste de Navegacao Temporal (Seek):**
   - Arraste a barra de transporte para o meio do video. O video deve responder com seek rapido e sem tela preta persistente.
   - Pressione a Seta Direita 5 vezes: o timecode deve avancar exatamente 5 frames de acordo com o framerate da gravacao (ex.: 24, 30 ou 60 fps).

3. **Teste de Waveform com Audio Real:**
   - Inspecione a presenca do grafico de picos de audio: trechos com falas altas devem apresentar picos visiveis e momentos de silencio devem apresentar linhas de amplitude baixa.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] O video real e carregado via protocolo nativo do Tauri (`convertFileSrc`) sem erros de CORS.
- [ ] Os controles de Play, Pause, Seek e atalhos de teclado (Espaco, J-K-L, Setas) funcionam de forma responsiva.
- [ ] O timecode SMPTE exibido corresponde com precisao de quadro a posicao do video.
- [ ] A forma de onda (waveform) reflete o audio real extraido do arquivo via motor Rust.
- [ ] O timer simulado por `setInterval` foi completamente descontinuado em favor dos eventos nativos do video.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da:
`ETAPA_03_TRANSCRICAO_E_SINCRONIZACAO.md`.
