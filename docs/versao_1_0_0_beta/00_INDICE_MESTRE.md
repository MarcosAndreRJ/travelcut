# TravelCut Studio v1.0.0-beta - Guia de Execucao e Indice Mestre

## 1. Visao Geral e Proposito da Versao 1.0.0-beta

O **TravelCut Studio** e um pre-editor de video assistido por inteligencia artificial para criadores de conteudo de viagem e documental. O objetivo desta serie de prompts e transformar a base de codigo atual - composta por modulos de interface React/TypeScript, motor nativo Rust (`crates/travelcut-media` e `src-tauri`) e prototipos de pesquisa Python (`research/transcription`) - em uma aplicacao desktop minimamente funcional e integrada, capaz de:

1. **Ingestar e inspecionar midias reais** de cartoes SD e SSDs locais (Sony FX3, DJI Mini, GoPro, Smartphones), extraindo metadados tecnicos reais via FFmpeg/FFprobe nativo.
2. **Reproduzir video e audio** de forma fluida e confiavel dentro do app, com navegacao temporal, timecode SMPTE e inspecao de trechos.
3. **Transcrever audio real com Whisper e VAD**, gerando alinhamento temporal fino a nivel de palavra e preservando respiros e ambiencias naturais de viagem (como drones e cachoeiras).
4. **Classificar tipos de trechos audiovisuais** (fala clara, transito/estrada, drone/paisagem, ruido severo) para orientar a tomada de decisao editorial.
5. **Sugerir cortes inteligentes** (hesitacoes, silencios mortos >= 1.2s, repeticoes, erros de take) apresentados para revisao humana em interface de texto e linha do tempo sincronizadas.
6. **Gerar uma pre-montagem cronologica e reproduzivel** em timeline NLE interna, permitindo ajustes finos e adicao de musica ambiente.
7. **Orquestrar o pipeline completo de forma automatizada**, em um unico clique com suporte a progresso, pausa e cancelamento cooperativo.
8. **Exportar a montagem final para o DaVinci Resolve Studio** (via FCPXML 1.8 / EDL CMX 3600) com referencias exatas aos arquivos originais, faixas de audio sincronizadas e marcadores de corte.
9. **Validar estabilidade, desempenho e gerar o executavel de release Windows** testado e pronto para distribuicao.

---

## 2. As Quatro Regras Fundamentais de Desenvolvimento

Toda IA ou desenvolvedor executando qualquer etapa deste plano deve seguir estritamente as quatro regras:

1. **Nao reconstruir o que ja existe:** Cada etapa comeca inspecionando e reaproveitando os componentes visuais (`src/features/library`, `src/features/editing`, `src/components/TimelineNLE.tsx`), os modulos Rust em `crates/travelcut-media` e os algoritmos em `research/transcription`. Nao crie interfaces ou funcoes do zero quando houver uma implementacao previa.
2. **Nao utilizar dados ficticios como resultados reais:** Eliminar progressivamente o uso de `mockData.ts` nas operacoes principais. As acoes do usuario (escanear pasta, reproduzir video, transcrever, cortar, exportar) devem executar operacoes reais no sistema operacional e no disco.
3. **Nao alterar os videos originais (Edicao Nao-Destrutiva):** Todos os cortes, descartes e recomendacoes operam sobre metadados, intervalos de tempo e timecodes. O arquivo bruto nunca e modificado ou sobregravado.
4. **Nao avancar sem testar:** Cada etapa contem criterios de aceite objetivos (Definition of Done) e comandos de validacao executaveis. A etapa seguinte so deve ser iniciada quando todos os criterios da etapa anterior estiverem 100% cumpridos.

---

## 3. Mapa de Dependencias entre as Etapas

A execucao deve ser rigorosamente sequencial de 0 a 9:

```
[Etapa 00: Infraestrutura & Contratos]
        │
        ▼
[Etapa 01: Projetos & Midias Reais]
        │
        ▼
[Etapa 02: Player & Preview Audiovisual]
        │
        ▼
[Etapa 03: Transcricao & Sincronizacao]
        │
        ▼
[Etapa 04: Classificacao Audiovisual]
        │
        ▼
[Etapa 05: Cortes Inteligentes]
        │
        ▼
[Etapa 06: Timeline & Pre-Montagem]
        │
        ▼
[Etapa 07: Pipeline de Automacao]
        │
        ▼
[Etapa 08: Exportacao DaVinci Resolve]
        │
        ▼
[Etapa 09: Testes Finais & Release Beta]
```

---

## 4. Indice de Prompts Detalhados

| Arquivo de Prompt | Titulo da Etapa | Foco Principal |
| :--- | :--- | :--- |
| `ETAPA_00_INFRAESTRUTURA_E_CONTRATOS.md` | Infraestrutura & Contratos | Unificacao Tauri 2, acoplamento de `crates/travelcut-media`, tipos compartilhados e desktop bridge. |
| `ETAPA_01_PROJETOS_E_MIDIAS_REAIS.md` | Projetos & Midias Reais | Dialogs nativos de pastas, varredura real em disco, ffprobe, persistencia `.travelcut` em `3-Trabalho/`. |
| `ETAPA_02_PLAYER_E_PREVIEW_AUDIOVISUAL.md` | Player & Preview Audiovisual | Streaming local seguro (custom protocol Tauri), transporte NLE, waveform real e filmstrip. |
| `ETAPA_03_TRANSCRICAO_E_SINCRONIZACAO.md` | Transcricao & Sincronizacao | Integracao do pipeline Whisper/VAD, extracao de audio 16kHz mono, timestamps por palavra e inspetor. |
| `ETAPA_04_CLASSIFICACAO_AUDIOVISUAL.md` | Classificacao Audiovisual | Deteccao de voz, b-roll, drone, silencio e musica; classificacao de segmentos dentro dos arquivos. |
| `ETAPA_05_CORTES_INTELIGENTES.md` | Cortes Inteligentes | Algoritmo de deteccao de hesitacoes e silencias; painel de revisao, selecao de texto e aprovacao. |
| `ETAPA_06_TIMELINE_E_PRE_MONTAGEM.md` | Timeline & Pre-Montagem | Montagem sequencial de trechos mantidos, ajuste de pontos In/Out, ripple edit e reproducao da timeline. |
| `ETAPA_07_PIPELINE_DE_AUTOMACAO.md` | Pipeline de Automacao | Execucao em lote "One-Click Import & Cut", gerenciamento de fila tokio, progresso e cancelamento. |
| `ETAPA_08_EXPORTACAO_DAVINCI_RESOLVE.md` | Exportacao DaVinci Resolve | Geracao rigorosa de FCPXML 1.8 e EDL CMX 3600 com caminhos absolutos locais e marcadores de cor. |
| `ETAPA_09_TESTES_E_RELEASE_BETA.md` | Testes Finais & Release Beta | Validacao de ponta a ponta com filmagens reais, testes de regressao, otimizacao e build final Windows. |

---

## 5. Como Utilizar Estes Prompts com a IA

Para executar qualquer etapa:
1. Abra uma nova sessao com a IA ou utilize o mesmo contexto apos a conclusao da etapa anterior.
2. Copie integralmente o conteudo do arquivo da etapa respectiva (`ETAPA_XX_...md`).
3. Forneca o prompt a IA sem adicionar regras contraditorias.
4. Acompanhe a execucao, conferindo os arquivos alterados e os comandos de teste sugeridos no corpo do prompt.
5. Verifique a secao **Criterios de Aceite (Definition of Done)** antes de autorizar o avanco para a proxima etapa.
