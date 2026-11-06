# F12-B — Solicitações e Alinhamento de Contratos com Frente A

**Status:** Nenhum conflito de contrato ou alteração necessária em arquivos de posse da Frente A.

## Auditoria de Interfaces Compartilhadas

A Frente B preservou integralmente a integridade dos contratos e serviços IPC gerenciados pela Frente A:
- `src/TravelCut.Desktop/IpcDispatcher.cs`: Não modificado.
- `frontend/src/services/ipcBridge.ts`: Não modificado.
- `frontend/src/types/ipc.ts`: Não modificado.
- DTOs compartilhados: Nenhum DTO compartilhado com a Frente A foi quebrado ou modificado de forma incompatível.

## Mapeamento por Funcionalidade

1. **Frente B.1 — Importação e Análise de Fala:**
   - Utilizou o contrato existente `StartImportRequest(DetectSpeech: bool)` e `ImportJobStatusDto(SpeechAnalysis: string)`.
   - Utilizou o contrato canônico do pipeline `StartPipelineRequest(ProjectId, MediaIds, Stages, Language, Force)`.
   - Conectou diretamente ao `IPipelineJobManager` injetado via Dependency Injection.
   - Nenhuma nova rota IPC foi necessária.

2. **Frente B.2 — Origens Configuráveis de Mídia:**
   - Utilizou a rota existente `fs.listDirectory` (`ListDirectoryUseCase`), navegando por unidades do Windows (`path == null`) e diretórios arbitrários.
   - Persistência de favoritos e última pasta selecionada implementada via `localStorage` isolado no frontend (`travelcut.import.favorites`, `travelcut.import.lastSource`).
   - Nenhuma alteração de contrato IPC foi necessária.

3. **Frente B.3 — Fechar Projeto:**
   - Operação de ciclo de vida do cliente executada no frontend via `App.tsx` e `TopHeader.tsx`.
   - Limpa o contexto ativo e retorna à tela inicial de boas-vindas sem interferir no banco de dados SQLite ou nos arquivos físicos.
   - Reutiliza a rota existente `project.recent` para atualizar a lista de projetos recentes.
   - Nenhuma alteração de contrato IPC foi necessária.

4. **Frente B.4 — Ícone Nativo Windows:**
   - Gerou o recurso binário multirresolução `app.ico` (16, 24, 32, 48, 64, 128, 256) em `src/TravelCut.Desktop/app.ico`.
   - Configurado no `TravelCut.Desktop.csproj` (`<ApplicationIcon>`) e no `MainWindow.xaml` (`Icon="app.ico"`).
   - Sem impacto em contratos de código ou IPC.
