# Prompt de Desenvolvimento - Etapa 05: Cortes Inteligentes e Revisao Editorial Human-in-the-Loop

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 05 - Sugestoes de Cortes Inteligentes por IA e Painel de Aprovacao Editorial
- **Objetivo Central:** Transformar os dados de transcricao, VAD e classificacao em recomendacoes editoriais concretas. Detectar automaticamente hesitacoes vocais (*"eh"*, *"hum"*, gaguejos), silencios mortos desnecessarios (>= 1.2 segundos), repeticoes de frases (falsos inícios / bad takes) e trechos de baixa relevancia. Apresentar essas sugestoes no espaco de trabalho de revisao (`ReviewWorkspace.tsx`), permitindo ao editor aceitar, rejeitar, refinar ou criar cortes manuais com um unico clique ou selecao de texto.
- **Resultado Esperado:** Ao carregar os videos transcritos, a IA gera uma lista de cortes recomendados com classificacao e justificativa clara. O usuario pode reproduzir o trecho antes/depois do corte (Modo A/B), aprovar em lote com seguranca, rejeitar sugestoes inadequadas e ver o tempo total economizado da montagem em tempo real.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `research/transcription/`:
  - `alignment.py`: Detecta marcadores de hesitacao e palavras com tempo anormalmente longo ou confianca baixa.
  - `silence_detector.py`: Segmenta pausas de acordo com a taxonomia de viagem, identificando especificamente `REMOVABLE_GAP` (>= 1.2s) em trechos de apresentador e protegendo pausas curtas de respiracao (`PAUSE_SENTENCE`).
- `src/views/ReviewWorkspace.tsx`:
  - Interface completa de revisao editorial com filtros de categoria (`hesitation`, `silence`, `redundancy`, `bad_take`), controles de aprovacao/rejeicao e resumo de tempo economizado.
- `src/features/editing/components/`:
  - `IntegratedTranscript.tsx`: Editor textual sincronizado com marcadores visuais de corte sobre o texto falado.
  - `IntegratedVideoPlayer.tsx`: Player com suporte a pre-visualizacao de corte A/B (saltar o trecho sugerido automaticamente durante o play).
- `src/features/editing/context/EditingContext.tsx`:
  - Gerenciador de estado para aceitar cortes (`acceptCut`), rejeitar (`rejectCut`), aprovar todos pendentes (`acceptAllPendingCuts`) e desfazer (`history`).

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** aplique cortes destrutivos diretamente nos arquivos de video. Todos os cortes sao marcas virtuais (timecodes de inicio e fim de descarte).
- **NAO** descarte silencios em tomadas de drone ou paisagem contemplativa. O detector deve consultar a classificacao da Etapa 04 e preservar lacunas de audio em arquivos marcados como `drone` ou `landscape`.
- **NAO** force a remocao de pausas naturais de respiracao (< 1.2s) entre frases normais, para nao gerar uma fala robotica ou artificial ("jump cuts" excessivos e desagradaveis).

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 5.1: Motor de Deteccao de Cortes Recomendados
1. Implemente o algoritmo de deteccao de cortes em `research/transcription/` ou no motor TypeScript (`src/features/editing/services/cutDetectionEngine.ts`):
   - **Regra 1 - Hesitacoes e Vicios de Linguagem:**
     - Analisa os tokens transcritos na Etapa 03.
     - Identifica palavras como: *"eh"*, *"hum"*, *"humm"*, *"aham"*, *"tipo assim"*, *"tipo"*, *"ne"*, *"entao assim"* em inicio ou meio de pausas.
     - Gera uma sugestao com `reason: 'hesitation'`, englobando a palavra e o silencio adjacente.
   - **Regra 2 - Silencios Mortos (Gaps >= 1.2s):**
     - Cruza com a taxonomia do `silence_detector.py`.
     - Identifica intervalos sem voz em trechos de dialogo onde `durationSec >= 1.2`.
     - Mantem um respiro de seguranca (padrao: 150ms de margem antes e depois) para que a transicao de audio nao soe truncada.
     - Gera uma sugestao com `reason: 'silence'`.
   - **Regra 3 - Repeticoes e Falsos Inicios (Bad Takes):**
     - Detecta n-gramas repetidos em uma mesma frase (ex.: *"Nos chegamos aqui... Nos chegamos aqui na praia"*).
     - Marca a primeira tentativa incompleta como `reason: 'redundancy'` ou `reason: 'bad_take'`.
2. Saida padronizada no modelo `UnifiedCutSuggestion`:
   ```typescript
   export interface UnifiedCutSuggestion {
     id: string;
     mediaClipId: string;
     title: string;
     reason: 'hesitation' | 'silence' | 'redundancy' | 'bad_take' | 'technical_glitch';
     description: string;
     startSec: number;
     endSec: number;
     durationSec: number;
     confidence: number;
     decisionState: 'pending' | 'accepted' | 'rejected';
     textSnippet: string;
   }
   ```

### Tarefa 5.2: Conectar o `ReviewWorkspace.tsx` aos Dados Reais
1. Substitua os arrays mockados do `EditingContext.tsx` pela saida gerada pelo motor de cortes sobre a transcricao real.
2. No cabecalho do `ReviewWorkspace.tsx`:
   - Atualize os contadores:
     - `Original:` duracao total bruta em formato SMPTE.
     - `Economizados:` soma da duracao dos cortes com `decisionState === 'accepted'`.
     - `Montagem:` duracao restante (`totalOriginal - economizados`).
3. Conecte os filtros de categoria:
   - Clicar em "Hesitacoes (X)" exibe apenas hesitacoes.
   - Clicar em "Silencios (Y)" exibe apenas pausas longas.
   - Clicar em "Todos" exibe a lista completa ordenada por timecode.

### Tarefa 5.3: Reproducao A/B no Player (Preview do Corte)
1. No `IntegratedVideoPlayer.tsx`:
   - Quando o modo A/B estiver ativado (`viewModeAB === 'preview_cut'`), ao dar play em um trecho contendo um corte aceito:
     - O player reproduz ate o instante `startSec` do corte e salta imediatamente para `endSec`, simulando como o video ficara apos a remocao do trecho indesejado.
   - Adicione um botao "Ouvir Corte" em cada card de sugestao que reproduz 2 segundos antes e 2 segundos depois do corte.

### Tarefa 5.4: Edicao de Corte Baseada em Texto (Text-Based Video Editing)
1. No `IntegratedTranscript.tsx`:
   - As palavras contidas em sugestoes aceitas devem ser renderizadas com estilo tachado (strikethrough) e opacidade reduzida.
   - O usuario pode selecionar manualmente um trecho de texto com o mouse e clicar no menu flutuante "Cortar Trecho" ou "Manter Trecho", gerando uma nova sugestao de corte manual com precisao de palavra.

---

## 5. Procedimentos de Teste e Validacao
Realize as seguintes verificacoes:

1. **Teste de Deteccao de Hesitacoes e Silencios:**
   - Execute a analise sobre um video contendo hesitacoes propositais (*"eh... nos estamos aqui"*) e pausas superiores a 2 segundos.
   - *Resultado esperado:* O painel de revisao deve listar as hesitacoes e os silencios longos com os timecodes corretos e a transcricao correspondente.

2. **Teste de Interacao Editorial (Aceitar / Rejeitar):**
   - No `ReviewWorkspace.tsx`, clique no botao "Aceitar" (Check) em uma sugestao de silencio de 3 segundos.
   - Verifique se o contador de tempo economizado aumenta em exatamente 3.0s e o tempo de montagem diminui.
   - Clique em "Rejeitar" (X) em outra sugestao: ela deve ser marcada como descartada e nao impactar o tempo da montagem.

3. **Teste de Simulacao A/B:**
   - De play no video no modo preview de corte: a agulha deve saltar o intervalo do silencio aprovado suavemente, sem interrupcao do player.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] O algoritmo identifica confiavelmente pausas longas (>= 1.2s) e palavras de hesitacao sobre a transcricao real.
- [ ] O painel `ReviewWorkspace.tsx` exibe as sugestoes reais com timecodes e trecho textual.
- [ ] Os botoes de Aceitar, Rejeitar e Aceitar Todos Pendentes atualizam as metricas de tempo economizado.
- [ ] O player permite pre-visualizar o salto do corte no modo de comparacao A/B.
- [ ] O usuario pode criar cortes manuais selecionando palavras diretamente no texto da transcricao.
- [ ] As decisoes editoriais sao persistidas no arquivo `.travelcut`.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da:
`ETAPA_06_TIMELINE_E_PRE_MONTAGEM.md`.
