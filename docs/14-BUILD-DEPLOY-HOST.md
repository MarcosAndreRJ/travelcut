# Build, empacotamento e validação no host

## Requisitos de desenvolvimento

.NET 10 SDK e Desktop Runtime, Node.js 22+, npm, WebView2 Runtime, WPF/Windows SDK, ferramentas de build C# compatíveis, acesso a FFmpeg/ffprobe; Python workers em env isolado se suportados. **Não presumir que a VM com Windows 10 Pro 22H2 está oficialmente suportada por .NET 10:** verificar capacidade de build separadamente e homologar em Windows host suportado. Pouco espaço C: na VM → não instalar indiscriminadamente em `C:`; planejar uso K: com folga real.

## Build reprodutível

1. Inventariar `dotnet --info`, `node --version`, `npm --version`, SO/SDK e diretorios.
2. Restaurar dependências a partir de lockfiles; compilar projetos em `Release` e rodar testes.
3. `npm ci` para frontend se `package-lock.json` existir, build Vite, integrar dist no WPF (ou pipeline equivalente documentado).
4. Publicar WPF x64 com manifesto de runtime e hashes; não copiar `node_modules`, arquivos de mídia ou pesos de modelo.
5. Empacotar com instalador adequado a Windows e Microsoft WebView2 Runtime (bootstrap ou pré-requisito, sem bundling silencioso indevido).
6. Rodar smoke test e gerar `release-manifest.md` com versões, hashes, passos host.

## Localização de dados

Binários/instalador separados de projetos; dados do usuário em diretório gerenciado; referência explícita à pasta projeto 1–4. Não escrever em `Program Files` em tempo de execução. Logs e cache dimensionados/limpáveis; trilha de arquivo fonte até artefato final sempre preservada.

## Teste host

Host manualmente instala/abre app, informa caminho de projeto de teste, valida WebView2/player, Worker Python/FFmpeg, consulta Resolve aberto, cria projeto temporário, importa mídias, aplica cortes, salva `.drp` e reabre. Registrar versão Resolve, Windows, codecs, resultados e logs. Não presumir conexão MCP por presença de arquivo de config. Problemas retornam à fase correspondente com regressão testada na VM.

## Distribuição

Inicialmente `.zip` portable/self-contained ou instalador local para homologação; atualizações automáticas ficam fora do MVP. Executáveis assinados quando houver distribuição ampla; não ignorar SmartScreen/antivírus nem desabilitar proteções. Não realizar push/release remoto sem autorização explícita.

## Procedimento real do pacote de teste (F10.3)

**Estado:** build final concluído na VM em 2026-11-03 03:30 -03:00 (ver abaixo). Nada aqui é homologação no host.

### Versão e configuração

- Versão única: `<TravelCutProductVersion>` em `Directory.Build.props` (raiz), atual `0.10.0-host-test.1`. Aplica-se a `Version`/`InformationalVersion`/`Company`/`Product` de todos os projetos.
- `Deterministic=true` (explícito). `ContinuousIntegrationBuild=true` **somente** com `TRAVELCUT_CI=1`. Não ativado por padrão: o repositório não tem `.git`, então os caminhos não são normalizados por controle de versão.

### Comando

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\release\Build-Release.ps1 -Zip
```

Parâmetros: `-Configuration Release` e `-Runtime win-x64` (únicos valores aceitos); `-SelfContained:$true` (padrão; `$false` gera sufixo `-fdd`, framework-dependent); `-OutputRoot dist` (padrão; caminho UNC é recusado); `-SkipTests`; `-Zip`.

Diagnóstico (usar só para isolar falhas; o manifesto marca `reproducible: false` e lista o motivo):
`-SkipFrontendInstall` (não roda `npm ci`/`install`), `-SkipFrontendBuild` (reaproveita `frontend/dist` existente).

### Etapas

| Etapa | Ação | Falha se |
|---|---|---|
| a | Checa `dotnet` (SDK 10+), `node` (22+) e `npm`; versões no log | ferramenta ausente ou versão menor |
| b | `npm ci` (ou `npm install` sem `package-lock.json`, registrado como aviso) e `npm run build` em `frontend/` | build falha ou `frontend/dist/index.html` não existe |
| c | `dotnet test TravelCutStudio.sln -c Release` | qualquer teste falha (ou pulado com `-SkipTests`, registrado) |
| d | `dotnet publish` de `TravelCut.Desktop` → `app/` e `TravelCut.Mcp` → `mcp/` (`win-x64`, self-contained) | publish falha ou falta `app/TravelCut.Desktop.exe`, `app/frontend/dist/index.html`, `app/workers/resolve_bridge.py`, `mcp/TravelCut.Mcp.exe` |
| e | Copia `workers/` (`resolve_bridge.py`, `whisper_worker.py`, `requirements.txt`, READMEs) para `app/workers/`; `tools/host-validation/` (sem `evidencias`); docs 10/14/18/19/20 e `progresso/HOST_TESTS.md` | arquivo obrigatório ausente |
| f | Varredura: falha se houver mídia, pesos, `.db`, `.tcpart`, `.drp`, `node_modules`, `venv`, caches, `evidencias`, `.bin` > 5 MB ou arquivo > 100 MB | qualquer item encontrado |
| g | `MANIFEST.json` (produto, versão, UTC, commit "nao versionado em git", runtime, self-contained, versões, testes, arquivos com tamanho e SHA-256) e `SHA256SUMS.txt` | — |
| h | `LEIA-ME-HOST.md` a partir de `scripts/release/LEIA-ME-HOST.template.md` | — |
| i | Com `-Zip`: `<pacote>.zip` e `<pacote>.zip.sha256` em `dist/` | — |

Saídas: `dist/TravelCutStudio-<versão>-win-x64/`, log em `dist/logs/build-<UTC>.log`. `dist/` está no `.gitignore`.

### Decisões de publicação

- **Self-contained, sem `PublishSingleFile`, sem ReadyToRun, sem trimming.** O WPF carrega `WebView2Loader` nativo e o app lê `frontend/dist`, `workers/*.py` e o MCP lê `workers/resolve_bridge.py` como arquivos ao lado do executável. Single-file exigiria extração temporária e acrescentaria risco de falha no host sem ganho para a homologação. Trimming pode remover código usado por reflexão/XAML sem teste no host.
- **Não assinado.** Executáveis podem ser alertados pelo SmartScreen; não desabilitar proteções.
- **`whisper_worker.py` não é `Content` do csproj**; o script copia para `app/workers/` (o app o procura em `<app>\workers\`).

### Pré-requisitos de execução

- **Nenhum processo pode manter arquivos de `frontend/node_modules` abertos.** Um `npm run dev` (Vite) ativo trava `@esbuild/win32-x64/esbuild.exe`: o `npm ci` falha com `EPERM` e **deixa `node_modules` parcialmente removido**. Encerrar o dev server antes do build (ação do dono do processo).
- Não rodar `dotnet test`/`publish` em paralelo com outro build do mesmo repositório.

### Estado da última execução (2026-11-03 03:30 -03:00, VM Windows 10 Pro 22H2)

- `Build-Release.ps1 -Zip` completo, sem pulos: `npm ci`, `npm run build` (versão 0.10.0-host-test.1 conferida no bundle), `tsc --noEmit` 0 erros, `dotnet test -c Release` (UnitTests 1044/1044, Mcp.Tests 107/107), publish self-contained win-x64 de Desktop e MCP.
- Build determinístico com `TRAVELCUT_CI=1` (definido pelo script): `ContinuousIntegrationBuild` + `PathMap` nos projetos de `src/` → DLL/PDB sem caminhos da máquina de build (conferido por busca no pacote). Os testes não usam `PathMap` (localizam fixtures pelo caminho do arquivo).
- `MANIFEST.json`: `reproducible` = `unverified` (entradas fixadas e build determinístico, mas sem git não há hash de fonte e dois builds não foram comparados).
- Pacote: 713 arquivos no manifesto, varredura sem violações; zip ≈ 103,9 MiB. O SHA-256 do zip fica em `<pacote>.zip.sha256` e em `docs/21-NOTAS-DE-VERSAO.md` (não neste arquivo, que vai dentro do pacote).
- Incidentes resolvidos: `npm ci` com EPERM enquanto um servidor Vite estava aberto (encerrar o dev server antes do build); falha intermitente de `EndToEndMigrationTests` não reproduziu (build intermediário durante edição concorrente).
- Aviso de segurança aberto: `NU1903` em `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (GHSA-2m69-gcr7-jv3q, alta) — atualização pendente de decisão.

### Verificação de integridade

Ver o trecho de PowerShell em `LEIA-ME-HOST.md` do próprio pacote (seção 5). Não copiar para `Z:` nem outro local de rede sem autorização explícita do usuário.
