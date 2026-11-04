# F12-B — Relatório de Execução do Fluxo e Windows

**Data de Execução:** 2026-11-04
**Branch:** `feat/f12-fluxo-windows`
**Escopo:** Frentes B.1, B.2, B.3 e B.4

---

## 1. Visão Geral das Entregas

A Frente B realizou as melhorias do fluxo de trabalho e experiência nativa Windows no TravelCut Studio sem impactar as interfaces compartilhadas da Frente A.

| Frente | Descrição | Status | Testes |
|---|---|---|---|
| **B.1** | Conexão real da opção "Detectar cenas com fala" ao `IPipelineJobManager` | Concluído | 89 testes unitários aprovados (+3 novos testes específicos) |
| **B.2** | Navegador de origens configuráveis, favoritos, unidades do Windows e inclusão recursiva | Concluído | 187 testes de frontend aprovados (+5 novos testes de origens) |
| **B.3** | Ação "Fechar projeto" no cabeçalho com isolamento de contexto e proteção assíncrona | Concluído | Build e testes de interface validados |
| **B.4** | Ícone nativo multirresolução (16x16 até 256x256) na janela, barra de tarefas, Alt+Tab e .exe | Concluído | Compilação do executável WPF com ícone embutido |

---

## 2. Detalhamento por Frente

### Frente B.1 — Importação e Análise de Fala
- **Auditoria de `MediaImportService.cs`:**
  - Identificada a dependência anterior que apenas registrava uma nota estática de indisponibilidade ("nada foi enfileirado").
  - Conexão realizada através da injeção de `IPipelineJobManager` via Dependency Injection.
  - No `StartAsync`: quando `DetectSpeech == true`, o serviço consulta `GetEnvironmentAsync()`. Se o Whisper estiver indisponível (ex.: dependências ausentes), marca `speechAnalysis = "unavailable"` com a causa detalhada reportada pelo ambiente. Se disponível, marca `speechAnalysis = "queued"`.
  - No `ExecuteJobAsync`: após a conclusão da cópia e do registro das mídias, se `DetectSpeech == true` e `SpeechAnalysis == "queued"`, o serviço invoca `pipeline.StartAsync(new StartPipelineRequest(ProjectId, MediaIds, Stages: null, Language: "pt", Force: false))`.
  - **Preservação rigorosa de distinções:**
    - Arquivo importado: `ImportItemResults.Imported` ou `RegisteredInPlace`.
    - Metadados extraídos: estágio `metadata`.
    - Miniaturas geradas: estágio `thumbnails`.
    - Detecção de fala agendada: `speechAnalysis = "queued"`.
    - Detecção de fala executada: execução assíncrona desacoplada conduzida pelo `PipelineJobManager`.
    - Transcrição concluída: mantida sob responsabilidade do worker Whisper no pipeline; nunca marcada precipitadamente pela importação.
    - Dependência indisponível: `speechAnalysis = "unavailable"` com notas elucidativas.
    - O pipeline existente não foi duplicado.

### Frente B.2 — Origens Configuráveis de Mídia
- **Navegador de Arquivos (`FileNavigator.tsx`):**
  - **Desacoplamento do diretório pai:** A navegação inicial agora respeita a última origem utilizada ou as unidades reais do Windows, sem forçar a abertura no diretório pai do projeto.
  - **Memória de última origem:** Persistência no `localStorage` sob a chave `travelcut.import.lastSource`.
  - **Pastas favoritas de mídia:** Sistema de favoritos persistidos em `localStorage` (`travelcut.import.favorites`), com atalhos padrão ("Unidades do Windows", "Pasta do Projeto", "Câmera / DCIM") e capacidade de fixar/remover qualquer pasta atual através do botão "+ Fixar pasta".
  - **Navegação por unidades reais:** Botão dedicado "Unidades" na barra de ferramentas e no seletor rápido, navegando para `null` e acionando `ipc.listDirectory(null)`.
  - **Adição de pastas com indicação recursiva:**
    - Botão explícito "+ Pasta (recursivo)" em cada linha de diretório, com tooltip explicativo informando a varredura recursiva de subpastas.
    - Botão de destaque "+ Adicionar esta pasta inteira (recursivo)" na barra superior do navegador quando dentro de um diretório.
  - **Adição de arquivos:** Botão individual "+ Adicionar" e duplo clique.
  - **Segurança dos arquivos originais:** Aviso de segurança no rodapé do navegador garantindo leitura segura sem alteração de arquivos de origem.

### Frente B.3 — Fechar Projeto
- **Ação no Cabeçalho (`TopHeader.tsx`):**
  - Adicionado o botão "Fechar projeto" ao lado de "Trocar projeto".
  - Estilização com destaque suave e confirmação de propósito.
- **Ciclo de Vida no Cliente (`App.tsx`):**
  - Encerra apenas o contexto ativo do projeto (`setProject(null)`).
  - Retorna a tela principal para as boas-vindas / integração (`setCurrentTab('onboarding')`).
  - Limpa estados transitórios (sugestões, erros de revisão, prompts e modais).
  - Preserva intactos o banco de dados SQLite, arquivos do projeto e decisões já salvas.
  - Proteção de concorrência com `activeProjectIdRef`: respostas assíncronas de consultas anteriores a `ipc.listSuggestions` ou outros endpoints descartam atualizações se o projeto ativo tiver sido fechado ou trocado.
  - Reabertura facilitada: recarrega a lista de projetos recentes através de `loadRecentProjects()`.

### Frente B.4 — Ícone Windows Multirresolução
- **Script Gerador (`scripts/generate_app_icon.js`):**
  - Criação de recurso `.ico` nativo Windows contendo 7 camadas de resolução em conformidade com as diretrizes da Microsoft: 16x16, 24x24, 32x32, 48x48, 64x64, 128x128 e 256x256.
  - Fidelidade à identidade visual do TravelCut Studio: gradiente Ciano `#06B6D4` a Azul `#2563EB` com emblema geométrico "TC" e chanfro cinematográfico.
  - Geração de arquivos em:
    - `src/TravelCut.Desktop/app.ico` (binário multi-camada)
    - `frontend/public/favicon.ico`
    - `frontend/public/icon-256.png`
- **Configuração WPF e Windows Executable:**
  - `TravelCut.Desktop.csproj`: Inclusão de `<ApplicationIcon>app.ico</ApplicationIcon>` e `<Resource Include="app.ico" />`.
  - `MainWindow.xaml`: Inclusão de `Icon="app.ico"`.
  - `frontend/index.html`: Inclusão de `<link rel="icon" type="image/x-icon" href="/favicon.ico" />`.
  - Garantia de exibição na janela, barra de tarefas, Alt+Tab, atalhos do sistema e ícone embutido no executável `TravelCut.Desktop.exe`.

---

## 3. Resumo dos Testes e Validação

- **Testes de Backend (.NET 10):**
  - `TravelCut.UnitTests`: 1053 testes aprovados (100% de sucesso).
  - `TravelCut.Mcp.Tests`: 107 testes aprovados (100% de sucesso).
- **Testes de Frontend (Vitest / TypeScript / Vite):**
  - 187 testes aprovados (100% de sucesso).
  - Build de produção (`vite build`) finalizado com sucesso.
- **Compilação do Executável Windows (WPF):**
  - `dotnet build TravelCut.Desktop.csproj` gerou `TravelCut.Desktop.exe` com sucesso e zero erros.
