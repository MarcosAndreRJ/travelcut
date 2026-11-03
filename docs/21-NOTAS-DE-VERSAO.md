# 21 — Notas de versão: 0.10.0-host-test.1

**Versão:** 0.10.0-host-test.1
**Data:** 2026-11-03
**Tipo:** pacote de teste de desenvolvimento para homologação no host. Não é release, não é instalador e não é assinado digitalmente.
**Estado:** `mock_pass` na VM de desenvolvimento; `host_pending` para tudo que depende de DaVinci, FFmpeg, Whisper, HyperFrames, WebView2 no host e MCP com o app aberto. Nenhuma homologação humana no host foi feita nesta versão.

Limitações e riscos completos: [23-LIMITACOES-E-RISCOS.md](23-LIMITACOES-E-RISCOS.md). Roteiro de smoke test: [22-SMOKE-TEST-HOST.md](22-SMOKE-TEST-HOST.md).

## 1. Pacote e hashes

| Item | Valor |
|---|---|
| Arquivo | `TravelCutStudio-0.10.0-host-test.1-win-x64.zip` |
| SHA-256 do `.zip` | `3494021673f9bcf740e85e6fcee615dcf2ca5adc8fd9fdd23a65e31e39b18f90` (conferido com o arquivo `.zip.sha256`) |
| Tamanho do `.zip` | 103,9 MB (conforme diário de 01:06; não reverificado nesta redação) |
| Pasta | `TravelCutStudio-0.10.0-host-test.1-win-x64/` |
| Arquivos na pasta | 713 (`MANIFEST.json`, `totals.fileCount`) |
| Tamanho da pasta | 713 arquivos listados no `MANIFEST.json`, 253.112.833 bytes (≈ 241,4 MiB). O log do script mostra "241.6 MB" porque mede a pasta inteira, incluindo `MANIFEST.json` e `SHA256SUMS.txt`. |
| Build (UTC) | 2026-11-03T04:03:43Z (`MANIFEST.json`) |
| Configuração | Release, `win-x64`, self-contained, sem single-file, sem ReadyToRun, sem trimming |
| Reprodutível | `true`, sem motivos de não reprodutibilidade |
| Testes no build | `dotnet test TravelCutStudio.sln -c Release`, código de saída 0 (UnitTests 1043/1043, Mcp.Tests 107/107) |
| Ferramentas | .NET SDK 10.0.401; Node v24.12.0; npm 11.6.2; PowerShell 5.1.19041; Windows 10.0.19045 |
| Assinatura | nenhuma (SmartScreen e antivírus podem alertar) |
| Commit | não versionado em Git (a cópia do repositório não tem `.git`) |

Verificação de integridade: comparar o SHA-256 do `.zip` com o `.zip.sha256`. Depois de extrair, executar o trecho da seção 5 de `LEIA-ME-HOST.md`, que confere `SHA256SUMS.txt`.

## 2. Novidades por área

### 2.1 Importação de mídia (F04b)

- Seletor de arquivos e arrastar e soltar do Explorer no WebView2. Pastas são listadas pelo backend. (`host_pending`)
- Cópia aditiva para `1-Bruto/` ou `2-Assets/`: arquivo `.tcpart`, SHA-256 relido, renomeação. Nunca sobrescreve, move ou apaga (ADR-013).
- Conflito de nome vira `nome (1).ext`. Duplicata idêntica vira "Já existia (idêntico)" e não é copiada de novo.
- Cancelamento remove só o `.tcpart` do próprio job. Arquivos já importados são mantidos. Há verificação de espaço livre antes de começar e um import por projeto.
- Metadados e duração vêm do FFprobe. Sem FFprobe, o item fica sem metadados. Não há mais valores padrão de 1920×1080 ou 30 fps.
- Miniaturas via FFmpeg em `3-Trabalho/cache/thumbs`, com status explícito quando indisponíveis.
- Extensões reconhecidas incluem `.insv`, `.braw`, `.mxf` e `.webm`. `.lrf` foi excluído de propósito (decisão registrada no diário de 2026-11-02; o motivo detalhado não está no diário).
- Placeholders do OneDrive (arquivos sob demanda) geram mensagem explícita quando selecionados. A listagem do scanner ainda os descarta em silêncio (ver docs/23).

### 2.2 Análise de mídia (F05)

- Silêncios via FFmpeg `silencedetect`, com `ArgumentList`, timeout e cancelamento. Sem FFmpeg, o resultado é "indisponível". Não há mais silêncio estimado pelo tamanho do arquivo.
- Transcrição via worker Python (`workers/whisper_worker.py`, protocolo JSON `v1`) com modelo `large-v3`. O app não baixa nem troca modelo. Sem Python ou modelo, a transcrição fica indisponível, com motivo. Não há transcrição sintética.
- Palavras validadas por monotonia e limites antes de salvar.
- Hesitações e repetições são marcadas com contexto e faixa qualitativa, sempre para revisão humana. Não há confiança percentual.
- Classificação de cena por evidências, com `isHypothesis` e `indeterminado` quando não há base suficiente.
- Cache por fingerprint, ferramenta e modelo em `3-Trabalho/analysis`. Denominadores de progresso são reais. Um job por projeto. Cancelamento e status por etapa.
- Biblioteca mostra fila real, painel por clipe e estados `not_run`, `unavailable` e `failed`.

### 2.3 Plano editorial (F06)

- Plano imutável por revisão em `3-Trabalho/plans/<planId>/rNNNN.json`, com índice no banco e fingerprint canônico.
- Cronologia pela data de captação (GPS, `creation_time`, tags ou arquivo). Quando não há data de câmera, o título mostra "data aproximada (arquivo)". Fuso local e `timeZoneId` registrados.
- Seções começo, meio e fim como hipótese. Cada seção é marcada como hipótese.
- Só cortes aprovados são aplicados, e cortes triviais de alta evidência com marcação de automático. Trecho residual curto vira clipe para revisão (`residual_sub_frame`).
- Velocidade 2×/4× para B-roll sem fala é apenas sugestão. Trechos de estrada ficam 1× com "velocidade sugerida" pendente.
- Zoom é só sugestão. Música nunca é aplicada automaticamente.
- Timebase racional para 29,97, 23,976, 59,94 e VFR, com timecode NDF exibido. Arredondamento half-even e layout sem drift acumulado.
- Gerar sem mudança mantém o mesmo fingerprint. Reverter cria nova revisão, e revisões antigas não são alteradas.
- Validador com códigos como `TRACK_OVERLAP`, `CLIP_OUTSIDE_MEDIA`, `SPEED_OVER_SPEECH` e `MEDIA_CHANGED`. Na leitura, o plano também é verificado contra adulteração (`PLAN_TAMPERED`) e corrupção (`PLAN_FILE_CORRUPT`).

### 2.4 Revisão, decisões e prévia (F07)

- Decisões: aprovar, rejeitar, adiar e modificar in/out. Cada decisão grava `baseRevision` e o intervalo decidido.
- Desfazer e refazer em pilha (ADR-014). O histórico é append-only.
- Lote só para silêncio, com aviso de heurística não calibrada.
- Atalhos da Tela 04: A, R, D, M, Z, Y, ↑ e ↓.
- O plano reage às decisões: aparece "Plano atualizado: rN" quando o conteúdo muda. Decisões sem efeito no plano não criam revisão.
- `PLAN_OUTDATED` quando as entradas do plano mudaram. `SOURCE_FILE_MISSING` por clipe, em vermelho, quando o arquivo de origem não está no lugar.
- Prévia de intervalo com 2 s de contexto e prévia de programa na Tela 03. Os selos "não frame-accurate" aparecem na tela. Clipe sem fonte reproduzível mostra o motivo.
- Forma de onda via FFmpeg com picos reais. Clipe sem áudio mostra "sem áudio".
- Proxies H.264 para ProRes, BRAW e Insta360 em `3-Trabalho/cache/proxies`, com job, cancelamento e arquivo `.tcpart` (sem sobrescrita). A origem não é alterada.
- Host virtual `media.travelcut.studio`: somente leitura, restrito à raiz do projeto aberto, com `DenyCors` (fetch e XHR da página são bloqueados; o `<video>` sem `crossorigin` funciona). A página não o aceita como origem IPC.
- Codec é informado com `likelyPlayable` honesto: H.264 indica reprodução provável, HEVC indica "suporte não confirmado" e ProRes, DNxHD, BRAW e INSV indicam não reproduzível diretamente.

### 2.5 Envio ao DaVinci (F08)

- Contrato `editor/1` com estados de ambiente, capabilities por operação e nível de verificação. Resultados são `SIMULATED`, `DRY_RUN`, `UNAVAILABLE`, `READY_REAL`, `PARTIAL_REAL` ou `ERROR`.
- Fluxo: preparar (dry-run com simulação seca e manifesto) → confirmar a troca do projeto aberto → aplicar. A aplicação exige `assemblyFingerprint`, `confirmProjectSwitch` e chave de idempotência.
- Só cria projeto novo `TravelCut_*`. Não abre nem salva projetos existentes para escrita. Query, export e render só atuam no projeto `TravelCut_*` que já está aberto (`PROJECT_NOT_CURRENT` caso contrário).
- Saídas confinadas a `3-Trabalho/editor`. Snapshot `.drp` exportado após aplicar. Registro em `3-Trabalho/editor/<planId>/applications`.
- Limite V1: velocidade diferente de 1×, transições e faixas além de V1 (mais A1 vinculado) são rejeitadas com `CAPABILITY_UNSUPPORTED`.
- Adaptador real é o padrão. Sem Resolve, a resposta é `NOT_INSTALLED`, `NOT_RUNNING` ou `SCRIPTING_DISABLED`, nunca sucesso. Não há fallback silencioso para o simulador (ADR-015).
- Bridge `workers/resolve_bridge.py` no protocolo `resolve-bridge/1`, por subprocesso com timeout. Testes Python existem, mas não foram executados na VM.

### 2.6 Servidor MCP (F09)

- Servidor `travelcut` (`mcp\TravelCut.Mcp.exe`, stdio). Logs só em stderr.
- Leitura (sempre disponível, 8 ferramentas): `list_projects`, `get_inventory`, `get_transcript`, `get_suggestions`, `get_edit_plan`, `job_status`, `get_editor_capabilities` e `get_motion_capabilities`.
- Mutação (só com `--allow-mutations`, 3 ferramentas): `submit_review`, `queue_analysis` e `apply_plan`. `apply_plan` faz dry-run por padrão.
- Projetos autorizados por GUID (`--project` ou `TRAVELCUT_MCP_PROJECTS`). A autorização não é ampliada pelo cliente.
- `idempotencyKey` obrigatória em mutações, com cache em memória. Chave reutilizada com argumentos diferentes retorna `IDEMPOTENCY_KEY_REUSED`.
- Trava entre processos por operação (`3-Trabalho/.locks`): `editor-apply`, `analysis` e `motion-render`. Ocupada retorna `OPERATION_IN_PROGRESS` ou `PIPELINE_IN_PROGRESS`.
- Não migra o banco sem `--migrate-database`. Se o schema estiver incompleto, o processo sai com código 3.

### 2.7 HyperFrames por capability (F09.5)

- Templates `title_card`, `lower_third` e `map_route`, no contrato `motion/1`.
- Assets só de `2-Assets`. Saída em `3-Trabalho/motion/<renderId>/`. Sucesso só com `output.mp4` íntegro.
- Node é localizado por caminho absoluto. CLI `.cmd`, `.bat` e `.ps1` são recusados (`CLI_UNSUPPORTED_TYPE`). Junções e links nos segmentos do caminho são recusados (`PERMISSION_DENIED`).
- Sem Node ou CLI: estado explícito (`NODE_UNAVAILABLE`, `NOT_INSTALLED`). Nunca sucesso.
- Simulador só com `TRAVELCUT_MOTION_ADAPTER=mock` (`SIMULATED`, sem vídeo).
- A sintaxe do CLI (`render --manifest/--assets-root/--output`) é uma suposição. A renderização real não foi executada.

### 2.8 Empacotamento (F10.3)

- `scripts/release/Build-Release.ps1 -Zip`: `npm ci` e build do frontend, `dotnet test` em Release, `dotnet publish` do Desktop e do MCP, varredura de conteúdo proibido, `MANIFEST.json`, `SHA256SUMS.txt`, `LEIA-ME-HOST.md` e `.zip` com `.sha256`.
- Versão única em `Directory.Build.props` (`0.10.0-host-test.1`). O build usa `TRAVELCUT_VERSION`.
- Sem single-file, sem ReadyToRun e sem trimming: o WPF carrega o `WebView2Loader` nativo e o frontend e os workers são lidos do disco, ao lado do executável.

### 2.9 Logs e diagnóstico (F10.2)

- Logs em `%LOCALAPPDATA%\TravelCutStudio\logs`, com rotação diária e 14 arquivos. Perfil mascarado, tokens redigidos e escrita sem bloquear a interface. Também valem em Release.
- Erros internos chegam à interface sem stack trace (`INTERNAL_ERROR`). Os logs guardam o tipo da exceção e a mensagem sanitizada.
- Falha de inicialização mostra uma caixa de mensagem traduzida (`StartupFailureClassifier`). (`host_pending`)

### 2.10 Robustez e segurança (F05 a F10)

- WebView2: allowlist de origem, navegação externa e novas janelas bloqueadas. DevTools e dev server só em Debug. CSP apenas no build de produção. Menu e atalhos do navegador desligados em Release.
- `system.openFolder` é único, confinado ao projeto, sem caminhos UNC e com `ArgumentList`.
- Raízes de projeto proibidas: raiz de unidade, UNC, pastas do sistema e perfil do usuário (`INVALID_PROJECT_ROOT`).
- Migração estrutural faz backup automático `travelcut.db.bak-<utc>` (`VACUUM INTO`) antes de alterar o banco. Palavras órfãs da transcrição vão para `TranscriptWords_orphans`, na mesma transação. Banco com versão mais nova que a do app é recusado (`SCHEMA_TOO_NEW`). Migrações concorrentes são serializadas por trava.
- Subprocessos (FFmpeg, FFprobe, Python, Node, Resolve) com `ArgumentList`, timeout e encerramento da árvore de processos.
- Mídia de origem é verificada antes e depois das operações nos testes (SHA-256 de `1-Bruto` e `2-Assets` intacto).

## 3. Mudanças de comportamento relevantes

| Tema | Comportamento nesta versão |
|---|---|
| Editor padrão | Sem `TRAVELCUT_EDITOR_ADAPTER`, o app usa o adaptador DaVinci real. Só `TRAVELCUT_EDITOR_ADAPTER=mock` ativa o simulador (`SIMULATED`, sem `.drp`). Qualquer outro valor também usa o adaptador real. |
| Abrir projeto | Não cria pastas sem confirmação. Se faltar uma das 4 pastas, o app pede confirmação (`INVALID_FOUR_FOLDERS`). |
| Início | Abre na tela de boas-vindas, sem projeto aberto automaticamente. Não há mais `demo-project`. |
| MCP e banco | O MCP não migra o banco sem `--migrate-database`. Sem schema pronto, sai com código 3 e orienta a abrir o Desktop uma vez. |
| Raízes proibidas | Raiz de unidade, UNC, pastas do sistema e perfil do usuário são recusadas. |
| Estimativas | Duração estimada de corte fica `null` (exibida como "—"). Não há mais fator 0,7 nem outro valor inventado. |
| Decisões | Desfazer é uma pilha (ADR-014). Corte automático só para `silence`. |
| Mock de interface | O badge DEMONSTRAÇÃO aparece só no modo mock do navegador. |
| Ambiente | Estados vêm do host (`analysis.environment`, `editor.capabilities`, `motion.capabilities`, `pipeline.status`). Sem dado, aparece "Não verificado". Não há versões fixas na tela. |

## 4. Simulado ou indisponível nesta versão

| Item | Estado na VM | Comportamento observado |
|---|---|---|
| DaVinci Resolve | Não instalado | `NOT_INSTALLED`. Aplicação retorna erro controlado, nunca sucesso. |
| FFmpeg e FFprobe | Não instalados | Análise, miniaturas, forma de onda e proxies ficam indisponíveis, com motivo. |
| Python e Whisper `large-v3` | Não instalados | Transcrição indisponível, com motivo. Nenhuma transcrição é inventada. |
| HyperFrames | Não instalado | `NOT_INSTALLED`. Sem render. |
| Reprodução de vídeo real | Sem mídia de teste nem FFmpeg | Não validada. Codecs, Range e fluidez do salto "after" dependem do host. |
| Editor e motion em modo mock | Só com variável explícita | `SIMULATED`, sem `.drp` e sem vídeo. Não é integração real. |
| Testes da bridge Python (`workers/tests/`) | Não executados | Sem Python na VM. |
| Harness PowerShell (`tools/host-validation/davinci/`) | Só análise sintática | Não executado. |
| Importação com seletor e arrastar e soltar | Não exercitados no host | `host_pending`. |
| Desktop e MCP com o app aberto ao mesmo tempo | Testado só com duas instâncias no mesmo processo | `host_pending`. |

## 5. Mudanças de documentação nesta versão

- `README.md` reescrito para .NET 10, WPF, WebView2, React e a estrutura real.
- Novos: `docs/21` (esta nota), `docs/22` (smoke test no host) e `docs/23` (limitações e riscos).
- `docs/00-INDICE.md` inclui 17, 21, 22 e 23.

## 6. Pendências desta versão

- F10.4: cópia e instalação no host (depende de autorização; a unidade Z: não está montada na VM).
- F10.6: auditoria final do pacote e da interface.
- F11: homologação real no host e critérios de MVP.
- Decisões do usuário registradas: ativar `.mcp.json` no projeto; política de `get_transcript` (hoje entrega a transcrição completa ao cliente MCP); confirmação da ADR-014 (aprovada pelo orquestrador); atualização de `SQLitePCLRaw` (NU1903).
