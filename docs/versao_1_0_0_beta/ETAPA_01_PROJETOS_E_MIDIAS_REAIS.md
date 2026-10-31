# Prompt de Desenvolvimento - Etapa 01: Projetos e Midias Reais

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 01 - Gerenciamento de Projetos, Abertura de Pastas e Ingestao de Midias Reais
- **Objetivo Central:** Substituir os dados ficticios de demonstracao da tela inicial e da biblioteca de midia por operacoes reais de sistema de arquivos no Windows. Permitir a selecao de diretorios reais de filmagem atraves de dialogs nativos, escanear recursivamente pastas de cartoes de memoria ou SSDs, extrair metadados tecnicos via FFprobe e salvar/carregar projetos reais em arquivos `.travelcut` dentro da pasta `3-Trabalho/`.
- **Resultado Esperado:** O usuario consegue clicar em "Novo Projeto" ou "Importar Gravacao", selecionar uma pasta real do seu computador contendo videos (MP4, MOV, etc.), ver a grade de clipes preenchida com os dados tecnicos reais (duracao, resolucao, framerate, codec, camera) e salvar o projeto no disco.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `src/features/home/HomeDashboard.tsx` e componentes em `src/features/home/components/`:
  - `QuickActionsBar.tsx`: Botoes "Novo Projeto", "Importar Gravacao", "Abrir Projeto Existente".
  - `RecentProjectsGrid.tsx`: Exibicao de projetos salvos.
  - `HeroProjectBanner.tsx`: Retomada do projeto mais recente.
- `src/features/library/MediaLibraryWorkspace.tsx` e componentes em `src/features/library/components/`:
  - `MediaGrid.tsx`: Visualizacao em lista ou grade com badges tecnicos de resolucao, camera e duracao.
  - `ProjectHeader.tsx`: Exibicao do nome do projeto, resolucao padrao e status do fluxo.
  - `ProjectStatsCards.tsx`: Calculo consolidado de espaco em disco, quantidade de clipes e duracao total.
- `crates/travelcut-media/src/inventory/scanner.rs`:
  - Motor de escaneamento de diretorios que identifica extensoes de video (`.mp4`, `.mov`, `.mkv`, `.m4v`, `.avi`, `.braw`), calcula tamanho de arquivo e data de modificacao, filtrando arquivos de sistema ocultos.
- `crates/travelcut-media/src/probe/ffmpeg_probe.rs`:
  - Extrator de metadados FFprobe que identifica codecs de video/audio, dimensoes exatas, taxas fracionarias de FPS (23.976, 59.94) e metadados de camera/rotacao.
- `src-tauri/src/commands.rs`:
  - Contem os comandos `save_project_state` e `load_project_state` que gravam e leem arquivos JSON de estado de projeto.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** utilize `MOCK_MEDIA_CLIPS` ou `mockHomeData.ts` quando o usuario selecionar uma pasta ou criar um novo projeto. O estado deve ser preenchido exclusivamente com os arquivos encontrados no disco.
- **NAO** copie ou mova os arquivos brutos de video do usuario. O TravelCut Studio armazena apenas referencias de caminhos absolutos e metadados em cache para respeitar o principio de edicao nao-destrutiva.
- **NAO** inicie o processo de transcricao ou geracao de cortes nesta etapa (essas sao as Etapas 03 e 05). Aqui o foco e estritamente a ingestao, inspecao de metadados e persistencia do projeto.

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 1.1: Integrar Dialogs Nativos do Tauri para Selecao de Pastas
1. Implemente ou ative no Tauri (`src-tauri/tauri.conf.json` e plugins) a capacidade de abrir janelas de selecao de diretorio nativas do Windows (`@tauri-apps/plugin-dialog` ou comando IPC dedicado em Rust).
2. No `desktopBridge.ts`, crie a funcao:
   ```typescript
   export async function selectFolderDialog(title: string): Promise<string | null>;
   ```
3. Na barra de acoes rapidas da Home (`QuickActionsBar.tsx`) e no cabecalho da biblioteca (`ProjectHeader.tsx`), conecte o clique do botao "Novo Projeto" e "Importar Gravacao" para invocar o seletor nativo.

### Tarefa 1.2: Conectar o Scanner e FFprobe Real a Ingestao de Midias
1. Ao receber o caminho da pasta selecionada pelo usuario:
   - Dispare o comando `media_scan_directory` passando o caminho absoluto.
   - Para cada arquivo de video identificado, invoque `media_probe` para recuperar metadados tecnicos detalhados.
2. Em `src/features/library/MediaLibraryWorkspace.tsx`, substitua o estado inicial estatico:
   - Transforme cada `RustMediaMetadata` no formato `MediaAsset` do frontend.
   - Calcule dinamicamente a duracao formatada (HH:MM:SS), o tamanho do arquivo legivel (MB / GB) e os badges de camera a partir dos metadados de criacao e do modelo de equipamento reportado pelo FFprobe.
   - Atualize os `ProjectStatsCards` com a soma real de horas/minutos, quantidade de arquivos e total de gigabytes ocupados em disco.

### Tarefa 1.3: Estrutura de Diretorios de Trabalho e Persistencia do Projeto
1. Defina a convencao padrao de armazenamento do projeto:
   - Ao criar um projeto para uma pasta de filmagens (ex.: `D:\Viagens\Chapada2026`), o TravelCut deve criar ou utilizar um subdiretorio de controle `3-Trabalho/` (ou `.travelcut/` na raiz do projeto).
   - O arquivo de definicao do projeto deve ser nomeado `{nome_do_projeto}.travelcut` (formato JSON estruturado).
2. Estrutura do payload salvo no arquivo `.travelcut`:
   ```json
   {
     "version": "1.0.0-beta",
     "projectName": "Chapada dos Veadeiros 2026",
     "rootDirectory": "D:\\Viagens\\Chapada2026",
     "createdAt": "2026-10-30T14:00:00Z",
     "updatedAt": "2026-10-30T14:30:00Z",
     "mediaAssets": [ ... ],
     "settings": {
       "targetFps": 59.94,
       "targetResolution": "3840x2160"
     }
   }
   ```
3. Conecte os botoes de "Salvar Projeto" (Ctrl+S) e "Abrir Projeto Existente" para invocar `save_project_state` e `load_project_state` via `desktopBridge.ts`.

### Tarefa 1.4: Atualizar a Lista de Projetos Recentes na Home
1. Mantenha em cache local do aplicativo (`AppData/travelcut-studio/recent_projects.json`) o historico dos ultimos projetos abertos com seus caminhos e data de ultima modificacao.
2. No componente `RecentProjectsGrid.tsx` e `HeroProjectBanner.tsx`, renderize a lista de projetos reais lidos desse arquivo de historico. Se a lista estiver vazia, exiba um estado amigavel convidando o usuario a criar o primeiro projeto.

---

## 5. Procedimentos de Teste e Validacao
Execute as seguintes verificacoes praticas:

1. **Teste de Selecao de Pasta e Scanner:**
   - Crie uma pasta temporaria de teste contendo ao menos 2 arquivos de video reais (`.mp4` ou `.mov`).
   - Abra o TravelCut Studio e clique em "Novo Projeto".
   - Selecione a pasta criada na janela nativa do Windows.
   - *Resultado esperado:* O aplicativo deve transicionar para a aba `project`, exibindo os 2 videos na grade com duracao e resolucao reais, sem nenhum item ficticio do mock de Roraima.

2. **Teste de Persistencia em Disco:**
   - Altere o nome do projeto no cabecalho e clique em Salvar (ou use o atalho Ctrl+S).
   - Verifique no Windows Explorer se o arquivo `.travelcut` foi gravado corretamente na pasta do projeto com o JSON valido.
   - Feche e reabra o aplicativo: o projeto recem-criado deve aparecer no topo da lista de projetos recentes na tela Home.

3. **Teste de Resiliencia com Pastas Grandes:**
   - Aponte para uma pasta com subdiretorios ou mais de 50 arquivos.
   - *Resultado esperado:* A varredura deve ser assincrona, sem travar a interface React, apresentando um indicador visual de progresso durante o carregamento dos metadados.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] O usuario consegue abrir uma pasta local atraves do seletor nativo do sistema operacional.
- [ ] Os videos sao escaneados e os metadados (resolucao, fps, codec, duracao, tamanho) sao extraidos via FFprobe real.
- [ ] Os `ProjectStatsCards` refletem metricas reais da soma dos arquivos selecionados.
- [ ] O projeto e salvo e carregado com sucesso a partir de arquivo `.travelcut` em disco.
- [ ] A tela inicial (Home) lista os projetos recentemente abertos no sistema sem dados falsos.
- [ ] Nenhuma referencia ativa a `mockData.ts` e utilizada na ingestao de novos projetos.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da:
`ETAPA_02_PLAYER_E_PREVIEW_AUDIOVISUAL.md`.
