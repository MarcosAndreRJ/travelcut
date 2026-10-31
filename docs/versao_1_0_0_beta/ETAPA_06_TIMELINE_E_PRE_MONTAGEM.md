# Prompt de Desenvolvimento - Etapa 06: Timeline NLE e Pre-Montagem Sequencial

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 06 - Montagem da Timeline NLE, Sequenciamento Cronologico e Reproducao de Pre-Corte
- **Objetivo Central:** Construir uma pre-montagem funcional e reproduzivel na linha do tempo profissional (NLE), gerada a partir da ordem cronologica das filmagens e dos trechos preservados (pos-aprovacao de cortes da Etapa 05). Permitir que o editor assista a sequencia montada continuamente no player, ajuste pontos de entrada (In) e saida (Out), mova clipes, execute operacoes de ripple edit (exclusao com fechamento de lacuna) e adicione uma trilha de musica de fundo ou som ambiente.
- **Resultado Esperado:** O usuario visualiza sua pre-montagem em uma timeline de multiplas faixas (V1 para cortes principais, V2 para B-Roll/Drone, A1 para audio de fala, A2 para musica/ambiente). Ao dar play na timeline, o player reproduz os trechos selecionados de forma encadeada e suave, sem que o usuario precise abrir um software externo para validar o ritmo do video.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `src/components/TimelineNLE.tsx`:
  - Componente robusto de linha do tempo com visualizacao de faixas de video (V1, V2) e audio (A1, A2, A3), regua de tempo com zoom interativo, agulha de reproducao (playhead) e manipuladores de recorte (trim handles).
- `src/features/editing/components/IntegratedTimeline.tsx`:
  - Mini-timeline do painel de revisao que renderiza a comparacao entre a duracao original e os blocos mantidos.
- `src/features/editing/types/editingTypes.ts`:
  - Definicao de `UnifiedTimelineClip`:
    ```typescript
    export interface UnifiedTimelineClip {
      id: string;
      mediaClipId: string;
      title: string;
      trackIndex: number;
      timelineStartSec: number;
      durationSec: number;
      sourceInSec: number;
      sourceOutSec: number;
      speed: number;
      colorGradeLut?: string;
    }
    ```
- `src/features/editing/utils/davinciExporter.ts`:
  - Funcoes utilitarias de calculo de tempo, conversao entre segundos e frames (`secondsToFrames`) e regras de SMPTE.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** renderize ou renderize previamente um arquivo de video `.mp4` intermediario gigante no disco para tocar a timeline. O TravelCut Studio utiliza reproducao dinamica NLE virtual: o player le os arquivos originais nos intervalos In/Out definidos e salta de um clipe para o outro dinamicamente.
- **NAO** implemente transicoes complexas 3D, efeitos visuais ou geracao de titulos animados neste momento. O foco e a pre-montagem estrutural (corte seco, sincronia de audio e ajuste de tempo).
- **NAO** quebre a sincronia entre a agulha da timeline e o video do player; qualquer arrasto na timeline deve atualizar o video em tempo real (scrubbing suave).

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 6.1: Gerador de Sequencia Inicial a Partir dos Cortes Aprovados
1. No `src/features/editing/services/timelineBuilder.ts`, crie a funcao que calcula os clipes da timeline:
   ```typescript
   export function buildTimelineSequence(
       mediaAssets: MediaAsset[],
       cutSuggestions: UnifiedCutSuggestion[],
       targetFps: number = 60
   ): UnifiedTimelineClip[];
   ```
   - **Logica de Subclipagem:**
     - Para cada video original (ordenado pela data/hora real de gravacao `recordedAt`):
       - Identifica os intervalos de cortes com status `'accepted'`.
       - Os intervalos complementares (nao cortados) tornam-se subclipes mantidos na faixa V1/A1.
       - Exemplo: Video de 60s com corte aceito de 10s a 15s e de 40s a 45s:
         - Subclipe 1: `sourceIn: 0.0`, `sourceOut: 10.0`, `duration: 10.0`, `timelineStart: 0.0`
         - Subclipe 2: `sourceIn: 15.0`, `sourceOut: 40.0`, `duration: 25.0`, `timelineStart: 10.0`
         - Subclipe 3: `sourceIn: 45.0`, `sourceOut: 60.0`, `duration: 15.0`, `timelineStart: 35.0`
         - Duracao total da sequencia: 50.0 segundos.
     - Se houver clipes categorizados como B-Roll ou Drone (Etapa 04), permitir a alocacao opcional na faixa V2 sobreposta.

### Tarefa 6.2: Motor de Reproducao Encadeada da Timeline (Timeline Playback Engine)
1. Crie o hook ou gerenciador `useTimelinePlayback`:
   - Mantem a posicao global da timeline (`timelineCurrentTimeSec`).
   - Mapeia o tempo global para o clipe ativo:
     - Localiza o subclipe onde `timelineCurrentTimeSec >= clip.timelineStartSec && timelineCurrentTimeSec < (clip.timelineStartSec + clip.durationSec)`.
     - Determina o arquivo de midia associado e o offset interno:
       `videoCurrentTime = clip.sourceInSec + (timelineCurrentTimeSec - clip.timelineStartSec)`.
   - Se o video atual chegar ao `clip.sourceOutSec`, troca automaticamente o `src` do player para o proximo clipe e define `currentTime = nextClip.sourceInSec`.
   - Permite reproduzir continuamente a sequencia inteira do inicio ao fim.

### Tarefa 6.3: Ajustes Interativos na Linha do Tempo (Trim e Ripple)
1. Em `src/components/TimelineNLE.tsx`:
   - Conecte as alcas de corte (handles esquerdo e direito dos blocos de clipe):
     - Arrastar a alca esquerda ajusta o `sourceInSec` do clipe.
     - Arrastar a alca direita ajusta o `sourceOutSec`.
   - Implemente o modo **Ripple Edit**:
     - Ao encurtar ou deletar um clipe na faixa principal V1, todos os clipes subsequentes sao automaticamente deslocados para a esquerda, eliminando espacos vazios na montagem.
   - Adicione atalhos de teclado padrao de NLE:
     - Tecla Q: Corta a cabeca do clipe (Ripple Trim In ate a agulha).
     - Tecla W: Corta o rabo do clipe (Ripple Trim Out da agulha ate o fim).
     - Tecla Delete / Backspace: Remove o clipe selecionado com fechamento de lacuna.

### Tarefa 6.4: Suporte a Trilha de Audio Secundario (Musica / Ambiencia)
1. Na faixa A2/A3 da timeline:
   - Permita importar ou arrastar um arquivo de audio externo (`.mp3` ou `.wav`) para atuar como trilha sonora ou som ambiente.
   - Adicione controle de volume individual da faixa de musica para que ela permaneça em segundo plano (ex.: volume em -18dB enquanto ha fala na faixa A1).

---

## 5. Procedimentos de Teste e Validacao
Realize as seguintes verificacoes:

1. **Teste de Geracao Automatica da Pre-Montagem:**
   - Aprove 3 cortes em um projeto contendo 2 videos reais.
   - Navegue para a aba da Timeline (`ReviewWorkspace.tsx` ou aba `edit`).
   - *Resultado esperado:* A timeline deve exibir os blocos de subclipes ordenados cronologicamente, com a duracao total correspondendo exatamente ao tempo original menos os cortes aceitos.

2. **Teste de Reproducao Continua da Sequencia:**
   - Posicione a agulha 2 segundos antes do final do Subclipe 1 e de Play.
   - *Resultado esperado:* O player deve tocar o final do primeiro clipe e, sem travar, transicionar instantaneamente para o inicio do segundo clipe.

3. **Teste de Ripple Edit:**
   - Selecione o segundo clipe da timeline e pressione Delete.
   - *Resultado esperado:* O clipe e removido e o terceiro clipe se desloca para a esquerda, encostando no final do primeiro clipe sem deixar espaco vazio na regua de tempo.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] A timeline e populada automaticamente com os subclipes gerados a partir dos cortes aceitos.
- [ ] O player e capaz de reproduzir sequencialmente todos os trechos da montagem sem interrupcao manual.
- [ ] O usuario consegue ajustar os pontos In e Out dos clipes diretamente pelas alcas da timeline.
- [ ] O recurso de Ripple Edit fecha lacunas automaticamente ao aparar ou deletar clipes.
- [ ] A timeline suporta ao menos 2 faixas de video (V1, V2) e 2 faixas de audio (A1, A2).
- [ ] O estado da timeline e persistido no arquivo `.travelcut`.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da:
`ETAPA_07_PIPELINE_DE_AUTOMACAO.md`.
