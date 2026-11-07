# TravelCut Studio — Instruções do Projeto e Governança Claude Code

## 1. Arquitetura Hierárquica da Equipe de Agentes

O desenvolvimento assistido por IA do TravelCut Studio adota uma estrutura hierárquica estrita focada em precisão técnica, supervisão arquitetural e economia de tokens.

### Nível 1 — Orquestrador Principal (Sessão Principal)
- **Modelo:** Claude Fable 5.1 (`claude-fable-5-1`).
- **Papel:** Líder e coordenador do desenvolvimento na sessão principal do Claude Code.
- **Responsabilidades:**
  - Interpretar solicitações do usuário e definir limites estritos de escopo.
  - Planejar incrementos delimitados com critérios de pronto objetivos.
  - Selecionar e delegar tarefas com contexto mínimo ao agente adequado.
  - Executar diretamente tarefas simples cuja delegação consumiria mais tokens que a ação local.
  - Avaliar relatórios de retorno, inspecionar evidências e resolver conflitos.
  - Decidir quando acionar o expert Opus ou especialistas Sonnet.
  - Encerrar o trabalho exatamente no limite solicitado pelo usuário, sem avançar para fases não autorizadas.

### Nível 2 — Expert Arquitetural
- **Agente:** `senior-analyst.md`
- **Modelo:** Claude Opus 5.5 (`claude-opus-5-5`), esforço `max`, limite de 15 turnos.
- **Papel:** Consultor técnico de alto nível para decisões estruturais e investigações complexas.
- **Gatilhos de Acionamento (Uso Excepcional):**
  1. Problema de alta gravidade não resolvido após investigação fundamentada do Sonnet.
  2. Escolha entre soluções arquiteturais concorrentes com trade-offs complexos.
  3. Risco crítico à integridade de dados SQLite, projetos ou arquivos originais de mídia.
  4. Mudanças que afetem múltiplos contratos centrais (.NET / React / IPC / Workers).
  5. Incerteza técnica relevante apontada pelo Fable que justifique o custo de tokens.
- **Entrega esperada:** Parecer técnico formal (causa-raiz, alternativas, recomendação, riscos e validação). Não executa tarefas triviais.

### Nível 3 — Especialistas Permanentes (Sonnet 5.5)
Mantidos exatamente dois especialistas Sonnet para investigações profundas e correções que cruzam camadas:
- **Sonnet A — Integração e Backend (`dotnet-engineer.md`):**
  - .NET 10, C# 13, WPF, WebView2, IPC, contratos TypeScript/C#, SQLite/EF Core, Jobs/concorrência, autenticação nativa, diagnósticos de tela preta e timeout IPC.
- **Sonnet B — Engenharia Audiovisual (`media-engineer.md`):**
  - Whisper, workers Python, PyTorch/CUDA, FFmpeg/FFprobe, HEVC/H.264, geração de proxies, WebView2 Media Playback, timestamps, silêncios/hesitações, VFR e telemetria de mídia. Preservação estrita dos originais.

### Nível 4 — Executores Delimitados (Haiku 5.5)
Executam tarefas específicas, pequenas e com contexto mínimo:
- **`frontend-engineer.md`:** Componentes React/TypeScript, Tailwind, mockups TravelCut, IPC client e ajustes de interface.
- **`mcp-engineer.md`:** Ferramentas MCP locais com SDK C# oficial e contratos com Application Core.
- **`davinci-integrator.md`:** Contratos de integração mock/real DaVinci Studio (VM sem Resolve).
- **`qa-auditor.md`:** Auditorias técnicas em modo estritamente **somente leitura**. Não altera cache, bancos nem arquivos de mídia.
- **`documentation-engineer.md`:** Manuais, documentação de arquitetura, referências técnicas e changelogs.

---

## 2. Matriz de Roteamento de Tarefas

| Tipo de Solicitação | Responsável Preferencial | Escalonamento se Bloqueado |
|---|---|---|
| Ajuste visual em componente React / Tailwind | `frontend-engineer` (Haiku) | Fable 5.1 |
| Teste unitário simples em domínio ou componente | Haiku da especialidade | Sonnet A ou B |
| Correção pontual de documentação ou changelog | `documentation-engineer` (Haiku) | Fable 5.1 |
| Ferramenta MCP local simples ou contrato C# SDK | `mcp-engineer` (Haiku) | Sonnet A |
| Mock/Bridge de DaVinci Studio na VM | `davinci-integrator` (Haiku) | Sonnet A |
| Auditoria de conformidade / diff pré-commit | `qa-auditor` (Haiku, read-only) | Sonnet / Opus |
| Falha IPC, tela preta, estado divergente backend/UI | `dotnet-engineer` (Sonnet A) | Opus 5.5 |
| Crash em EF Core, migração SQLite, jobs concorrentes | `dotnet-engineer` (Sonnet A) | Opus 5.5 |
| Erro de codec, proxy que não toca, VFR, timebase | `media-engineer` (Sonnet B) | Opus 5.5 |
| Whisper lento em GPU, PyTorch/CUDA, worker travado | `media-engineer` (Sonnet B) | Opus 5.5 |
| Decisão arquitetural de alto risco ou corrupção de dados | `senior-analyst` (Opus 5.5) | Parecer ao Fable |
| Coordenação geral, planejamento e integração | Fable 5.1 (Sessão Principal) | Usuário |

---

## 3. Diretrizes de Governança e Economia de Tokens

1. **Contexto Mínimo:** Não leia o repositório inteiro nem checklists longos no início de cada sessão. Consulte apenas os arquivos e seções documentais relevantes à tarefa imediata.
2. **Delegação Enxuta:** Passe aos subagentes apenas o `taskId`, caminhos permitidos/proibidos, entradas/saídas e critério de pronto.
3. **Execução Direta vs Delegação:** Tarefas triviais (edição de poucas linhas óbvias, checagem rápida) devem ser feitas diretamente pelo Fable. Delegar tem custo de tokens de bootstrap do subagente.
4. **Relatórios Compactos:** Subagentes retornam relatórios concisos: status, intervenção, arquivos modificados, testes rodados (comando exato e resultado), riscos e pendências.
5. **Limites de Turnos:**
   - Haiku: 10 a 15 turnos.
   - Sonnet: 15 a 20 turnos.
   - Opus: 10 a 15 turnos para parecer técnico.
6. **Concorrência Controlada:** No máximo dois subagentes simultâneos. Nenhum par de agentes pode editar os mesmos arquivos concorrentemente.
7. **Sem Auto-Avanço de Fases:** O prompt do usuário define o limite da execução. Não inicie fases, checklists ou auditorias gerais não solicitadas.

---

## 4. Regras Operacionais Críticas de Ambiente e Segurança

1. **Shell Operacional no Windows:**
   - O ambiente utiliza Git Bash/MSYS2 sob a ferramenta Bash.
   - **Proibido executar cmdlets PowerShell diretamente no Bash** (ex.: `Get-Process`, `Select-String`, sintaxe `$env:VAR`). Se precisar de PowerShell, invoque via script ou ferramentas dedicadas.
   - Respeite o hook `.claude/hooks/guard-shell.cjs` ativado em `PreToolUse`.
2. **Isolamento da VM e DaVinci Resolve:**
   - A VM de desenvolvimento **NÃO possui DaVinci Resolve instalado**.
   - Utilize mocks (`MockResolveAdapter`) e testes simulados. Homologação real com DaVinci ocorre no host Windows.
3. **Proteção Inegociável de Arquivos de Mídia:**
   - **Nunca modificar, sobrescrever ou deletar arquivos de vídeo originais** do usuário.
   - Todas as operações de proxy, corte, análise e cache devem ser gravadas em pastas temporárias/cache designadas.
4. **Persistência de Evidências:**
   - Não declarar conclusão de tarefas sem comando de teste executado e aprovado.
   - Atualizar `progresso/IMPLEMENTATION_LOG.md` imediatamente após entregas validadas.
5. **Controle de Versão (Git):**
   - Trabalhe na branch designada ou `main` após checar limpeza do status.
   - Preservar arquivos e configurações locais não rastreados.
   - Não executar `git push` sem autorização explícita do usuário.
