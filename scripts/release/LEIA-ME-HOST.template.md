# TravelCut Studio {{VERSION}} — pacote de teste para o host

Pacote: `{{PACKAGE}}`
Build UTC: {{BUILD_UTC}}
Runtime: {{RUNTIME}} · self-contained: {{SELF_CONTAINED}}

> **Pacote de teste de desenvolvimento.** Não é release, não é instalador e não é assinado digitalmente.
> Não substitui a homologação no host: o resultado só vale quando o roteiro de `progresso/HOST_TESTS.md` for executado
> no host com evidências. Nenhum teste de DaVinci, Whisper, FFmpeg ou HyperFrames foi provado por este pacote.

## 1. Conteúdo

| Caminho | Conteúdo |
|---|---|
| `app/TravelCut.Desktop.exe` | Aplicativo WPF (shell + WebView2) |
| `app/frontend/dist/` | Interface React já compilada (carregada pelo WebView2) |
| `app/workers/` | `whisper_worker.py`, `resolve_bridge.py`, `requirements.txt`, READMEs |
| `mcp/TravelCut.Mcp.exe` | Servidor MCP (stdio), somente leitura por padrão |
| `tools/host-validation/` | Harness de homologação do bridge DaVinci (roteiro no host) |
| `docs/` | Documentos 10, 14, 18, 19 e 20 |
| `progresso/HOST_TESTS.md` | Formulário e roteiros de homologação (ordem na seção 6) |
| `MANIFEST.json` | Versões, testes executados ou pulados, lista de arquivos com tamanho e SHA-256 |
| `SHA256SUMS.txt` | SHA-256 de todos os arquivos do pacote (exceto ele mesmo) |

**Não incluído (por regra):** vídeos, áudios, pesos de modelos (Whisper `large-v3`), bancos SQLite, caches, `node_modules`,
ambientes Python (`venv`), arquivos `.tcpart`, evidências e projetos DaVinci. A varredura do build falha se encontrar algum deles.

## 2. Requisitos no host

- **Windows 10 ou 11 x64.** Confirme que a edição e o build do host estão cobertos pelo suporte do .NET 10. A VM de desenvolvimento
  (Windows 10 Pro 22H2) **não** é oficialmente suportada pelo .NET 10; os testes finais devem ser feitos em host suportado.
- **Microsoft Edge WebView2 Runtime** (Evergreen). Sem ele o aplicativo não abre a interface.
- **.NET 10 Desktop Runtime:** não é necessário no pacote self-contained. Só para o pacote `-fdd`.
- **FFmpeg ≥ 5.1** (recomendado; obrigatório para análise de áudio, miniaturas e proxies). `ffmpeg.exe` no PATH ou em `TRAVELCUT_FFMPEG_PATH`.
  O `ffprobe` é procurado em `TRAVELCUT_FFPROBE_PATH`, depois ao lado do `ffmpeg` localizado, em pastas comuns do Windows e por fim no PATH (sempre caminho absoluto).
- **Python 3.10 ou 3.11, 64 bits, de python.org** (não o alias da Microsoft Store) para o Whisper. Ambiente isolado do usuário, por exemplo
  `workers\.venv`, conforme `app\workers\README.md`. Instale o `requirements.txt` nesse venv. Não instale globalmente.
- **Whisper `large-v3`:** download manual e autorizado, em `%USERPROFILE%\.cache\whisper\large-v3.pt`. O aplicativo **não** baixa nem troca o modelo.
  Sem ele a transcrição aparece como indisponível.
- **DaVinci Resolve Studio** com *Preferências > Sistema > Geral > External scripting using = Local*. Python 64 bits compatível com a versão do Resolve.
- **Node.js 22+ e HyperFrames** (opcionais; só para animações). O HyperFrames **não** vem no pacote e não é instalado pelo app.
- **Dados do usuário:** banco e perfil do WebView2 em `%LOCALAPPDATA%\TravelCutStudio`; arquivos de trabalho em `3-Trabalho/` de cada projeto.

## 3. Variáveis de ambiente

Defina no nível do usuário (`[Environment]::SetEnvironmentVariable(..., "User")`) e reinicie o TravelCut. Não há valor padrão inventado: ausente = comportamento descrito.

| Variável | Efeito |
|---|---|
| `TRAVELCUT_EDITOR_ADAPTER` | `mock` = simulador (`SIMULATED`, sem `.drp`). Qualquer outro valor ou ausente = adaptador **DaVinci real**. |
| `TRAVELCUT_PYTHON` | `python.exe` 64 bits usado pelo **bridge DaVinci** (`resolve_bridge.py`). Ausente: `py -3`, depois `python` no PATH. |
| `TRAVELCUT_RESOLVE_PATH` | Caminho de `Resolve.exe` para detecção da instalação. |
| `TRAVELCUT_RESOLVE_END_INCLUSIVE` | `true` (padrão) ou `false`: convenção de fim no `AppendToTimeline`. Fixar após o teste do host. |
| `RESOLVE_SCRIPT_API` / `RESOLVE_SCRIPT_LIB` | Pastas do módulo `DaVinciResolveScript`, se o padrão não bater com a instalação. |
| `TRAVELCUT_PYTHON_PATH` | `python.exe` do **Whisper** (venv isolado). Atenção: nome diferente do bridge (`TRAVELCUT_PYTHON`). |
| `TRAVELCUT_FFMPEG_PATH` | Caminho absoluto do `ffmpeg.exe` (arquivo, não pasta). |
| `TRAVELCUT_FFPROBE_PATH` | Caminho absoluto do `ffprobe.exe`, se não estiver ao lado do `ffmpeg`. |
| `TRAVELCUT_MOTION_ADAPTER` | `mock` = animação simulada (`SIMULATED`, nenhum vídeo). Ausente = HyperFrames real. |
| `TRAVELCUT_NODE_PATH` | Caminho absoluto do `node.exe`, se o Node não estiver no PATH. |
| `TRAVELCUT_HYPERFRAMES_CLI` | Caminho absoluto do `.js`/`.exe` do CLI do HyperFrames. Nunca `.cmd`, `.bat` ou `.ps1`. |
| `TRAVELCUT_MCP_PROJECTS` | GUIDs de projetos autorizados ao MCP, separados por `;`. Vazio = nenhum. |
| `TRAVELCUT_MCP_DATABASE` | Banco usado pelo MCP (caminho absoluto). Padrão: o banco do Desktop. Para testes, use um banco descartável. |

Opções de linha de comando do MCP: `--allow-mutations` (habilita ferramentas de escrita; padrão desligado), `--project <guid>`
(repetível), `--migrate-database` (padrão: o MCP não migra; abra o Desktop uma vez). Ajuda: `mcp\TravelCut.Mcp.exe --help`.

## 4. Primeiro uso

1. **Faça backup do banco** antes de abrir a versão nova:
   copie `%LOCALAPPDATA%\TravelCutStudio\` (inclui `travelcut.db`) para outra pasta. Se o banco não existir, não há o que copiar.
   O app também cria `travelcut.db.bak-<utc>` ao migrar um banco antigo.
2. Use **projetos de teste** (pasta com `1-Bruto`, `2-Assets`, `3-Trabalho`, `4-Final`). Nunca aponte para projetos pessoais no primeiro teste.
3. Mídias de origem (`1-Bruto`, `2-Assets`) são somente leitura para o app. Confirme pelo SHA-256 antes e depois.
4. Abra `app\TravelCut.Desktop.exe`. Não há instalação nem registro no sistema.
5. Não presuma conexão MCP pela presença de `.mcp.json`. O MCP só é usado após configuração explícita.

## 5. Verificar integridade

Execute na raiz deste pacote (PowerShell 5.1 ou 7). A saída lista apenas arquivos com divergência:

```powershell
Get-Content .\SHA256SUMS.txt | ForEach-Object {
    $parts = $_ -split '  ', 2
    $real = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PWD $parts[1])).Hash.ToLower()
    if ($real -ne $parts[0]) { 'DIVERGE: ' + $parts[1] }
}
```

Sem saída = todos os hashes conferem. Para o `.zip`, compare com o arquivo `.zip.sha256` ao lado dele (quando gerado).

## 6. Ordem do roteiro no host

Use `progresso/HOST_TESTS.md` e registre `PASS`, `FAIL` ou `NOT_RUN` com versão, passos, resultado, log e captura de tela:

1. **Roteiro mínimo** (abre, WebView2, pastas 1–4, projeto temporário).
2. **F04b/F05** (importação, miniaturas, transcrição, análise de silêncio).
3. **F06** (plano editorial; banco existente e backup).
4. **F07** (revisão, prévia, proxies, timeline).
5. **F08 / DaVinci** (`docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md` e `tools/host-validation/davinci/README.md`).
   Use **somente** mídia sintética e projetos `TravelCut_*`. Salve e feche o seu projeto pessoal antes.
6. **Animações e MCP** (`docs/20-HYPERFRAMES.md` e `docs/19-MCP-TRAVELCUT.md`), quando pertinente.

Ao final, devolva ao desenvolvimento: versões (Windows, Resolve, Python, FFmpeg), o `MANIFEST.json` deste pacote, logs e capturas,
sem credenciais e sem caminhos privados desnecessários. Falhas voltam à fase correspondente para correção na VM.

## 7. Limitações conhecidas

- Binários **não assinados**: o Windows (SmartScreen) ou o antivírus podem alertar. Não desabilite proteções; confira os hashes da seção 5.
- Atualização automática **não** existe neste pacote.
- Sem instalador: a pasta é portátil. Dados ficam em `%LOCALAPPDATA%\TravelCutStudio`.
- A `e_sqlite3.dll` empacotada vem do SQLitePCLRaw 2.1.10, com aviso de segurança NU1903 de gravidade alta (GHSA-2m69-gcr7-jv3q). O banco é local e do próprio usuário; a atualização do pacote está pendente de decisão.
- Os `.pdb` usam caminhos de origem neutros (`/_/`), sem caminhos da máquina de build.
- Nada aqui foi executado no host. Os testes da VM (`mock_pass`) não provam áudio, vídeo, DaVinci nem HyperFrames.
