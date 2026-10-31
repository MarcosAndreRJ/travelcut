# Prompt de Desenvolvimento - Etapa 08: Exportacao Profissional para o DaVinci Resolve

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 08 - Geracao e Exportacao de Timelines FCPXML, XML FCP7 e EDL para o DaVinci Resolve Studio
- **Objetivo Central:** Garantir que todo o trabalho de pre-edicao, selecao de trechos e cortes realizado no TravelCut Studio possa ser transferido perfeitamente para o DaVinci Resolve Studio (versoes 18 e 19). Gerar arquivos de intercambio NLE de padrao profissional (FCP7 XML `xmeml` versao 5, FCPXML 1.8 e EDL CMX 3600) contendo referencias exatas aos arquivos originais de video no disco, timecodes de entrada/saida perfeitamente alinhados ao framerate nativo e marcadores de cor categorizados.
- **Resultado Esperado:** O usuario clica em "Exportar para DaVinci Resolve" na tela de exportacao (`ExportView.tsx`), escolhe o diretorio de destino e o TravelCut grava o arquivo XML e os relatorios no disco. Ao abrir o DaVinci Resolve e importar a timeline (`File > Import > Timeline > Import AAF, EDL, XML...`), o Resolve localiza todos os arquivos brutos imediatamente, sem clipes "Offline", montando a timeline idêntica ao que foi pre-visualizado no TravelCut.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `src/features/editing/utils/davinciExporter.ts`:
  - Contem a base de geracao do formato FCP7 XML (`generateDaVinciXML`), funcoes de conversao de timecode SMPTE (`secondsToTimecode`) e calculo exato de quadros (`secondsToFrames`).
- `src/views/ExportView.tsx`:
  - Interface de usuario com seletores de formato de exportacao (DaVinci Resolve XML, Final Cut Pro XML, EDL CMX 3600, Relatorio CSV de Auditoria Editorial).
- `src/features/editing/types/editingTypes.ts`:
  - `DaVinciExportData` contendo `projectName`, `fps`, `resolution`, `timelineClips`, `mediaAssets` e `cutSuggestions`.
- `src-tauri/src/commands.rs`:
  - Modulo nativo para escrita de arquivos texto diretamente no sistema de arquivos do Windows sem limitacoes de download do browser.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** gere caminhos relativos ficticios ou URLs de localhost sem caminho real. O DaVinci Resolve exige caminhos URI validos para encontrar as midias locais (ex.: `file://localhost/D:/Videos/Viagem2026/clip_01.mp4`).
- **NAO** misture timebases fracionarias incorretamente (ex.: configurar o projeto para 60fps inteiro quando a filmagem original foi gravada em 59.94fps NTSC). O descompasso de framerate causa desincronizacao progressiva de audio e video na timeline do Resolve.
- **NAO** force re-renderizacao de video. A exportacao deve ser exclusivamente um arquivo de metadados de troca de timeline (XML/EDL), executando instantaneamente em fracao de segundos.

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 8.1: Aprimorar o Gerador de FCP7 XML (`xmeml`)
1. Refatore `src/features/editing/utils/davinciExporter.ts` para garantir compatibilidade total com o DaVinci Resolve 18/19:
   - Formato padrao da tag de arquivo:
     ```xml
     <pathurl>file://localhost/${filePath.replace(/\\/g, '/')}</pathurl>
     ```
   - Configuracao correta de taxa de quadros e flag NTSC:
     - Para 23.976: `<timebase>24</timebase><ntsc>TRUE</ntsc>`
     - Para 29.97: `<timebase>30</timebase><ntsc>TRUE</ntsc>`
     - Para 59.94: `<timebase>60</timebase><ntsc>TRUE</ntsc>`
     - Para 24.0, 25.0, 30.0, 60.0: `<ntsc>FALSE</ntsc>`
   - Estrutura completa de trilhas de video (`<video>`) e trilhas de audio sincronizadas (`<audio>`) com os pares estereo A1/A2.

### Tarefa 8.2: Integracao de Marcadores de Cor Tematicos (Resolve Markers)
1. No arquivo XML gerado, inclua marcadores de clipe ou marcadores de timeline (`<marker>`) para cada sugestao de corte ou decisao editorial relevante:
   - **Verde (Cyan/Green):** Inicio de corte aceito / trecho mantido com fala destacada.
   - **Laranja (Orange):** Hesitacao removida ou ponto de respiracao ajustado.
   - **Azul (Blue):** Transicao para B-Roll / tomada de cobertura sugerida.
   - **Roxo (Purple):** Tomada aerea de Drone ou paisagem contemplativa.
2. Cada marcador deve conter:
   ```xml
   <marker>
     <comment>Hesitacao removida: 'eh...'</comment>
     <name>Corte IA</name>
     <in>${markerFrameIn}</in>
     <out>${markerFrameOut}</out>
     <color>Orange</color>
   </marker>
   ```

### Tarefa 8.3: Suporte ao Formato EDL (CMX 3600)
1. Crie a funcao `generateCMX3600EDL`:
   - Gera um arquivo `.edl` padrao da industria cinematografica, essencial como opcao de contingencia quando o editor prefere importar apenas os cortes secos da fita de video sem filtros de XML:
     ```text
     TITLE: MEU_VLOG_VIAGEM
     FCM: NON-DROP FRAME
     
     001  AX       V     C        00:00:10:00 00:00:25:00 00:00:00:00 00:00:15:00
     * FROM CLIP NAME: CLIP_001.MP4
     002  AX       V     C        00:00:05:00 00:00:30:00 00:00:15:00 00:00:40:00
     * FROM CLIP NAME: CLIP_002.MP4
     ```

### Tarefa 8.4: Relatorio de Auditoria Editorial em CSV / Markdown
1. Crie a funcao `generateEditorialAuditReport`:
   - Gera um arquivo `{nome_do_projeto}_relatorio_cortes.csv`:
     - Colunas: `ID, Arquivo_Origem, Timecode_In, Timecode_Out, Duracao_s, Categoria, Motivo_Corte, Status_Aprovacao, Trecho_Texto`.
   - Gera um resumo executivo em Markdown com metricas de aproveitamento da filmagem bruta.

### Tarefa 8.5: Conectar a Acao de Exportacao na `ExportView.tsx`
1. Em `src/views/ExportView.tsx`:
   - Adicione o seletor de pasta de destino usando o dialog nativo do Windows (Etapa 01).
   - Ao clicar em "Gerar Timeline para DaVinci Resolve":
     - Invoca o comando Rust nativo para gravar os arquivos gerados (`.xml`, `.edl`, `.csv`) diretamente na pasta escolhida (por padrao em `3-Trabalho/Exportacoes/`).
     - Exibe um card modal de sucesso com atalho para "Abrir Pasta no Windows Explorer" (`show_item_in_folder`).

---

## 5. Procedimentos de Teste e Validacao
Realize as seguintes verificacoes praticas:

1. **Teste de Validacao Sintatica do XML Gerado:**
   - Exporte um projeto contendo ao menos 5 cortes aceitos em 2 videos.
   - Abra o arquivo `.xml` gerado em um editor de texto ou parser XML:
     - Verifique se a tag de cabecalho `<?xml version="1.0" encoding="UTF-8"?>` e o `DOCTYPE xmeml` estao presentes e validos.
     - Confirme que a tag `<pathurl>` contem o caminho absoluto correto para o arquivo de video no Windows.
     - Verifique se os frames de `<start>`, `<end>`, `<in>` e `<out>` sao inteiros positivos e respeitam a equacao: `end - start == out - in`.

2. **Teste Pratico de Importacao no DaVinci Resolve:**
   - Abra o DaVinci Resolve Studio.
   - Crie um novo projeto limpo.
   - Va em `File > Import > Timeline > Import AAF, EDL, XML...` e selecione o arquivo exportado pelo TravelCut Studio.
   - *Resultado esperado:* O DaVinci Resolve deve carregar a timeline sem nenhuma caixa de dialogo solicitando busca manual de midias ausentes. A sequencia deve abrir na Edit Page com todos os cortes e marcadores coloridos visiveis.

3. **Teste de Conferencia de Timecode no Resolve:**
   - Toque a timeline importada no Resolve: o audio deve estar perfeitamente sincronizado com os movimentos labiais e as pausas cortadas devem coincidir com a pre-visualizacao do TravelCut.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] O arquivo XML gerado e totalmente compativel com o DaVinci Resolve 18 e 19.
- [ ] Todas as tags `<pathurl>` utilizam caminhos absolutos reais para os arquivos locais no Windows.
- [ ] Taxas de quadros fracionarias (23.976, 59.94) sao tratadas com a flag NTSC apropriada para evitar descompasso de audio.
- [ ] Sugestoes de corte e categorias aparecem como marcadores coloridos na timeline do Resolve.
- [ ] A exportacao inclui tambem a opcao de arquivo EDL CMX 3600 e relatorio CSV de auditoria.
- [ ] Os arquivos sao gravados diretamente em disco e o usuario pode abrir a pasta com um clique.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da etapa final:
`ETAPA_09_TESTES_E_RELEASE_BETA.md`.
