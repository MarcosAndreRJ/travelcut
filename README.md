# TravelCut Studio

TravelCut Studio é uma ferramenta de desbaste e decupagem assistida para projetos de viagem. Ele importa a mídia para uma estrutura de pastas do projeto, analisa silêncio e fala, gera candidatos a corte com evidência, monta um plano editorial revisável e pode enviar a montagem ao DaVinci Resolve Studio. Decisões são humanas, e os arquivos originais não são alterados.

**Versão atual:** `0.10.0-host-test.1` — pacote de teste de desenvolvimento para o host. Não é release, não é instalador e não é assinado.

**Estado de validação:** o código e os testes automatizados passam na VM de desenvolvimento como `mock_pass`. Integrações reais (DaVinci, FFmpeg, Whisper, WebView2 no host, HyperFrames, MCP com o app aberto) estão `host_pending`. Ver [docs/23](docs/23-LIMITACOES-E-RISCOS.md).

## O que o app faz hoje

- Abre ou cria projetos com as 4 pastas canônicas, com tela de boas-vindas e recentes reais.
- Importa mídia por seletor ou arrastar e soltar, copiando para `1-Bruto`/`2-Assets` de forma aditiva e verificada por SHA-256 ([ADR-013](docs/15-DECISOES-ARQUITETURAIS.md)).
- Analisa cada mídia: metadados via FFprobe, silêncios via FFmpeg, transcrição via worker Whisper `large-v3` quando instalado. Sem a ferramenta, o status aparece como indisponível, com motivo.
- Apresenta candidatos a corte com evidência, para aprovar, rejeitar, adiar, modificar, desfazer e refazer.
- Gera um plano editorial imutável por revisão, em V1/A1/A2, com seções-hipótese (começo, meio, fim).
- Oferece prévia por `<video>` do WebView2 (a reprodução depende do codec no host), forma de onda e proxies gerados pelo FFmpeg.
- Envia a montagem ao DaVinci em duas etapas: dry-run e depois aplicação, sempre em projeto novo `TravelCut_*`. Não há fallback silencioso para simulador.
- Expõe um servidor MCP (stdio) com 8 ferramentas de leitura. Mutações só existem com `--allow-mutations`.
- Gera animações HyperFrames (`title_card`, `lower_third`, `map_route`) quando o Node e o CLI estão configurados.

O TravelCut não é um NLE: a montagem final é feita no DaVinci ([ADR-008](docs/15-DECISOES-ARQUITETURAIS.md)).

## Estrutura do repositório

```text
TravelCutStudio/
├── TravelCutStudio.sln          # solução (src + tests)
├── Directory.Build.props        # versão única (TravelCutProductVersion)
├── src/
│   ├── TravelCut.Domain/        # entidades, timebase e regras de plano (sem UI, Python, DaVinci ou MCP)
│   ├── TravelCut.Contracts/     # DTOs e mensagens IPC/MCP tipadas
│   ├── TravelCut.Application/   # casos de uso, portas (interfaces) e IpcDispatcher
│   ├── TravelCut.Infrastructure/# SQLite/EF Core, FFmpeg/FFprobe, Whisper, HyperFrames, arquivos e logs
│   ├── TravelCut.ResolveBridge/ # adaptador DaVinci (subprocesso Python) e simulador MOCK
│   ├── TravelCut.Desktop/       # shell WPF + WebView2 (carrega frontend/dist)
│   └── TravelCut.Mcp/           # servidor MCP stdio (fachada sobre o mesmo IPC)
├── frontend/                    # React 19 + TypeScript + Vite + Tailwind (única implementação das telas)
├── workers/                     # resolve_bridge.py, whisper_worker.py, hyperframes/ e tests/
├── tools/host-validation/davinci/ # harness PowerShell de homologação do bridge (mídia sintética)
├── scripts/release/             # Build-Release.ps1 e LEIA-ME-HOST.template.md
├── tests/
│   ├── TravelCut.UnitTests/     # xUnit: domínio, aplicação, infraestrutura e fim a fim (EndToEnd)
│   └── TravelCut.Mcp.Tests/     # testes do MCP, incluindo smoke stdio real
├── docs/                        # especificação técnica (índice em docs/00-INDICE.md)
├── progresso/                   # STATE, CHECKLIST, IMPLEMENTATION_LOG e HOST_TESTS (operação)
├── prompts/ e .claude/          # fases de execução, agentes e skills
├── referencias/mockups/         # mockups de referência visual
└── dist/                        # pacotes gerados (no .gitignore; esta cópia não está sob Git)
```

## As 4 pastas de cada projeto

| Pasta | Regra |
|---|---|
| `1-Bruto/` | Originais da câmera, drone e gravador. **Somente leitura.** Única escrita permitida: importação aditiva (ADR-013): arquivo novo em `.tcpart`, SHA-256 relido, renomeação. Nunca sobrescreve, move ou apaga. |
| `2-Assets/` | Trilhas, efeitos, logotipos, fontes e mapas. Mesma regra de `1-Bruto/`. |
| `3-Trabalho/` | Tudo que o app gera: cache (miniaturas, proxies, forma de onda), análises, planos (`plans/`), editor (`editor/`), animações (`motion/`) e travas (`.locks/`). Temporários e análises. |
| `4-Final/` | Masters aprovados. O código apenas verifica e cria a pasta; não encontrei gravação nela. |

Operações de leitura e análise não alteram os originais (RNF-01). Logs e cache podem ser limpos sem perder a trilha da origem.

## Compilar e testar

Pré-requisitos: Windows, .NET 10 SDK (testado com 10.0.401), Node.js 22+ (testado com 24.12.0) e WebView2 Runtime para executar a interface.

```powershell
dotnet build TravelCutStudio.sln
dotnet test TravelCutStudio.sln

cd frontend
npm ci
npm test
npm run build
```

Para rodar o app a partir do código, gere `frontend/dist` com `npm run build` antes de `dotnet run --project src/TravelCut.Desktop/TravelCut.Desktop.csproj`.

Não mantenha um servidor Vite (`npm run dev`) aberto durante `npm ci` ou o build de release: o `esbuild.exe` bloqueado causa `EPERM` e deixa `node_modules` incompleto.

## Gerar o pacote de teste

Na raiz do projeto (duplo clique em `deploy.cmd` também funciona):

```powershell
.\deploy.ps1
```

Opções: `-SemTestes` (pula `dotnet test`), `-SemZip` (só a pasta) e `-FrameworkDependent` (sem o runtime .NET embutido). O `deploy.ps1` chama `scripts\release\Build-Release.ps1 -Zip`, que também pode ser usado diretamente:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\release\Build-Release.ps1 -Zip
```

Saídas em `dist\TravelCutStudio-<versão>-win-x64\`, com `.zip` e `.zip.sha256`, `MANIFEST.json`, `SHA256SUMS.txt` e log em `dist\logs\`. O pacote é self-contained, não é assinado e não contém mídia, pesos de modelo, bancos, caches nem `node_modules`. Procedimento completo: [docs/14](docs/14-BUILD-DEPLOY-HOST.md).

## Estado de validação (2026-11-03)

| Área | Situação |
|---|---|
| Testes automatizados (build Release do pacote) | UnitTests 1043/1043, Mcp.Tests 107/107 (`mock_pass`). Frontend `npm test` 162/162 (registro de 01:03). |
| Não coberto por testes | Componentes, teclado e fluxo visual da interface (exceção aceita em F07). |
| `host_pending` | App WPF e WebView2 no host (seletor, arrastar e soltar, prévia, codecs); FFmpeg e FFprobe reais; Whisper `large-v3`; bridge DaVinci Resolve real; HyperFrames; Desktop e MCP ao mesmo tempo. |
| Pendências de release | F10.4 (cópia e instalação no host, aguarda autorização), F10.6 (auditoria final do pacote e da UI), F11 (homologação no host). |

Nada deste estado é homologação no host. Para homologar, siga [docs/22-SMOKE-TEST-HOST.md](docs/22-SMOKE-TEST-HOST.md) e registre as evidências em `progresso/HOST_TESTS.md`.

## Configuração

- Variáveis de ambiente e requisitos do host: `scripts/release/LEIA-ME-HOST.template.md` (seção 3) e [docs/14](docs/14-BUILD-DEPLOY-HOST.md).
- Não há `.mcp.json` ativo no repositório. Só o modelo `.mcp.json.example` é entregue, e a ativação depende de decisão do usuário ([docs/19](docs/19-MCP-TRAVELCUT.md)).

## Documentação

- [00 — Índice técnico](docs/00-INDICE.md)
- [14 — Build, pacote e host](docs/14-BUILD-DEPLOY-HOST.md)
- [15 — Decisões arquiteturais (ADRs)](docs/15-DECISOES-ARQUITETURAIS.md)
- [18 — Homologação do bridge DaVinci](docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md)
- [19 — Servidor MCP](docs/19-MCP-TRAVELCUT.md)
- [20 — HyperFrames](docs/20-HYPERFRAMES.md)
- [21 — Notas de versão](docs/21-NOTAS-DE-VERSAO.md)
- [22 — Smoke test no host](docs/22-SMOKE-TEST-HOST.md)
- [23 — Limitações e riscos](docs/23-LIMITACOES-E-RISCOS.md)

Para agentes de IA que trabalham neste repositório, as instruções operacionais estão em `CLAUDE.md` e `prompts/00-INICIAR-AQUI.md`.

## Licença e autoria

Desenvolvido para o ecossistema TravelCut Studio. Todos os direitos reservados.
