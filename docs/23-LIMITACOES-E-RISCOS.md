# 23 — Limitações e riscos: 0.10.0-host-test.1

Lista consolidada do estado real do código em 2026-11-03. Cada item tem estado, mitigação e fonte (diário `progresso/IMPLEMENTATION_LOG.md`, `progresso/CHECKLIST.md`, `docs/*`). Quando as fontes divergem, a divergência aparece na própria linha ou na seção 4.

## Legenda de estado

| Estado | Significado |
|---|---|
| `mock_pass` | Código e testes passam com mock, fixture ou fake. Não prova integração com sistema externo. |
| `host_pending` | Depende de execução no host (DaVinci, FFmpeg real, Whisper, WebView2, HyperFrames). Não executado. |
| `aberto` | Problema ou dívida conhecida, sem correção nesta versão. |
| `decisão` | Depende de decisão do usuário. |
| `documentado` | Comportamento conhecido e explicado, sem correção planejada. |

Nada aqui é `host_pass`. O único caminho para isso é a evidência registrada em `progresso/HOST_TESTS.md` (ver [22-SMOKE-TEST-HOST.md](22-SMOKE-TEST-HOST.md)).

## 1. Limitações funcionais e de escopo

| ID | Limitação | Estado | Mitigação / próximo passo | Fonte |
|---|---|---|---|---|
| L1 | Sem testes automatizados de componente, teclado ou fluxo visual. O `npm test` (162 testes) cobre funções puras, modelos e mocks, não a renderização das telas. | `aberto` (exceção aceita pelo usuário na F07; dívida para F10.6) | Verificação manual no navegador (mock) e roteiro do host (docs/22). | Diário 2026-11-02 (F07, 23:10 e 23:40); CHECKLIST F07.6 |
| L2 | Prévia não é frame-accurate. O `<video>` do WebView2 não garante seek por frame. | `documentado` (selo "não frame-accurate" na tela) | Usar a prévia para revisão, não para conformação. | Diário 2026-11-02 (F07, 23:10) |
| L3 | Reprodução real não validada: codecs (HEVC depende da extensão do Windows), Range requests no host virtual, fluidez do salto "after". ProRes, BRAW e INSV não tocam direto e dependem de proxy. | `host_pending` | Roteiro F07 do HOST_TESTS e item 14 de docs/22. | Diário 2026-11-02 (F07.3, F07.5); docs/22 |
| L4 | Envio ao DaVinci rejeita velocidade diferente de 1×, transições e faixas além de V1 (mais A1 vinculado) com `CAPABILITY_UNSUPPORTED`. | `documentado` (lacuna por desenho, ADR-015) | Planejar sem essas operações no envio. Ampliar só em fase futura, com nova capability verificada. | ADR-015 (docs/15); diário 2026-11-02 (F08.1) |
| L5 | Integração real com o DaVinci não executada. Suposições da API listadas: convenção de `endFrame`, `recordFrame` relativo a `GetStartFrame`, VFR no `ImportMedia`, `File Path`, áudio vinculado, `SetSetting` antes da importação, `ExportProject`, render e `GetLeftOffset`. | `host_pending` | Harness em `tools/host-validation/davinci/` e roteiro docs/18 §4 e §5. | docs/18 §5; diário 2026-11-02 (F08.4–F08.5) |
| L6 | HyperFrames: sintaxe do CLI (`render --manifest/--assets-root/--output`) é suposição. Nenhum render real foi executado. Os templates só viram `host_pass` após H6 do roteiro. | `host_pending` | Registrar a saída exata do CLI no host e ajustar `HyperFramesRenderer`, se necessário. | docs/20 §5 e §4; `workers/hyperframes/README.md`; diário 2026-11-02 23:39 |
| L7 | `get_transcript` (MCP) entrega a transcrição completa ao cliente MCP. Transcrição é dado sensível. | `decisão` (política pendente com o usuário) | Decidir: manter, limitar trechos ou exigir confirmação. Não colar transcrições em logs ou relatórios. | Diário 2026-11-02 23:59; docs/19 §3 e §6 |
| L8 | O scanner descarta placeholders do OneDrive sem aviso (contrato). Seleção explícita e listagem mostram mensagem própria. | `aberto` (contrato) | Manter o arquivo disponível localmente ("Sempre manter neste dispositivo"). | Diário 2026-11-03 01:06 (A4) |
| L9 | Cache de idempotência do MCP é só em memória, com limite de 1000 entradas e sem TTL. Reiniciar o servidor faz a mesma chave executar de novo. | `aberto` (limite documentado) | Não reutilizar `idempotencyKey` entre sessões. | docs/19 §4 e §12.1; diário 2026-11-03 00:15 |
| L10 | `submit_review` (MCP) não envia `baseRevision`. Decisões concorrentes podem sobrescrever uma à outra. O app envia `baseRevision` pelo IPC. | `aberto` | Uso por um operador por vez. Pertença da sugestão ao projeto é verificada antes. | docs/19 §12.2; diário 2026-11-02 23:52 |
| L11 | `confirmProjectSwitch` no MCP é um booleano enviado pelo cliente. O servidor não distingue clique humano de decisão do agente. | `documentado` | O agente deve pedir aprovação ao usuário antes de enviar `true`. | docs/19 §6.7 e §12.5 |
| L12 | Transcrição e cena não entram no `inputsFingerprint` do plano. Análise concluída depois do plano não gera `PLAN_OUTDATED`. | `aberto` | Regerar o plano manualmente após concluir análise. | Diário 2026-11-02 21:20 (F07.2) |
| L13 | Heurísticas de hesitação, repetição, cena e faixa "high" não estão calibradas. Corte automático só para `silence` (ADR-014). | `aberto` (calibração) | Revisão humana obrigatória. Não há confiança percentual na interface. | ADR-014 (docs/15); diário 2026-11-02 (F05, F06) |
| L14 | Persistência por `EnsureCreated` e DDL idempotente, sem migrações EF formais. | `aberto` (dívida) | Os testes de migração cobrem a ordem atual. Avaliar migrações formais em fase futura. | CHECKLIST F07 (pendências) |
| L15 | Migração do banco real do usuário não testada. Os testes de fim a fim usaram banco legado sintético, rebaixado por DDL, e não um banco de release real. | `host_pending` | Backup antes da primeira abertura (docs/22 item 3). | Diário 2026-11-03 00:46 (F10.1) |
| L16 | Timecode NDF diverge cerca de 3,6 s por hora do relógio em 29,97 fps. | `documentado` | Não usar o timecode NDF como relógio de parede. | Diário 2026-11-02 18:30 (F06.2) |
| L17 | Mídias catalogadas antes da correção de VFR mantêm `r_frame_rate` até nova varredura. | `aberto` (dado antigo) | Nova varredura do projeto. | Diário 2026-11-02 23:14 (F08, A1) |
| L18 | Bancos antigos mantêm 1920×1080 e 30 fps inventados até novo scan. O código novo não grava mais esses valores. | `aberto` (dado antigo) | Nova varredura. | Diário 2026-11-02 13:05 (F04b, pendências) |
| L19 | Campos do perfil editorial `silencePolicy`, `pace` e `jokePreservation` não têm efeito no planejador. Os campos saíram da Tela 06. `speedBroll` "auto" = 2× sem definição formal. | `aberto` | Revisar em fase de perfil. | Diário 2026-11-02 18:20 (A6); CHECKLIST F06 (pendências) |
| L20 | Zoom em "conversa" diverge de docs/07 ("cenas fixas com pessoas"). | `aberto` | Revisar regra editorial com o usuário. | CHECKLIST F06 (pendências) |
| L21 | Fila de revisão fica à esquerda (o mockup coloca à direita). Itens do mockup sem dado real não foram implementados, como "confiança editorial 87%" e relatório. | `documentado` (não inventar dados) | Só implementar com dado real. | Diário 2026-11-02 22:40 (F07.4) |
| L22 | Sem dado de ambiente, a tela mostra "Não verificado". A tela foi conferida à mão no navegador interno (modo mock), sem verificação automatizada. | `mock_pass` | Conferir no host (docs/22 item 6). | Diário 2026-11-03 01:03 (F10.2) |
| L23 | `tsc --noEmit` tem 4 erros pré-existentes em código morto (`DashboardView`, `TimelineMontageView`). O `npm run build` passa. | `aberto` (higiene) | Remover o código morto em manutenção. | Diário 2026-11-03 01:03 |
| L24 | `plan.get` carrega análises de todas as mídias a cada leitura. Cada decisão faz até três cargas completas. | `aberto` (desempenho) | Medir com projeto grande no host. | CHECKLIST F07 (pendências) |
| L25 | Pacote não assinado. Sem atualização automática e sem instalador (pasta portátil). | `decisão` (distribuição) | Conferir hashes. Não desativar SmartScreen nem antivírus. | docs/14 (Distribuição); `LEIA-ME-HOST` seção 7 |
| L26 | Esta cópia do repositório não está sob Git (sem `.git`). O manifesto marca commit como "não versionado em git". | `aberto` | Versionar em momento autorizado pelo usuário. | `MANIFEST.json`; diário 2026-11-03 00:51 |

## 2. Riscos

| ID | Risco | Impacto | Estado | Mitigação / próximo passo | Fonte |
|---|---|---|---|---|---|
| R1 | Advisory **NU1903** (alta) em `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (GHSA-2m69-gcr7-jv3q). Afeta Desktop, Infrastructure e Mcp. | Vulnerabilidade conhecida em biblioteca nativa do SQLite | `decisão` (atualização de pacote pendente) | Atualizar o pacote com a suíte completa e novo pacote. Requer decisão. | docs/14 (Estado da última execução); diário 2026-11-02 (F02) e 2026-11-03 00:51 |
| R2 | 5 vulnerabilidades no `npm audit` completo, todas em ferramentas de desenvolvimento: esbuild (Vite 5), tinypool e @vitest/mocker (vitest 3). `npm audit --omit=dev` retorna 0. | Não entra no app empacotado | `aberto` | Correção exige Vite 6+ e vitest 4. Planejar em manutenção. | Diário 2026-11-02 23:40 |
| R3 | .NET 10 na Windows 10 Pro 22H2 não é suportado oficialmente (ADR-012). Build e testes rodaram na VM. | Falhas de runtime que só aparecem no host | `aberto` | Validação final em Windows 10/11 suportado (docs/22, pré-requisitos). | ADR-012 (docs/15); CLAUDE.md; docs/14 |
| R4 | Migração do banco real (legado) ao abrir a versão nova pela primeira vez. | Perda ou alteração de decisões antigas | `host_pending` | Backup manual (docs/22 item 3) e backup automático `travelcut.db.bak-<utc>`. Banco de versão mais nova é recusado (`SCHEMA_TOO_NEW`). | Diário 2026-11-02 (F05, 15:20) e 2026-11-03 (F10.2, 01:06); CHECKLIST |
| R5 | Modo de journal. O banco da VM ficou em WAL (`-wal` e `-shm` apareceram após o smoke do MCP). A decisão B8 (journal DELETE) continua aberta. | Backup incompleto se só `travelcut.db` for copiado | `aberto` | Copiar os três arquivos com o app fechado (docs/22 item 3). Confirmar o modo no host. | Diário 2026-11-03 00:18 (B8) e 00:51 (WAL) |
| R6 | Desktop e MCP ao mesmo tempo sobre o mesmo projeto. A trava por arquivo foi testada só com duas instâncias no mesmo processo. Não há teste com app e MCP abertos, nem em Unix ou unidade de rede. | Operação concorrente pode passar sem a trava | `host_pending` | Teste H13 (docs/20) e teste do item 19 com app aberto. | docs/19 §4.2 e §12.3; docs/20 §3 e §5.1 |
| R7 | Testes Python da bridge (`workers/tests/test_resolve_bridge.py`) e harness PowerShell nunca executados. Sem Python na VM e `.ps1` validados só por parser. O diário registra "29 testes" (23:14) e depois "≈87" (23:30); a contagem não está reconciliada. | Falha de protocolo que só aparece no host | `host_pending` | Rodar `python -m unittest discover -s tests -v` em `workers/` antes do harness. | Diário 2026-11-02 (F08, 23:14 e 23:30); HOST_TESTS (roteiro F08) |
| R8 | Suposições da API do DaVinci podem divergir no host (convenção de `endFrame`, `recordFrame`, `leftOffset`, export, render). | Cortes deslocados em 1 frame ou operação recusada | `host_pending` | Dry-run, query de conferência, `TRAVELCUT_RESOLVE_END_INCLUSIVE` e harness. | docs/18 §5 e §7 |
| R9 | Aplicar troca o projeto aberto no Resolve. O adaptador não confere `confirmProjectSwitch` por conta própria; a verificação fica no caso de uso. | Alterações não salvas do projeto pessoal podem ser perdidas se o usuário não salvar antes | `mock_pass` (desenho); `host_pending` (comportamento) | Confirmação obrigatória, salvar e fechar o projeto pessoal, reversão em docs/18 §6. | Diário 2026-11-02 23:30 (observações F08); docs/18 §6 |
| R10 | O adaptador DaVinci real é o padrão. Quem esperava simulação verá erro (`NOT_INSTALLED`, `NOT_RUNNING`, `SCRIPTING_DISABLED`). | Confusão sobre o estado do editor | `mitigado` | Estados explícitos. Simulador só com `TRAVELCUT_EDITOR_ADAPTER=mock`, sempre marcado `SIMULATED`. | ADR-015 (docs/15); diário 2026-11-02 (F08) |
| R11 | Placeholders do OneDrive: o arquivo some do inventário sem aviso na listagem. | Mídia "faltando" sem explicação | `aberto` | Ver L8. | Diário 2026-11-03 01:06 |
| R12 | Prévia: falhas de codec ou de Range podem aparecer só como vídeo que não toca. | Revisão com prévia falsamente quebrada | `host_pending` | Ver L3. O carregamento do `<video>` tem tempo limite de 15 s e a interface mostra motivo de codec quando conhecido. | Diário 2026-11-02 (F07.5, 23:40) |
| R13 | Concorrência SQLite: busy timeout de 15 s; transação deferida sem retry de `SQLITE_BUSY`; a trava de migração pode esperar até 30 s na inicialização do Desktop. | Interface lenta ou erro de escrita sob concorrência | `aberto` | Registrar no host se a interface travar na inicialização. | CHECKLIST F07 (pendências); diário 2026-11-03 00:33 |
| R14 | O host virtual `media.travelcut.studio` expõe a raiz inteira do projeto aberto, em leitura. | Leitura de arquivos fora de `1-Bruto` e `2-Assets` dentro do projeto | `aberto` (aceito por desenho) | Só projeto aberto, sem escrita, `DenyCors`, não aceito como origem IPC. | CHECKLIST F07 (pendências); diário 2026-11-02 20:40 |
| R15 | Links e junções: checagem de reparse point antes de criar ou apagar. Há TOCTOU residual (link criado entre a checagem e a escrita) em importação e render. Nome 8.3 não reproduzido. | Escrita fora do projeto em caso raro | `aberto` (risco residual) | Checagem em todos os segmentos. Validar no host se o volume tiver 8.3 desativado. | docs/20 §5.1; diário 2026-11-03 00:28 |
| R16 | Logs podem conter caminhos e fragmentos de dados. O stderr do MCP sanitiza caminhos, mas a revisão é manual. | Exposição de caminho privado ou transcrição em relatório | `aberto` (revisão manual) | Revisar antes de anexar (docs/22 item 20). | docs/19 §8; diário 2026-11-03 01:06 |
| R17 | Dependências externas com versão mínima: FFmpeg ≥ 5.1 (`-fps_mode passthrough`), `drawtext` e `libx264` para o harness, Python 64 bits e Node. | Proxy e prévia podem falhar com FFmpeg anterior à 5.1 | `host_pending` | Conferir `ffmpeg -version` no host (docs/22, pré-requisitos). | docs/14; docs/18 §1; diário 2026-11-02 (F07.3) |
| R18 | Configuração com nomes parecidos: `TRAVELCUT_PYTHON` (bridge DaVinci) e `TRAVELCUT_PYTHON_PATH` (Whisper). `TRAVELCUT_FFPROBE_PATH` existe no código, mas não aparece no `LEIA-ME-HOST.template.md`. | Python errado apontado; variável ignorada | `aberto` (documentação) | Usar as duas variáveis conforme o nome. Completar o template em manutenção. | `src/TravelCut.ResolveBridge/ResolveBridgeOptions.cs`; `src/TravelCut.Infrastructure/Audio/WhisperTranscriptionService.cs`; `src/TravelCut.Infrastructure/Media/FfprobeLocator.cs`; `scripts/release/LEIA-ME-HOST.template.md` |
| R19 | Testes instáveis observados: `IpcDispatcherTests` (plano) falhou uma vez na F07 e `SchemaVersionMarkerTests` uma vez em 00:33 (código em andamento). Nas execuções seguintes passaram (972/972 e 1043/1043). `EndToEndMigrationTests` falhou 2 de 2 em build intermediário, registrado em docs/14. Não reproduziu depois. | Regressão intermitente sem causa identificada | `aberto` (monitorar) | Repetir a suíte antes de cada pacote. Investigar se voltar. | Diário 2026-11-02 (F07), 2026-11-03 00:33 e 00:51; docs/14 |
| R20 | Pacote de teste não é validado no host. Nenhum item de docs/22 foi executado. | Defeito real só aparece com o app instalado e o Resolve | `host_pending` | Executar docs/22 e registrar em HOST_TESTS. | docs/22; `progresso/HOST_TESTS.md` |

## 3. O que a versão faz para reduzir risco

- Nada é apresentado como real sem evidência: estados explícitos (`NOT_RUN`, `Não verificado`, `indisponível`), selos `SIMULATED` e `DEMONSTRAÇÃO`.
- Originais: somente leitura, com importação aditiva verificada (ADR-013). Origem não é alterada nos testes.
- Projeto de terceiros do DaVinci: nunca aberto para escrita. Só projeto `TravelCut_*` novo.
- MCP: somente leitura por padrão, autorização por GUID, `idempotencyKey` obrigatória para mutações e dry-run padrão no `apply_plan`.
- Build: versão única, hash SHA-256 por arquivo, varredura sem mídia, pesos, bancos, caches, `node_modules` e `.drp`.

## 4. Discrepâncias documentais abertas

Estas divergências estão nas fontes, não no código. Não foram corrigidas nesta redação porque estão fora do escopo desta entrega.

- `docs/14-BUILD-DEPLOY-HOST.md`, seção "Estado da última execução (2026-11-03)", ainda descreve o build como **bloqueado** e a suíte com falha determinística. O diário registra o build final concluído com `reproducible: true` às 00:51 e 01:06. A seção precisa de atualização.
- `progresso/HOST_TESTS.md` não tem seção própria para MCP (docs/19 §9) nem HyperFrames (docs/20 §3). Hoje as evidências desses itens devem ser anotadas em "Evidências e erros".
- A legenda de validação em `docs/00-INDICE.md` usa `IMPLEMENTADO`, `SIMULADO`, `HOST_PENDENTE` e `HOST_VALIDADO`. As fontes novas usam `mock_pass`, `host_pending` e `host_pass`.
- `scripts/release/LEIA-ME-HOST.template.md` não lista `TRAVELCUT_FFPROBE_PATH` (R18).
