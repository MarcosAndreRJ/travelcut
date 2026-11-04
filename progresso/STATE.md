# Estado operacional — ponto de retomada

- **Status:** `waiting_for_host` — F11.4 corrigida na VM, merge concluído na branch `main` e pacote `0.10.0-host-test.2` gerado com sucesso para reteste humano no host Windows.
- **Fase atual:** `11 — Homologação no host, correções e fechamento do MVP (F11.4: Correção da tela preta na navegação de projeto e geração do pacote 2)`. Branch ativa: `main`.
- **Causa-raiz sanada:**
  1. Contrato IPC `media.list`: backend C# retornava `{ media: dtos }` enquanto frontend em `LibraryTranscriptionView.tsx` e `ipcBridge.ts` esperavam array direto. A tentativa de executar `assets.filter(...)` causava `TypeError: assets.filter is not a function`, que sem Error Boundary desarmava toda a árvore React, deixando a tela preta.
  2. Ausência de Error Boundary: criado componente `ErrorBoundary.tsx` de classe React protegendo tanto a raiz (`main.tsx`) quanto as views dinâmicas (`App.tsx`), capturando erros de renderização com fallback visual e botão de recuperação.
  3. Divergências de canais IPC: adicionados aliases no `IpcDispatcher.cs` para `media.details`/`media.getDetails`, `media.chronology`/`media.getChronology` e suporte a `thresholdSeconds`/`sessionGapMinutes`.
  4. Instrumentação de erros JS no WebView2: adicionado script de escuta de `window.error` e `unhandledrejection` no `MainWindow.xaml.cs` que encaminha exceções JS para `_logger.LogError(...)` no host nativo WPF.
  5. Camada defensiva no frontend: `ipcBridge.ts` aceita array ou objeto encapsulado; `LibraryTranscriptionView.tsx` trata erro de rede/IPC exibindo banner com botão de retry sem travar o componente.
- **Evidências de validação na VM:**
  - `Build-Release.ps1 -Zip`: execução completa em 201s sem erros.
  - `dotnet test tests/TravelCut.UnitTests/TravelCut.UnitTests.csproj` (Release): 1050/1050 testes passando (100% de sucesso).
  - `dotnet test tests/TravelCut.Mcp.Tests/TravelCut.Mcp.Tests.csproj` (Release): 107/107 testes passando (100% de sucesso).
  - `npm test` (Vitest): 182/182 testes passando (100% de sucesso).
  - `npx tsc --noEmit`: 0 erros de TypeScript.
  - `npm run build`: bundle de produção gerado com versão `0.10.0-host-test.2` verificada no asset JS.
  - Publicação self-contained win-x64 de `TravelCut.Desktop.exe` e `TravelCut.Mcp.exe` concluída sem violações de path ou caminhos de máquina.
- **Pacote para o host:** `dist/TravelCutStudio-0.10.0-host-test.2-win-x64/` e `dist/TravelCutStudio-0.10.0-host-test.2-win-x64.zip` (104 MB).
  - **SHA-256 do ZIP:** `0886b342d117e8a88c33374e667d6a83cea3df07a37ef86bd0491484db5b6722`.
- **Último commit em main:** `7ed80f2` (branch `main`).
- **Próxima ação (humana, no host):**
  1. Copiar `dist/TravelCutStudio-0.10.0-host-test.2-win-x64.zip` para o host e validar o hash SHA-256.
  2. Extrair e executar `TravelCut.Desktop.exe`.
  3. Validar se a abertura/criação de projetos navega normalmente para a tela Biblioteca & Transcrição (T02) sem tela preta.
  4. Executar os demais itens do roteiro em `progresso/HOST_TESTS.md`.
- **Decisões pendentes do usuário (não bloqueiam o teste no host):** confirmar ADR-014 (desfazer em pilha; corte automático só silêncio) e ADR-015 (bridge Python por comando; só projetos novos `TravelCut_*`); ativar `.mcp.json` no projeto; política de `get_transcript` (transcrição completa ao cliente MCP); atualizar SQLitePCLRaw (NU1903 alta); autorizar testes de UI com jsdom/Testing Library (dívida da exceção F07).
- **Riscos conhecidos:** `docs/23-LIMITACOES-E-RISCOS.md` (inclui banco legado real com migração e backup, WAL, .NET 10 em Windows 10 22H2 sem suporte oficial, sintaxe do CLI HyperFrames suposta, convenção de endFrame do Resolve a confirmar).
- **Limpeza pendente (usuário):** `K:\scratch_zip.txt` (listagem temporária criada pelo auditor; a remoção automática foi barrada pela checagem de segurança).
- **Ambiente VM (verificado 2026-11-02):** sem ffmpeg/ffprobe, sem Python, sem DaVinci; Node 24 presente.
- **Progresso persistente:** `progresso/CHECKLIST.md` e `progresso/IMPLEMENTATION_LOG.md`.
