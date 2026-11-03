# ADRs — decisões e pendências

| ID | Decisão | Estado | Motivação |
|---|---|---|---|
| ADR-001 | Migrar Rust/Tauri para **.NET 10 + WPF/WebView2 + React** | Aprovada | reduzir complexidade de desenvolvimento, preservar mockups |
| ADR-002 | WPF apenas como host, interface majoritariamente React | Recomendada, validar | uma camada visual, UI moderna sem duplicação |
| ADR-003 | Core C# independente de frameworks de UI e editor | Aprovada em conceito | testabilidade, execução via UI/MCP |
| ADR-004 | SQLite/EF Core para estado, artefatos grandes em arquivo | Aprovada em conceito | local, simples, seguro |
| ADR-005 | FFmpeg/FFprobe, Python/Whisper large-v3 e HyperFrames integrados, não reimplementados em C# | Aprovada em conceito | reuso de motores existentes |
| ADR-006 | DaVinci fora da VM, via adapter mock/real | Aprovada | limite de infraestrutura |
| ADR-007 | Orquestrador Opus 5.5, analista Sonnet 5.5, executores Haiku 5.5 | Aprovada | separação de raciocínio e tarefas econômicas |
| ADR-008 | Timeline do TravelCut é revisão/planejamento, DaVinci é NLE final | Aprovada | limitar escopo |
| ADR-009 | Perfil de edição e sugestões reversíveis, preservação absoluta das fontes | Aprovada | evitar perda de informação |
| ADR-010 | LibVLCSharp vs player WebView2 vs engine custom | **A validar** | seek por frame, áudio, overlays, codecs, cache |
| ADR-011 | Bridge entre .NET e DaVinci (Python scripting vs MCP) | **A validar no host** | disponibilidade de operações reais |
| ADR-012 | Compilar .NET 10 na VM Windows 10 Pro 22H2 | **Risco / não suportado oficialmente** | testes de build são experimentais; host suportado |
| ADR-014 | Desfazer/refazer de decisões como pilha (Ctrl+Z) e corte automático só para silêncio | **Aprovada pelo orquestrador** (2026-11-02, padrão conservador; revisável pelo usuário) | Desfazer volta à decisão de usuário anterior (aprovar → rejeitar → desfazer = aprovado); só volta a Proposed quando não há decisão anterior. Refazer reaplica a última desfeita. Histórico continua append-only. Corte automático do planejador aceito apenas para `silence` (não `pause`, `filler`, `repetition`, `false_start`, `prolongation`): faixa "high" é heurística não calibrada e docs/07 exige revisão para repetições e pausas de ritmo/humor. |
| ADR-015 | Bridge DaVinci: subprocesso Python por comando (stdin/stdout JSON, `resolve-bridge/1`) sobre a API oficial de scripting; MCP do Resolve não é usado nesta fase | **Aprovada pelo orquestrador** (2026-11-02; homologação no host pendente; revisável pelo usuário) | Refina ADR-011. API de scripting é a interface documentada pela Blackmagic; subprocesso por comando isola falhas/travamentos do Resolve do app e respeita timeout. Segurança: só cria projeto NOVO `TravelCut_*`, nunca abre/salva projetos existentes para escrita; aplicar exige dry-run (assemblyFingerprint), confirmação de troca do projeto aberto e chave de idempotência; snapshot = `.drp` real exportado após aplicar. Sem fallback silencioso ao mock; mock só por `TRAVELCUT_EDITOR_ADAPTER=mock` e sempre `SIMULATED`. Velocidade ≠ 1×, transições e faixas além de V1(+áudio vinculado) são rejeitadas com `CAPABILITY_UNSUPPORTED` (lacuna honesta). Fallback seguro: manifesto `editor-assembly/1` em `3-Trabalho/editor`. Alternativas: MCP do Resolve (disponibilidade/versão não confirmadas), automação de UI (proibida sem consentimento), EDL/FCPXML (possível fallback futuro). |
| ADR-013 | Ingestão aditiva em `1-Bruto`/`2-Assets` pela Tela 07 (Importar mídia) | **Aprovada pelo usuário** (2026-11-02) | o mockup 07 copia arquivos externos para o projeto; única escrita permitida nessas pastas é a criação de arquivo novo via importação explícita: `.tcpart` → SHA-256 verificado → rename, nunca sobrescreve/move/apaga, origem intocada. Todas as demais operações seguem RNF-01 (somente leitura). Alternativa: registrar por referência sem copiar (toggle "Copiar" desligado). |

## Alterações de ADR

Mudanças em decisão aprovada precisam de justificativa, alternativas e impacto de migração. Sonnet recomenda para problemas complexos, Opus audita e registra; se afetar escolha aprovada do usuário, pedir consentimento. Não apagar histórico de decisões.
