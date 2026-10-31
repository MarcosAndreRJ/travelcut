# Prompt de Desenvolvimento - Etapa 04: Classificacao Audiovisual e Segmentacao de Cenas

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 04 - Classificacao Audiovisual e Identificacao de Segmentos Tematicos
- **Objetivo Central:** Capacitar o TravelCut Studio a diferenciar os tipos de conteudo presentes nas gravacoes de viagem (fala/dialogo, estrada/deslocamento, drone/aereo, paisagem/natureza e b-roll), tanto a nivel de arquivo completo quanto em trechos especificos dentro do mesmo clipe. Implementar regras inteligentes baseadas em metadados de camera, presenca de voz (VAD) e analise de mudanca de cena (scene cut detection) para marcar segmentos e exibi-los na biblioteca e no painel de analise para revisao do usuario.
- **Resultado Esperado:** Os clipes na biblioteca e na linha de analise recebem tags e segmentacoes categorizadas (ex.: "Trecho 00:00-00:45: Fala", "Trecho 00:45-02:10: Estrada/B-Roll"). O usuario pode filtrar seus videos por tipo de tomada e ajustar ou reclassificar qualquer trecho manualmente.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `research/transcription/`:
  - `vad.py`: Fornece segmentos com presenca ou ausencia confiavel de voz.
  - `silence_detector.py`: Aplica a taxonomia de preservacao de paisagens e drones (`DRONE`/`SCENERY`), evitando que trechos contemplativeis sejam marcados como silencio inutil.
- `crates/travelcut-media/src/probe/ffmpeg_probe.rs`:
  - Fornece metadados cruciais do equipamento de captura (`camera_make`, `camera_model`, resolucao, taxa de quadros e canais de audio). Por exemplo:
    - Dispositivos com modelo contendo "DJI" ou "FC..." sao identificados nativamente como Drone/Aereo.
    - Dispositivos "GoPro" ou com alta taxa de quadros (120fps/240fps) sao categorizados prioritariamente como Acao/Slow-motion.
- `src/views/AnalysisView.tsx`:
  - Painel de analise do projeto com graficos de distribuicao de categorias (Fala vs B-Roll vs Drone vs Silencio).
- `src/features/library/components/MediaGrid.tsx`:
  - Exibicao de tags e badges coloridos de classificacao nos cards de video.
- `src/features/media/types.ts` e `src/features/editing/types/editingTypes.ts`:
  - Tipagens para `ContentCategory = 'speech' | 'drone' | 'landscape' | 'road_trip' | 'action' | 'broll'`.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** dependa de redes neurais pesadas de visao computacional em nuvem que exijam conexao com a internet ou GPUs de datacenter. A classificacao do TravelCut Studio v1.0.0-beta deve ser rapida, local e deterministica, combinando metadados de hardware, analise espectral de audio/VAD e cortes de cena via FFmpeg local.
- **NAO** torne a classificacao automatica irrevogavel. Toda classificacao inferida pela IA deve ser passivel de correcao rapida pelo editor com um clique.
- **NAO** quebre arquivos fisicos no disco ao segmentar cenas; toda segmentacao e virtual, operando por timecodes In/Out nao-destrutivos.

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 4.1: Extrator de Cenas e Segmentos em Rust
1. Adicione ao crate `travelcut-media` (ou `commands.rs`) a funcionalidade de deteccao de cortes de cena por mudanca visual (Scene Change Detection):
   ```rust
   pub async fn detect_scene_cuts(
       video_path: String,
       threshold: f32 // padrao: 0.35 (35% de mudanca de quadro)
   ) -> Result<Vec<f64>, String>;
   ```
   - O comando invoca internamente:
     `ffmpeg -i "{video_path}" -filter_complex "select='gt(scene,{threshold})',showinfo" -f null -`
   - O parser captura os timestamps onde ocorrem transicoes de cena brutas.

### Tarefa 4.2: Motor de Regras de Classificacao Hibrida
1. Crie o modulo classificador que cruza os dados disponiveis:
   - **Regra 1 (Metadados de Camera):** Se `camera_model` contem "DJI", "Mavic", "Mini", o arquivo/segmento recebe a tag inicial `drone`.
   - **Regra 2 (Atividade de Voz - VAD):** Se o intervalo temporal possui atividade de voz confirmada pelo VAD (> 60% de densidade de fala no trecho), o segmento e classificado como `speech` (Dialogo / Apresentacao).
   - **Regra 3 (Cortes de Cena e Movimento):** Se o trecho nao possui fala e contem mudancas de cena frequentes ou sem audio significativo, e rotulado como `broll` ou `landscape`.
   - **Regra 4 (Assinatura Sonora / Estrada):** Ruido continuo de baixa frequencia (ronco de motor / asfalto) sem fala identificavel e categorizado como `road_trip`.
2. Estruture a saida como uma lista de segmentos por arquivo:
   ```typescript
   export interface MediaSegment {
     id: string;
     assetId: string;
     startSec: number;
     endSec: number;
     category: 'speech' | 'drone' | 'landscape' | 'road_trip' | 'broll';
     confidence: number;
     isUserOverridden: boolean;
   }
   ```

### Tarefa 4.3: Interface de Visualizacao e Filtros na Biblioteca
1. Em `src/features/library/components/MediaGrid.tsx`:
   - Adicione uma barra de filtros rapidos por categoria no topo da biblioteca:
     `[ Todos ] [ Falas (12) ] [ Drone (8) ] [ Paisagens (15) ] [ Estrada (5) ]`
   - Cada card de video deve exibir o badge principal da tomada e uma mini barra de divisao temporal mostrando as cores dos segmentos internos.
2. Ao clicar com o botao direito no card ou abrir os detalhes do clipe:
   - Permitir que o usuario altere a categoria do clipe ou de qualquer segmento com um clique em menu suspenso.

### Tarefa 4.4: Painel de Analise e Distribuicao (`src/views/AnalysisView.tsx`)
1. Atualize a tela de analise para exibir graficos reais calculados sobre os segmentos reais do projeto:
   - Percentual de tempo de fala vs tomadas de apoio (B-Roll/Drone).
   - Indicador de equilibrio narrativo do vlog (tempo estimado de apresentador vs tempo de cenarios).
   - Alertas editoriais (ex.: "Atencao: Existem mais de 10 minutos de gravações de estrada sem nenhuma fala associada").

---

## 5. Procedimentos de Teste e Validacao
Realize as seguintes verificacoes:

1. **Teste de Classificacao por Metadados de Camera:**
   - Adicione um clipe gravado por drone DJI e um gravado por camera Sony.
   - Execute a classificacao.
   - *Resultado esperado:* O video da DJI deve receber automaticamente o badge "Drone" e o video da Sony contendo narracao deve receber o badge "Fala".

2. **Teste de Segmentacao Mista em Arquivo Unico:**
   - Utilize um video longo onde o apresentador fala durante os primeiros 30 segundos e depois a camera foca na paisagem em silencio.
   - Execute a classificacao.
   - *Resultado esperado:* O arquivo deve apresentar 2 segmentos distintos na linha temporal (0-30s Fala; 30s-fim Paisagem).

3. **Teste de Sobrescrita Manual do Usuario:**
   - Altere manualmente a categoria de um trecho de "Estrada" para "B-Roll".
   - Salve o projeto e recarregue.
   - *Resultado esperado:* A classificacao manual do usuario deve ser preservada (`isUserOverridden: true`) e nao ser recalculada automaticamente.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] O classificador identifica corretamente tomadas de drone a partir dos metadados de camera.
- [ ] Trechos de fala sao confiavelmente separados de trechos de silencio e contemplacao via VAD.
- [ ] A biblioteca permite filtrar os arquivos de midia por suas respectivas categorias tematicas.
- [ ] A interface permite que o usuario reclassifique qualquer clipe ou segmento manualmente.
- [ ] O painel `AnalysisView.tsx` reflete o balanceamento percentual real dos videos importados.
- [ ] As informacoes de segmentos sao persistidas no arquivo `.travelcut`.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da:
`ETAPA_05_CORTES_INTELIGENTES.md`.
