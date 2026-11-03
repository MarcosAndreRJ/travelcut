# Worker Whisper (TravelCut Studio)

Worker Python isolado que transcreve fala em português com **openai-whisper** e o modelo **`large-v3`**,
com timestamps por palavra. O app (.NET) conversa com ele por stdin/stdout em NDJSON
(protocolo `travelcut.whisper`, versão 1).

## Regras

- O modelo é sempre `large-v3`. O worker **não troca** por outro modelo.
- O worker **não baixa** modelos. Se `large-v3.pt` não estiver no cache, a transcrição retorna
  `model_not_available` e o app mostra o estado como indisponível.
- Sem Python, sem openai-whisper ou sem ffmpeg, o app mostra "indisponível". Nada é estimado.
- Não instale dependências globais. Use o ambiente isolado abaixo.

## 1. Pré-requisitos no host (Windows)

- Python 3.10 ou 3.11 instalado de python.org (o alias da Microsoft Store **não** serve).
  Confira com `py -3 --version`.
- **ffmpeg** no PATH (o openai-whisper usa ffmpeg para decodificar o áudio do vídeo).
  Confira com `ffmpeg -version`.
- Para GPU NVIDIA: driver atualizado. Sem CUDA o worker roda em CPU (lento para `large-v3`).

## 2. Ambiente isolado

Execute na raiz do projeto (ou ajuste os caminhos):

```powershell
py -3.11 -m venv workers\.venv
workers\.venv\Scripts\python.exe -m pip install --upgrade pip
```

Instale o torch escolhendo a build (ver https://pytorch.org):

```powershell
# CPU
workers\.venv\Scripts\python.exe -m pip install torch --index-url https://download.pytorch.org/whl/cpu
# NVIDIA CUDA (exemplo cu121; ajuste ao driver)
workers\.venv\Scripts\python.exe -m pip install torch --index-url https://download.pytorch.org/whl/cu121
```

Depois instale as dependências do worker:

```powershell
workers\.venv\Scripts\python.exe -m pip install -r workers\requirements.txt
```

Alternativa: defina `TRAVELCUT_PYTHON_PATH` com o caminho do `python.exe` de outro ambiente isolado.
O app usa essa variável antes de procurar `workers\.venv`.

## 3. Modelo large-v3 (download manual, requer autorização)

O download pesa vários GB e **não é automático**. Após autorização do responsável:

```powershell
workers\.venv\Scripts\python.exe -c "import whisper; whisper.load_model('large-v3')"
```

O arquivo fica em `%USERPROFILE%\.cache\whisper\large-v3.pt`. Não versione esse arquivo.

## 4. Verificação rápida

```powershell
echo {"command":"probe","requestId":"t1","payload":{}} | workers\.venv\Scripts\python.exe workers\whisper_worker.py
```

Esperado: uma linha JSON com `"protocol":"travelcut.whisper"`, `"ok":true` e `result` contendo
`whisperInstalled`, `cuda`, `device` e `modelCached`. O probe nunca baixa modelo.

## 5. Protocolo (resumo)

Entrada (uma linha por comando):

```json
{"command":"transcribe","requestId":"abc","payload":{"mediaPath":"C:\...\clip.mp4","model":"large-v3","language":"pt"}}
```

Saída de sucesso:

```json
{"protocol":"travelcut.whisper","version":1,"requestId":"abc","ok":true,
 "result":{"text":"...","audio_duration_secs":12.345,
  "words":[{"text":"vamos","start":0.12,"end":0.48,"probability":0.93}],
  "engine":"openai-whisper 20240930","model":"large-v3","language":"pt"}}
```

Códigos de erro: `model_not_available`, `ffmpeg_not_found`, `whisper_not_installed`, `file_not_found`,
`invalid_request`, `transcription_failed`, `unknown_command`, `invalid_json`.

## 6. Estado de validação

- Worker escrito e revisado por leitura. **Não executado** na VM de desenvolvimento (sem Python, sem CUDA).
- Teste real com large-v3 no host: `host_pending`. Ver `progresso/HOST_TESTS.md`.

---

## Bridge DaVinci (`resolve_bridge.py`, protocolo `resolve-bridge/1`)

Bridge stdin/stdout para o DaVinci Resolve Studio. O app .NET o chama por `ProcessRunner`
(`src/TravelCut.ResolveBridge/DaVinciResolveAdapter.cs`). Um pedido JSON entra pelo stdin e uma resposta JSON sai pelo stdout.
Logs vão só para o stderr.

**Dependências:** somente stdlib e o módulo `DaVinciResolveScript` da instalação do Resolve. Não usa `requirements.txt`.

**Módulo do Resolve:** usa `RESOLVE_SCRIPT_API` e `RESOLVE_SCRIPT_LIB` do ambiente. Sem essas variáveis, usa
`%PROGRAMDATA%\Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting` e
`C:\Program Files\Blackmagic Design\DaVinci Resolve\fusionscript.dll`.

**Configuração do app (variáveis de ambiente):**

| Variável | Padrão | Uso |
|---|---|---|
| `TRAVELCUT_PYTHON` | (nenhum) | `python.exe` 64 bits para o bridge. Sem ela: `py -3`, depois `python` no PATH |
| `TRAVELCUT_RESOLVE_PATH` | `%ProgramFiles%\Blackmagic Design\DaVinci Resolve\Resolve.exe` | Detecção de instalação |
| `TRAVELCUT_RESOLVE_END_INCLUSIVE` | `true` | `false` usa fim exclusivo no `AppendToTimeline` (confirmar no host) |

**Comandos:** `probe`, `apply_assembly`, `query_timeline`, `export_project`, `render_preview`.

- `probe`: versão, produto, métodos presentes e projeto aberto (apenas para aviso; nada é salvo ou alterado).
- `apply_assembly`: cria projeto **novo** `TravelCut_*`, timeline, aplica os cortes do manifesto
  (`editor-assembly/1`), consulta a timeline, salva e, se pedido, exporta snapshot `.drp`. Devolve `steps`,
  `clipMappings`, `rejected`, `warnings` e `status` (`READY_REAL`, `PARTIAL_REAL` ou `ERROR`).
- `query_timeline`, `export_project`, `render_preview`: só atuam se o projeto **atual** do Resolve for exatamente o
  pedido e começar com `TravelCut_` (senão `PROJECT_NOT_CURRENT`). Nunca trocam de projeto.
- `apply_assembly`, `export_project`, `render_preview`: exigem `allowedOutputRoot` (`<projeto>\3-Trabalho\editor`).
  Snapshot, `.drp` e prévia devem ficar dentro dessa raiz (senão `PERMISSION_DENIED`). Mídia só é aceita em
  `<projeto>\1-Bruto` ou `<projeto>\2-Assets` (senão `PERMISSION_DENIED` por clipe).

**Regras de segurança:** nunca abre nem altera projeto existente para escrita. Caminhos aparecem apenas nos campos
de dados, nunca nas mensagens de erro (só o nome do arquivo). Qualquer exceção vira `BRIDGE_EXCEPTION`.
Testes Python: `python -m unittest discover -s tests -v` a partir de `workers/` (com módulo `DaVinciResolveScript` falso
injetado; ainda não executados na VM de desenvolvimento, que não tem Python).

**Testes (no host, a partir de `workers/`):**

```powershell
python -m unittest discover -s tests -v
```

Os testes cobrem conversão de frames (fim exclusivo/inclusivo), casamento de itens, validação de manifesto e nome de
projeto, status final, recusa de projeto sem prefixo e "Resolve ausente" (sem precisar do Resolve).

**Estado de validação:** escrito e revisado por leitura; **não executado** na VM (sem Python e sem Resolve). Execução no host
(`host_pending`): roteiro em `docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md`, kit em `tools/host-validation/davinci/`.
