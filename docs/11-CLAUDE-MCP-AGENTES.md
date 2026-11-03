# Claude Code, MCP, hierarquia de agentes

## Hierarquia aprovada

**Principal em sessão Opus 5.5 (`claude-opus-5-5`)**: lê README, planeja, define tarefas, delega, integra, audita, atualiza fases, inicia a próxima fase após gate. **Analista sênior Sonnet 5.5 (`claude-sonnet-5-5`)**: revisa arquitetura difícil, parsing de mídias, sincronização/timebase, segurança/concorrência e bugs de integração. **Operários Haiku 5.5 (`claude-haiku-5-5`)**: `.NET`, frontend, mídia, DaVinci mock, MCP, QA, documentação. Os modelos efetivos devem ser conferidos em `/tasks` quando possível; políticas do cliente podem prevalecer.

## Orquestração

- O Opus define `taskId`, objetivo e testes com escopo; envia apenas documentos relevantes; integra cada resultado e dispara QA.
- Sonnet analisa incertezas críticas antes de mudanças extensas; Haiku nunca reinterpreta decisões aprovadas sem escalar.
- Paralelismo máximo recomendado VM: 2 tarefas pesadas e em arquivos distintos; evitar que dois agentes alterem o mesmo módulo.
- Subagentes não têm autonomia para publicar, instalar software global, sobrescrever originais nem trocar de modelo silenciosamente.
- Sessão ativa pode avançar entre fases; nenhuma tarefa segue "em segundo plano" após a sessão encerrar. Checkpoint documenta retomada.

## MCP TravelCut (estado atual)

**Estado em 2026-11-02:** `src/TravelCut.Mcp` existe, usa o [SDK oficial C#](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol` 2.3.0) em transporte **stdio** e é fachada fina sobre o Application Core. Setup, guardas, roteiro de teste e limitações estão em **[docs/19-MCP-TRAVELCUT.md](19-MCP-TRAVELCUT.md)**. Modelo de configuração de projeto: `.mcp.json.example` na raiz (o `.mcp.json` ativo só existe com decisão do usuário).

- **Leitura (sempre registrada, 8):** `list_projects`, `get_inventory`, `get_transcript`, `get_suggestions`, `get_edit_plan`, `job_status`, `get_editor_capabilities`, `get_motion_capabilities`.
- **Mutação (só com `--allow-mutations`, 3):** `submit_review`, `queue_analysis`, `apply_plan`. Exigem projeto autorizado e `idempotencyKey`. `apply_plan` começa em dry-run e exige confirmação humana da troca do projeto aberto no DaVinci.
- **Implementado mas não validado no host:** o editor real (DaVinci) continua `host_pending`; os testes automatizados usam o simulador (`TRAVELCUT_EDITOR_ADAPTER=mock`).
- **Não existe:** shell, execução arbitrária, acesso a arquivos arbitrários, cancelamento de job via MCP, compare-and-swap de revisão em `decision.submit` (ver limitações em `docs/19`).

Regra mantida: ferramentas são fachada de Application (não duplicam regra). Mutações registram intenção e resultado em stderr, sem conteúdo sensível. Nunca aceitar execução arbitrária de shell.

## Separar agente do produto e agente de desenvolvimento

Os subagentes deste kit **desenvolvem o software**; não são o sistema de IA que classifica vídeos em produção. O produto deve permitir futura conexão ao Claude por MCP para decisões editoriais; não presumir que um subagente Haiku rode inferência multimodal local automaticamente.

## Controle de custo

Reaproveitar inventário/cache; prompts curtos; Skills sob demanda; QA em diffs; não duplicar análise entre agentes; escalar ao Sonnet por risco/custo, não automaticamente toda tarefa. Reportar consumo apenas quando medido pelo cliente.

## Skill de edição em produção — integração PLANEJADA (não implementada)

Estado em 2026-11-02 (F09): **apenas planejada**. Nada no produto executa uma Skill de edição hoje.

- **O que seria:** uma Skill do Claude (instalada pelo usuário no próprio Claude Code, fora do repositório do produto) que, conectada ao MCP do TravelCut (`src/TravelCut.Mcp`, ver `docs/19-MCP-TRAVELCUT.md`), lê inventário/transcrição/sugestões e propõe decisões editoriais seguindo `docs/07-REGRAS-EDITORIAIS.md` (preservar humor e reações, revisão humana para ambiguidades).
- **O que NÃO é:** os subagentes e Skills de `.claude/agents/` e `.claude/skills/` deste repositório. Esses **desenvolvem o software**; não fazem parte do produto e não classificam vídeos do usuário.
- **Limites previstos:** só ferramentas MCP read-only por padrão; mutações (`submit_review`, `queue_analysis`, `apply_plan`) exigem o servidor iniciado com `--allow-mutations`, projeto autorizado explicitamente e chave de idempotência; `apply_plan` começa sempre em dry-run. A Skill nunca recebe mídia bruta — só metadados, transcrição e sugestões (mídia local não sai para APIs pagas).
- **Pendências antes de implementar:** definir o texto da Skill com o usuário, medir custo por projeto, testes com projeto sintético e homologação no host. Até lá, qualquer menção na UI deve dizer "planejado".
