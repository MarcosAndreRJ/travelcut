# Homologação manual do host — formulário

**Estado inicial:** `HOST_PENDENTE` — nenhum teste real realizado neste kit.

## Ambiente

- Host Windows (versão/build):
- DaVinci Resolve Studio (versão, licença/capabilities):
- TravelCut build/hash:
- Python e Whisper large-v3:
- FFmpeg:
- MCP DaVinci (ativo? teste read-only?):
- Data da sessão:

## Roteiro mínimo

- [ ] App inicia no host com WebView2 e dependências.
- [RETEST] Projeto de teste com pastas 1–4 reconhecido. FALHA BLOQUEANTE NO PACOTE 1: Ao abrir ou criar projeto no host Windows (0.10.0-host-test.1), a interface desaparecia ficando em tela preta (TypeError: assets.filter is not a function devido à divergência no contrato IPC media.list e ausência de Error Boundary). CORRIGIDO NA VM (branch fix/f11-black-screen-project-navigation): contrato media.list alinhado para retornar array direto de DTOs, camada defensiva no frontend (aceita array e { media: [...] }), Error Boundary com fallback e recuperação na raiz e nas views, tratamento de erro com banner na LibraryTranscriptionView, e script de captura de erros JS encaminhado ao log nativo WPF. Aguardando reteste no host com o pacote 0.10.0-host-test.2.
- [ ] Mídias sintéticas importadas; originais preservados.
- [ ] Transcrição testada no host, quando pertinente.
- [ ] Consulta read-only Resolve bem-sucedida.
- [ ] Projeto **novo temporário** criado.
- [ ] Timeline com 2+ clipes e áudio sincronizado.
- [ ] Cortes corretos e não destrutivos.
- [ ] Projeto `.drp` salvo e reaberto com mídia vinculada.
- [ ] Prévia MP4 exportada sem blackout/silêncio indevido.
- [ ] Shutdown/retomada e erros controlados.

## Roteiro F04b/F05 (importação e análise) — `HOST_PENDENTE`

Pré-requisitos: FFmpeg/FFprobe no PATH (ou `TRAVELCUT_FFMPEG_PATH`); para Whisper, seguir `workers/README.md` (venv isolado, `large-v3` já no cache — o app não baixa nem troca modelo). Faça uma cópia de `%LOCALAPPDATA%\TravelCutStudio\travelcut.db` antes da primeira execução (o app também cria `travelcut.db.bak-<utc>` ao migrar).

- [ ] App abre na tela de boas-vindas (sem abrir/criar projeto sozinho); recentes listados.
- [ ] Criar projeto → Biblioteca em modo Importar; pastas 1–4 criadas.
- [ ] "Adicionar arquivos" (seletor real) e arrastar-e-soltar do Explorer adicionam itens com duração/resolução reais do FFprobe.
- [ ] Miniaturas reais geradas em `3-Trabalho/cache/thumbs`.
- [ ] Importar 2+ vídeos: progresso por bytes, hash verificado; origem intacta (comparar SHA-256 antes/depois); conflito de nome vira "nome (1).ext"; reimportar o mesmo arquivo → "Já existia (idêntico)".
- [ ] Cancelar importação no meio: nenhum `.tcpart` sobra; importados mantidos.
- [ ] "Iniciar análise": silêncios reais; com Whisper, transcrição com palavras e tempos; sem Whisper, "Indisponível" com motivo e nenhuma transcrição.
- [ ] Revisão mostra candidatos reais com evidência; aprovar/rejeitar persiste após reabrir o app; reanálise não reverte decisões.
- [ ] "Abrir pasta" abre o Explorer no arquivo; link externo não navega dentro do app; DevTools fechado em build Release.
- [ ] Formatos de câmera: `.insv` (Insta360) e `.braw` reconhecidos (BRAW pode ficar com metadados "falha" se o FFprobe não decodificar).

## Roteiro F06 (plano editorial) — `HOST_PENDENTE`

- [ ] Primeira abertura com o banco existente: app sobe; `travelcut.db.bak-<utc>` criado se houve migração; decisões antigas preservadas.
- [ ] Com mídias analisadas e cortes aprovados na Revisão: "Gerar plano" na Tela 03 cria R1 em `3-Trabalho/plans/<planId>/r0001.json`; nada muda em `1-Bruto`/`2-Assets` nem no DaVinci.
- [ ] Seções seguem a ordem real das gravações; títulos com horário correto no fuso local (ou "data aproximada (arquivo)" quando não há data de câmera).
- [ ] Só cortes aprovados aparecem aplicados; trechos de estrada ficam 1× com "velocidade sugerida" pendente.
- [ ] Gerar R2 sem mudar nada → mesmo fingerprint; reverter para R1 cria R3; arquivos R1/R2 intactos.
- [ ] Aprovar/rejeitar/desfazer na Revisão, reabrir o app → status mantido; reanalisar a mídia não apaga sugestões decididas.
- [ ] Mídia de 29,97 / 59,94 / VFR (celular): timebase escolhido coerente e durações na timeline corretas.

## Roteiro F07 (revisão, prévia e timeline) — `HOST_PENDENTE`

Pré-requisitos: FFmpeg ≥ 5.1 no PATH (ou `TRAVELCUT_FFMPEG_PATH`); um projeto com pelo menos um vídeo H.264/AAC (celular ou GoPro), um HEVC (iPhone) e, se houver, um ProRes/BRAW/Insta360; mídias analisadas (F05) com candidatos a corte.

- [ ] Tela 04 — antes/depois: o `<video>` toca o trecho com 2 s de contexto; "depois" pula o intervalo sem atraso perceptível; selo "não frame-accurate" visível.
- [ ] Codecs: H.264 toca direto; HEVC mostra "suporte não confirmado" e toca ou falha com mensagem (anotar se a extensão HEVC do Windows está instalada); ProRes/BRAW/INSV oferecem "Gerar proxy".
- [ ] Proxy: gerar proxy de um ProRes/BRAW → arquivo em `3-Trabalho/cache/proxies`, origem intacta (SHA-256 antes/depois), prévia passa a tocar o proxy; sem `.tcpart` sobrando.
- [ ] Forma de onda: picos coerentes com o áudio ouvido; clipe sem áudio mostra "sem áudio".
- [ ] Busca no host virtual: arrastar o scrub em vídeo longo funciona (Range requests no `media.travelcut.studio`); DevTools (build Debug) sem erros de CORS para o `<video>`.
- [ ] Decisões: aprovar, rejeitar, adiar, modificar (in/out), desfazer e refazer (pilha, ADR-014) e atalhos A/R/D/M/Z/Y; fechar e reabrir o app → mesmos estados, intervalos ajustados e disponibilidade de desfazer/refazer.
- [ ] Plano reage: aprovar um corte com plano existente → "Plano atualizado: revisão rN"; aprovar algo sem efeito no plano → nenhuma revisão nova; Tela 03 sem aviso de "desatualizado".
- [ ] Tela 03: o player de programa toca os clipes V1 em sequência, respeita 2×, mostra "sem vídeo neste trecho" nas lacunas; renomear/mover um arquivo de `1-Bruto` fora do app → clipe em vermelho (SOURCE_FILE_MISSING) ao reabrir a tela, e volta ao normal ao restaurar o arquivo.
- [ ] Banco existente: primeira abertura após a atualização cria `travelcut.db.bak-<utc>` se houver decisões antigas a renumerar/marcar; histórico preservado.

## Roteiro F08 (bridge DaVinci) — `HOST_PENDENTE`

Detalhes, comandos e reversão: `docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md` e `tools/host-validation/davinci/README.md`. Usar só mídia sintética e projetos `TravelCut_*` criados pelo harness; **salvar e fechar o seu projeto pessoal antes** (o apply troca o projeto aberto no Resolve).

- [ ] Pré-condições anotadas: versão/edição Studio, "External scripting using: Local", Python 64 bits compatível, `RESOLVE_SCRIPT_API`/`RESOLVE_SCRIPT_LIB`.
- [ ] `python -m unittest discover -s tests -v` em `workers/` → todos verdes (nunca executado na VM).
- [ ] App com Resolve fechado → Tela "Enviar ao DaVinci" mostra "Resolve fechado" (NOT_RUNNING); sem Python → PYTHON_UNAVAILABLE; scripting desligado → SCRIPTING_DISABLED.
- [ ] `Run-HostValidation.ps1` (padrão `endFrame` inclusivo) → probe com versão real; apply cria `TravelCut_*` novo; query confere posição, duração e in-point de cada clipe (tolerância 0); `.drp` exportado reabre com mídia vinculada; render MP4 sem tela preta/silêncio.
- [ ] Repetir com `-EndFrameInclusive:$false` e anotar qual convenção dá divergência 0 (fixar `TRAVELCUT_RESOLVE_END_INCLUSIVE`).
- [ ] Testes negativos: nome sem prefixo → PERMISSION_DENIED; projeto repetido → PROJECT_EXISTS; mídia ausente → rejeitada; Resolve fechado no meio → erro controlado; query com outro projeto aberto → PROJECT_NOT_CURRENT.
- [ ] Pelo app (adaptador real): Tela 03 → "Enviar ao DaVinci" → preparar (dry-run) → confirmar troca → enviar; registro em `3-Trabalho/editor/<planId>/applications`; nada alterado em `1-Bruto`/`2-Assets`; repetir o envio → "resultado anterior reaproveitado".
- [ ] Mídia de celular VFR e 59,94 fps: conferir no Resolve se os cortes caem no frame certo (aviso VFR_SOURCE/MIXED_FRAME_RATE).
- [ ] Reversão: apagar manualmente os projetos `TravelCut_*` temporários no Project Manager e reabrir o projeto que estava aberto antes.
- [ ] Preencher a tabela de capabilities do `docs/18` (documented/mock_pass/host_pending/host_pass/unsupported) com o resultado real.

## Roteiro F09 (MCP e HyperFrames) — `HOST_PENDENTE`

Detalhes: `docs/19-MCP-TRAVELCUT.md` (§9 roteiro read-only) e `docs/20-HYPERFRAMES.md` (§3 roteiro host). Use um banco descartável (`TRAVELCUT_MCP_DATABASE`) e `TRAVELCUT_EDITOR_ADAPTER=mock` na primeira rodada.

- [ ] `mcp\TravelCut.Mcp.exe` sem `--migrate-database` em banco não migrado → sai com código 3 e orientação no stderr; depois de abrir o Desktop uma vez → inicia.
- [ ] Cliente MCP (Claude Code) com `.mcp.json` de projeto **criado pelo usuário** a partir de `.mcp.json.example`: `tools/list` mostra só as 8 ferramentas de leitura sem `--allow-mutations`.
- [ ] Projeto não listado em `TRAVELCUT_MCP_PROJECTS`/`--project` → `PROJECT_NOT_AUTHORIZED`; autorizado → `get_inventory`, `get_suggestions`, `get_edit_plan` iguais ao que a UI mostra.
- [ ] Com `--allow-mutations`: `submit_review` aparece na Tela 04 após reabrir; `apply_plan` só faz dry-run sem `assemblyFingerprint` + `confirmProjectSwitch=true`.
- [ ] Desktop e MCP abertos ao mesmo tempo: enviar ao DaVinci pelos dois → um recebe `OPERATION_IN_PROGRESS`; iniciar análise pelos dois → um recebe `PIPELINE_IN_PROGRESS`; nenhum erro "database is locked" no log.
- [ ] `get_motion_capabilities`: sem Node/CLI → `NODE_UNAVAILABLE`/`NOT_INSTALLED` (nunca simulado); com `TRAVELCUT_HYPERFRAMES_CLI` apontando para o `.js` do CLI → versão lida. Render real (H6 do docs/20) só se a sintaxe do CLI for confirmada.
- [ ] Decisões do usuário anotadas: ativar `.mcp.json` no projeto? `get_transcript` pode entregar a transcrição completa ao cliente MCP?

## Smoke do pacote 0.10.0-host-test.1 — `HOST_PENDENTE`

Checklist completo em `docs/22-SMOKE-TEST-HOST.md`; registrar aqui o resultado de cada item.

- [ ] Hash do `.zip` igual ao `.zip.sha256`; `SHA256SUMS.txt` confere (LEIA-ME-HOST §5).
- [ ] Backup manual de `%LOCALAPPDATA%\TravelCutStudio\travelcut.db` (e `-wal`/`-shm`, se existirem) antes da primeira abertura.
- [ ] Primeira abertura: migração com `travelcut.db.bak-<utc>`; se houver palavras órfãs, aviso no log e tabela `TranscriptWords_orphans`; app abre sem erro. Em falha: diálogo em português (não fechamento silencioso).
- [ ] Log criado em `%LOCALAPPDATA%\TravelCutStudio\logs\travelcut-<data>.log`, com o perfil mascarado como `%USERPROFILE%`.
- [ ] Tela "Verificação do ambiente" e Painel sem selo DEMONSTRAÇÃO e com estados reais (versões só as lidas).
- [ ] Release: sem DevTools, sem menu de contexto do navegador; colar com Ctrl+V funciona nos campos.
- [ ] Sidebar mostra `TravelCut v0.10.0-host-test.1`.

## Roteiro F13.2 (observabilidade) — `HOST_PENDENTE`

Não baixe o modelo de novo nem transcreva vídeo longo só para este roteiro; use um clipe curto.

- [ ] `python -m unittest discover -s tests -v` em `workers/` → testes de `test_whisper_worker.py` verdes (nunca executados na VM).
- [ ] Abrir a app, ir direto para a Biblioteca: a Sidebar mostra Whisper/FFmpeg com o mesmo estado da Verificação do Ambiente e "Verificado às HH:MM"; nenhum "Falha na consulta" residual.
- [ ] "Baixar large-v3" com o modelo já presente: demorar mais de 10 s no diálogo do Windows, confirmar e ver a faixa no topo com "Verificando SHA-256" (barra indeterminada) e depois "já está instalado e íntegro".
- [ ] Trocar de tela durante essa verificação: a faixa continua visível; ao concluir, fica até clicar em Fechar.
- [ ] Clipe curto: durante a análise o cartão mostra nome, "Decodificando o áudio" → "Carregando o modelo" → "Transcrevendo", tempo decorrido e último sinal; anotar se o **percentual real** apareceu (depende da versão do openai-whisper) ou se ficou indeterminado.
- [ ] Último sinal não passa de ~10 s durante a transcrição (heartbeat); anotar se o aviso "sem telemetria recente" apareceu.
- [ ] Cancelar a análise: "Cancelando…" e depois "Cancelado"; processo Python encerrado no Gerenciador de Tarefas; clipe volta ao estado real.
- [ ] Concluir: transcrição e resultados do clipe selecionado aparecem sem recarregar a tela.
- [ ] Instalar/configurar uma ferramenta e confirmar que Sidebar e Verificação mudam juntas sem reiniciar o app.

## Roteiro F13.3 (player e diagnóstico do Whisper) — `HOST_PENDENTE`

- [ ] Biblioteca: selecionar um clipe H.264 → player carrega sem tocar sozinho; reproduzir, pausar, arrastar a barra, volume, silenciar, tela cheia (anotar se a tela cheia ocupa a janela ou o monitor).
- [ ] Trocar de clipe durante a reprodução: o anterior para e o novo carrega pausado.
- [ ] Clipe HEVC (celular): anotar se toca direto, se aparece "não decodificou a imagem" (só áudio) ou erro de codec; em ambos, "Gerar proxy compatível" → proxy em `3-Trabalho/cache/proxies`, player recarrega na mesma posição; SHA-256 do original igual antes/depois.
- [ ] Clicar na faixa temporal, numa palavra da transcrição e num silêncio/hesitação: o vídeo vai ao instante; sem saltos repetidos.
- [ ] Com uma análise em andamento, o player continua funcionando.
- [ ] Fila de Processamento → "Detalhes técnicos da execução": conferir Python, worker, argumentos, diretório, ambiente, mídia, modelo, idioma, dispositivo, estado, código de saída e logs (stderr do Whisper).
- [ ] "Copiar comando de diagnóstico (PowerShell)": colar no Windows PowerShell 5.1 com um clipe curto; conferir stdout, stderr, código de saída e o arquivo salvo em `%TEMP%\travelcut-whisper-diagnostico-*.log`. Caminhos com espaço/acentos devem funcionar.
- [ ] Cancelar uma análise: "Cancelado" nos detalhes e processo Python encerrado.

## Evidências e erros

Preencher para cada teste: status (`PASS/FAIL/NOT_RUN`), versão, passos, resultado real, arquivo log/captura, erro e ticket regressão. Não anexar credenciais. Após o usuário retornar as evidências, Opus atualiza checklist/status.
