# 19 — Servidor MCP TravelCut (setup e uso)

**Estado:** `IMPLEMENTADO` no código (`src/TravelCut.Mcp`) com testes automatizados de transporte stdio em `tests/TravelCut.Mcp.Tests`.
Este documento descreve o que o código faz **hoje**, lido diretamente dos arquivos citados. A execução dos testes e do build
não foi feita na redação deste documento; o roteiro da seção 9 diz o que rodar. Não há homologação no host: o editor real
(DaVinci Resolve) continua `host_pending` (ver `docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md`).

## Índice

1. [O que é](#1-o-que-é)
2. [Compilar e localizar o executável](#2-compilar-e-localizar-o-executável)
3. [Registrar no Claude Code (escopo de projeto)](#3-registrar-no-claude-code-escopo-de-projeto)
4. [Configuração do operador](#4-configuração-do-operador)
5. [Ferramentas](#5-ferramentas)
6. [Guardas](#6-guardas)
7. [Erros estruturados](#7-erros-estruturados)
8. [Logs e dados sensíveis](#8-logs-e-dados-sensíveis)
9. [Roteiro de teste local](#9-roteiro-de-teste-local)
10. [O que não existe](#10-o-que-não-existe)
11. [Convivência com outros MCP](#11-convivência-com-outros-mcp)
12. [Limitações conhecidas](#12-limitações-conhecidas)

---

## 1. O que é

`TravelCut.Mcp` é um servidor MCP local (transporte **stdio**, SDK oficial `ModelContextProtocol` 2.3.0) que serve como
**fachada fina** sobre o mesmo Application Core usado pelo Desktop WPF. Cada ferramenta converte a chamada em uma
requisição IPC (`IpcDispatcher`) com o mesmo tipo que a UI usa. Não há regra de negócio duplicada no servidor.

- Assembly: `TravelCut.Mcp.exe` (AssemblyName `TravelCut.Mcp`, `net10.0`, `OutputType Exe`).
- Nome do servidor MCP anunciado: `travelcut`, versão `0.1.0`.
- stdout é exclusivo do protocolo JSON-RPC; logs vão para stderr (ver seção 8).
- O servidor não abre janela e não inicia o Desktop. Não há código de rede próprio na fachada MCP (não verificado além disso em toda a cadeia de dependências).

## 2. Compilar e localizar o executável

A partir da raiz do repositório:

```powershell
dotnet build src/TravelCut.Mcp
```

Executável esperado (padrão do SDK para `Exe` em Windows; o smoke test procura `TravelCut.Mcp.exe` ao lado da DLL):

```
src\TravelCut.Mcp\bin\Debug\net10.0\TravelCut.Mcp.exe
```

Para Release, troque `Debug` por `Release` no comando e no caminho. Se o `.exe` não existir, o smoke test cai para
`dotnet TravelCut.Mcp.dll`; use esse caminho só para diagnóstico.

Ajuda do servidor (escreve em stderr e sai com código 0, sem falar protocolo):

```powershell
src\TravelCut.Mcp\bin\Debug\net10.0\TravelCut.Mcp.exe --help
```

## 3. Registrar no Claude Code (escopo de projeto)

**Regra de decisão:** o arquivo `.mcp.json` ativo na raiz **só deve ser criado com decisão explícita do usuário**.
O repositório entrega apenas o modelo `.mcp.json.example` (somente leitura, sem `--allow-mutations`).

Passos:

1. Compile (seção 2) e anote o caminho absoluto do `TravelCut.Mcp.exe`.
2. Copie `.mcp.json.example` para `.mcp.json` na raiz do projeto **somente após a decisão do usuário**.
3. Substitua os placeholders `<...>` no `.mcp.json`: caminho do executável, GUID de projeto autorizado e, se usado, caminho do banco de teste.
4. Abra o Claude Code neste projeto. Na primeira vez, o cliente pede aprovação do servidor de projeto (confirmar o comportamento na versão instalada). Verifique com `/mcp`.

Formato usado (escopo de projeto, `.mcp.json` na raiz):

```json
{
  "mcpServers": {
    "travelcut": {
      "command": "<CAMINHO_ABSOLUTO>/src/TravelCut.Mcp/bin/Debug/net10.0/TravelCut.Mcp.exe",
      "args": [],
      "env": {
        "TRAVELCUT_MCP_PROJECTS": "<GUID_DE_PROJETO_AUTORIZADO>",
        "TRAVELCUT_MCP_DATABASE": "<CAMINHO_ABSOLUTO_DO_BANCO_DE_TESTE.db>",
        "TRAVELCUT_EDITOR_ADAPTER": "mock"
      }
    }
  }
}
```

Alternativa via CLI (**a confirmar** na versão instalada do Claude Code; a sintaxe abaixo não foi executada nesta redação):

```powershell
claude mcp add --transport stdio --scope project --env TRAVELCUT_MCP_PROJECTS=<GUID> --env TRAVELCUT_EDITOR_ADAPTER=mock travelcut -- <CAMINHO_ABSOLUTO>\src\TravelCut.Mcp\bin\Debug\net10.0\TravelCut.Mcp.exe
```

Nunca registrar o servidor em escopo de usuário ou global sem autorização. Nunca editar a configuração global do Claude Code para isto.

## 4. Configuração do operador

A configuração vem da linha de comando e do ambiente, nunca de argumentos enviados pelo cliente MCP (`McpServerConfiguration`).

| Entrada | Efeito | Padrão |
|---|---|---|
| `--allow-mutations` | Registra as 3 ferramentas de mutação | desligado (somente leitura) |
| `--project <guid>` | Autoriza um projeto (repetível) | nenhum autorizado |
| `--migrate-database` | Atualiza o schema do banco na inicialização (mesmo passo do Desktop) | desligado: o servidor só verifica o schema e falha se não estiver pronto (seção 4.1) |
| `TRAVELCUT_MCP_PROJECTS=<guid1>;<guid2>` | Autoriza projetos (separador `;`) | vazio |
| `TRAVELCUT_MCP_DATABASE=<caminho absoluto .db>` | Banco alternativo | banco do Desktop: `%LOCALAPPDATA%\TravelCutStudio\travelcut.db` |
| `TRAVELCUT_EDITOR_ADAPTER=mock` | Editor simulado (`SIMULATED`, sem `.drp`) | qualquer outro valor/ausente = **adaptador DaVinci real** (ver abaixo) |
| `TRAVELCUT_MOTION_ADAPTER=mock` | Animação simulada (`SIMULATED`) | ausente = HyperFrames real |
| `TRAVELCUT_HYPERFRAMES_CLI` | Caminho do CLI HyperFrames (usado só pelo renderer real) | ausente = `NOT_INSTALLED` |

Erros de configuração (argumento desconhecido, GUID inválido, `TRAVELCUT_MCP_DATABASE` relativo) fazem o processo sair com
código 2 e mensagem em stderr, sem protocolo. Valores sensíveis não aparecem nas mensagens.

**Editor padrão = adaptador DaVinci real.** Sem `TRAVELCUT_EDITOR_ADAPTER=mock`, o MCP usa o adaptador real do DaVinci Resolve,
como o Desktop. Não há fallback silencioso para o simulador (ADR-015, `docs/15-DECISOES-ARQUITETURAIS.md`). Na VM de
desenvolvimento, sem Resolve instalado, `get_editor_capabilities` responde `NOT_INSTALLED` e `apply_plan` falha com o
código correspondente. Isso é o comportamento esperado, não defeito. Resultado simulado (`SIMULATED`) só aparece com o
simulador explícito (`mock_pass` = prova do caminho de código, não integração real).

### 4.1 Banco e schema

- **Sem `--migrate-database` (padrão):** o servidor não altera o banco. Na inicialização ele verifica, em modo somente
  leitura e sem criar arquivo, a existência das tabelas `Projects`, `MediaAssets`, `PlanRevisions`, `EditDecisions` (com
  a coluna `IsRedo`) e `SchemaMarkers`. Se faltar algo (inclusive banco inexistente ou vazio), o processo sai com código
  **3** e escreve no stderr a orientação "Abra o TravelCut Desktop uma vez para atualizar o banco". Nada vai para stdout.
- **Com `--migrate-database`:** o servidor executa `EnsureSchemaUpdated()`, o mesmo passo do Desktop. Pode alterar o
  schema do banco apontado; use-o só em banco descartável ou depois de decisão do usuário.
- O servidor usa `Default Timeout=15` na connection string (espera de até 15 s quando outro processo mantém o banco
  ocupado). O `journal_mode` não é alterado pelo servidor.
- Para testes, use sempre `TRAVELCUT_MCP_DATABASE` apontando para um banco descartável.

### 4.2 Trava entre processos (Desktop e MCP no mesmo projeto)

Desktop e MCP podem rodar ao mesmo tempo sobre o mesmo projeto. Duas operações usam uma trava por arquivo, válida entre
processos: `<projeto>/3-Trabalho/.locks/<operação>.lock`, aberto com `FileShare.None` e `DeleteOnClose`. O arquivo guarda
só pid e horário UTC, sem caminhos.

| Operação | Quem adquire | Espera | Se ocupada |
|---|---|---|---|
| `editor-apply` | `editor.apply` (Desktop e `apply_plan` com `dryRun=false`) | até 2 s | `OPERATION_IN_PROGRESS` ("outra aplicação deste projeto está em andamento (app ou MCP)"), **sem chamar o adaptador** |
| `analysis` | início de análise (`pipeline.start`, mantida até o job terminar) | até 1 s | `PIPELINE_IN_PROGRESS` |

- A checagem de idempotência de `editor-apply` acontece **depois** de adquirir a trava. Assim, o segundo processo vê o
  registro gravado pelo primeiro e reaproveita o resultado pela chave.
- `OPERATION_IN_PROGRESS` e `PIPELINE_IN_PROGRESS` são transitórios: não entram no cache de idempotência, então a mesma
  chave pode ser repetida depois.
- Dentro do mesmo processo, um gate por raiz de projeto serializa as aplicações com a mesma espera de 2 s.
- Limitação: a exclusão depende de `FileShare.None` no Windows (alvo do projeto). Não foi validada em Unix nem em
  unidade de rede. Teste real com Desktop e MCP abertos simultaneamente depende de homologação no host (seção 12).

## 5. Ferramentas

As ferramentas de leitura estão sempre registradas. As de mutação só existem com `--allow-mutations`.
Os parâmetros são os declarados nos atributos `[Description]` de `src/TravelCut.Mcp/Tools/*.cs`. Rota IPC é o tipo
enviado ao `IpcDispatcher` (constantes em `TravelCutMcpFacade.IpcTypes`).

### 5.1 Leitura (8)

| Nome | Tipo | Parâmetros | Rota IPC | Observação |
|---|---|---|---|---|
| `list_projects` | leitura | — | `project.recent` | Até 10 mais recentes, filtrados pelos autorizados; sem autorizados, lista vazia |
| `get_inventory` | leitura | `projectId` | `media.list` | id, caminho relativo, tipo, duração, status de metadados |
| `get_transcript` | leitura | `projectId`, `mediaId` | `analysis.getMedia` | Devolve só `mediaId`, `transcriptionStatus`, `transcriptionReason`, `transcript`. Conteúdo sensível |
| `get_suggestions` | leitura | `projectId`, `status?` (Proposed, Approved, Rejected, Deferred) | `suggestions.list` | Filtro de status sem diferenciar maiúsculas; valor inválido = `INVALID_ARGUMENT` |
| `get_edit_plan` | leitura | `projectId`, `revision?` (inteiro ≥ 1) | `plan.get` | Sem revisão = mais recente; sem plano = `null` (não é erro) |
| `job_status` | leitura | `projectId`, `jobId?` | `pipeline.status` | Com `jobId` diferente da análise atual = `JOB_NOT_FOUND` |
| `get_editor_capabilities` | leitura | — | `editor.capabilities` (`EditorIpcHandler.CapabilitiesType`) | Estado do editor e nível de verificação. Não cria nem altera projeto |
| `get_motion_capabilities` | leitura | — | `motion.capabilities` (`MotionIpcHandler.CapabilitiesType`) | Estado de Node/CLI e templates. Não renderiza |

### 5.2 Mutação (3, somente com `--allow-mutations`)

| Nome | Tipo | Parâmetros | Rotas IPC | Observação |
|---|---|---|---|---|
| `submit_review` | mutação | `projectId`, `suggestionId`, `action` (approve, reject, defer, modify), `reviewer` (1 a 100 caracteres), `idempotencyKey`, `notes?` (até 2000), `modifiedStartSeconds?`, `modifiedEndSeconds?` | `suggestions.list` (confere pertença ao projeto), depois `decision.submit` | `modified*` só com `action=modify`; início ≥ 0 e fim > início, valores finitos |
| `queue_analysis` | mutação | `projectId`, `idempotencyKey`, `mediaIds?` (1 a 200, sem repetição; omitido = todas) | `pipeline.start` (sem estágios, idioma nulo, `Force=false`) | Mesmos padrões da UI |
| `apply_plan` | mutação (destrutiva no editor) | `projectId`, `idempotencyKey`, `revision?`, `dryRun` (padrão `true`), `assemblyFingerprint?`, `confirmProjectSwitch` (padrão `false`) | `editor.prepareApply` (dry-run), `editor.apply` (aplicar) | Dry-run não altera o editor e devolve `assemblyFingerprint`. Aplicar exige `revision`, fingerprint e `confirmProjectSwitch=true` |

As anotações MCP dos atributos são `ReadOnly` nas leituras; `apply_plan` é anotada como `Destructive = true`.
A anotação é informativa para o cliente; a proteção real está nas guardas da seção 6.

## 6. Guardas

Implementadas em `TravelCutMcpFacade` (`ExecuteReadAsync`, `ExecuteMutationAsync`, `RequireAuthorizedProject`) e em
`McpIdempotencyCache`.

1. **Mutações desligadas por padrão.** Sem `--allow-mutations`, as ferramentas de mutação nem são registradas. Se a
   chamada chegar mesmo assim, a fachada responde `MUTATIONS_DISABLED`.
2. **Projetos autorizados.** Só GUIDs listados em `--project` ou `TRAVELCUT_MCP_PROJECTS` podem ser usados. Leitura e
   mutação verificam isso. Projeto fora da lista = `PROJECT_NOT_AUTHORIZED`. O cliente não consegue ampliar a lista.
   Não há verificação de caminho de arquivo: a autorização é só por GUID.
3. **Ordem da mutação:** modo habilitado → projeto (formato, depois autorização) → `idempotencyKey` → **validação do corpo**
   (campos obrigatórios, enums, `reviewer`, intervalo, `revision`, fingerprint, confirmação) → cache → IPC. Falhas de
   formato, autorização e validação **não entram no cache**: erro de validação não consome a chave. Basta corrigir a
   entrada e repetir a mesma `idempotencyKey`.
4. **Idempotência.** `idempotencyKey` é obrigatória, no formato `[A-Za-z0-9_-]{1,100}`. O cache é por (ferramenta, chave) e
   guarda um hash SHA-256 dos argumentos **validados e normalizados** (por exemplo, `reviewer` sem espaços nas pontas,
   ação em minúsculas, lista de mídias em ordem canônica):
   - mesma chave e mesmos argumentos: devolve o resultado já obtido, sem executar de novo;
   - mesma chave com argumentos diferentes: `IDEMPOTENCY_KEY_REUSED`;
   - chamadas simultâneas com a mesma chave compartilham uma única execução;
   - a execução roda com o **token do servidor** (desligamento do processo), não com o token do primeiro chamador. Se o
     cliente cancelar, a mutação continua e os demais chamadores recebem o resultado; cada chamador só desiste da própria
     espera;
   - erros transitórios (`INTERNAL_ERROR`, `TIMEOUT`, `EDITOR_UNAVAILABLE`, `PIPELINE_IN_PROGRESS`, `OPERATION_IN_PROGRESS`,
     `DISK_FULL`) saem do cache, para que a mesma chave possa ser repetida;
   - limite de 1000 entradas: ao exceder, saem as mais antigas já concluídas. Uma chave antiga removida é executada de novo
     se repetida (limite documentado; entradas em voo não são removidas).
   O cache é só em memória (ver seção 12).
5. **Dry-run do `apply_plan`.** O padrão é `dryRun=true`. Aplicar exige `revision`, `assemblyFingerprint` do dry-run e
   `confirmProjectSwitch=true`. Sem `revision` ou fingerprint, a chamada falha antes do IPC. Use chaves diferentes para o
   dry-run e para a aplicação.
6. **Pertença de sugestão e mídia.** `submit_review` confere, antes da decisão, que a `suggestionId` está na lista de sugestões
   do projeto autorizado (`SUGGESTION_NOT_FOUND` caso contrário), porque `decision.submit` não recebe `projectId`.
   `queue_analysis` e `get_transcript` conferem que a(s) mídia(s) pertencem ao projeto autorizado (`MEDIA_NOT_FOUND`). Em
   `queue_analysis`, uma única mídia de outro projeto recusa a lista inteira, sem iniciar análise.
7. **Confirmação de troca de projeto depende do usuário humano.** O servidor só verifica o booleano
   `confirmProjectSwitch=true`. Ele não tem como confirmar que o usuário aprovou a troca do projeto aberto no DaVinci.
   O agente deve pedir essa aprovação antes de enviar `true`.
8. **Sem mutação automática pelo servidor.** Nenhuma ferramenta executa ação sem chamada explícita do cliente.

## 7. Erros estruturados

Qualquer falha de ferramenta volta como resultado com `isError=true` e texto JSON `{"code": "...", "message": "..."}`.
Exceções cruas não chegam ao cliente. O `message` é sanitizado (caminhos absolutos Windows/UNC viram `[caminho]` e a
mensagem é cortada em 500 caracteres).

Códigos próprios do servidor (`McpErrorCodes`):

| Código | Quando |
|---|---|
| `MUTATIONS_DISABLED` | Mutação chamada sem `--allow-mutations` |
| `PROJECT_NOT_AUTHORIZED` | Projeto fora da lista autorizada |
| `INVALID_ARGUMENT` | GUID, filtro, ação, intervalo, revisão, mídias, `idempotencyKey` ou fingerprint inválidos |
| `IDEMPOTENCY_KEY_REUSED` | Mesma chave com argumentos diferentes |
| `CONFIRMATION_REQUIRED` | `apply_plan` com aplicação sem `confirmProjectSwitch=true` |
| `SUGGESTION_NOT_FOUND` | Sugestão não pertence ao projeto |
| `MEDIA_NOT_FOUND` | Mídia não pertence ao projeto autorizado (em `queue_analysis` e `get_transcript`) |
| `JOB_NOT_FOUND` | `jobId` não é a análise atual do projeto |
| `INTERNAL_ERROR` | Exceção não prevista; detalhe só no log (stderr) |

Códigos do `IpcDispatcher` (por exemplo `EDITOR_UNAVAILABLE`, `PROJECT_NOT_CURRENT`, `PIPELINE_IN_PROGRESS`,
`OPERATION_IN_PROGRESS`, `PROJECT_VERSION_CONFLICT`) são repassados sem alteração. `OPERATION_IN_PROGRESS` indica trava
de `editor-apply` ocupada por outro processo (app ou MCP), com o adaptador não chamado: repita depois, com a mesma chave. A lista de referência está em `docs/05-CONTRATOS-E-INTEGRACOES.md`, seção "Erros estruturados".

Respostas de sucesso são JSON do contrato, com as chaves de caminho absoluto removidas (`rootPath`, `fullPath`,
`absolutePath`, `outputAbsolutePath`). O agente trabalha com IDs e caminhos relativos.

## 8. Logs e dados sensíveis

- Logs vão **somente para stderr**, em UTF-8 sem BOM. stdout é o protocolo.
- Log de mutação registra nome da ferramenta, GUID do projeto, `idempotencyKey`, resultado (`ok`/`erro`), código de erro e
  se a resposta foi reaproveitada do cache. Não registra conteúdo de transcrição, notas, nome de revisor nem caminhos.
- Erros internos registram no stderr só o tipo da exceção e a mensagem sanitizada (caminhos Windows, UNC e POSIX viram
  `[caminho]`), sem stack trace. O cliente recebe só `INTERNAL_ERROR` genérico. Ainda assim, revise o stderr antes de
  colá-lo em relatórios.
- Recusa de inicialização por schema (seção 4.1) escreve só o motivo genérico (ex.: "tabela MediaAssets ausente"), sem caminho.
- EF Core só registra `Warning` ou acima (sem SQL).
- Transcrições são dados sensíveis: não colar em logs, issues ou relatórios de homologação.

## 9. Roteiro de teste local

Ordem obrigatória: **leitura primeiro, com editor simulado e banco descartável**. Mutações só depois. Nada deste roteiro
foi executado na redação deste documento; registre os resultados reais em `progresso/` (o orquestrador faz isso).

**Passo 1. Build e testes automatizados** (a partir da raiz):

```powershell
dotnet build src/TravelCut.Mcp
dotnet test tests/TravelCut.Mcp.Tests
```

Esperado: os testes `McpStdioSmokeTests` iniciam o executável por stdio, usam banco temporário e `TRAVELCUT_EDITOR_ADAPTER=mock`,
e verificam que sem `--allow-mutations` só as 8 leituras aparecem.

**Passo 2. Ajuda e argumentos inválidos (sem protocolo):**

```powershell
src\TravelCut.Mcp\bin\Debug\net10.0\TravelCut.Mcp.exe --help
src\TravelCut.Mcp\bin\Debug\net10.0\TravelCut.Mcp.exe --shell
```

Esperado: a primeira linha de ajuda vai para stderr com código 0; `--shell` sai com código 2 e não escreve em stdout.

**Passo 3. Leitura com Claude Code (sem `--allow-mutations`):**

1. Defina um banco descartável em caminho absoluto (ex.: pasta temporária fora de `1-Bruto/` e `2-Assets/`). Não use o banco do Desktop.
   O banco precisa de schema: abra o Desktop uma vez com esse banco ou use `--migrate-database` (seção 4.1). Sem isso o servidor sai com código 3.
2. Configure `.mcp.json` (decisão do usuário), com `TRAVELCUT_EDITOR_ADAPTER=mock` (somente para este roteiro de simulação) e um GUID autorizado.
3. Em `/mcp`, confirme que `travelcut` está conectado e que só as 8 leituras aparecem.
4. Chame `list_projects`. Banco vazio = lista vazia (esperado, não é erro).
5. Chame `get_inventory` com GUID **não** autorizado. Esperado: `PROJECT_NOT_AUTHORIZED`.
6. Chame `get_editor_capabilities`. Com mock, deve indicar simulação (`SIMULATED`), não DaVinci real.
7. Chame `get_motion_capabilities` e confirme que nada é renderizado.

**Passo 4. Mutações (só após o passo 3 aprovado e decisão do usuário):**

1. Registre uma segunda configuração **de teste** com `--allow-mutations` (não sobrescreva a leitura sem decisão).
2. Verifique que as 3 mutações aparecem em `tools/list`.
3. `apply_plan` com `dryRun=true` (padrão): guarde o `assemblyFingerprint`. Confirme que o editor simulado não foi alterado.
4. Repita a mesma chave com outros argumentos: esperado `IDEMPOTENCY_KEY_REUSED`.
5. Chamadas sem `idempotencyKey` válida: esperado `INVALID_ARGUMENT`.
6. Aplicação (`dryRun=false`) sem `confirmProjectSwitch=true`: esperado `CONFIRMATION_REQUIRED`.
7. Reinicie o servidor e repita a chave de uma mutação já executada: o cache está vazio, então a operação é executada de novo (ver seção 12).
8. Com o Desktop aberto no mesmo projeto e uma análise em andamento, `queue_analysis` deve responder `PIPELINE_IN_PROGRESS`.
   Com aplicação em andamento, `apply_plan` com `dryRun=false` deve responder `OPERATION_IN_PROGRESS`. Este item só vale
   para o host (ver seção 12).

**Passo 5. DaVinci real:** fora deste roteiro. Depende de `docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md` e do host. Não rode
`apply_plan` com `dryRun=false` contra o DaVinci real antes disso.

## 10. O que não existe

- Ferramenta de shell, execução de comando ou leitura/escrita de arquivo arbitrário. Nada disso é exposto.
- Acesso a `1-Bruto/` ou `2-Assets/` pelo MCP além do que o IPC já expõe. Caminhos absolutos são removidos das respostas.
- Ferramenta de animação que renderiza (`get_motion_capabilities` só consulta estado).
- Ferramenta de cancelamento de job (`jobs.cancel` estava no rascunho antigo e não foi exposta).
- Ferramenta `prepare_host_validation` (citada em rascunhos antigos; não existe no código).
- Ferramenta `submit_edit_decisions`, `media.inventory`, `editplan.get` ou `project.list` com esses nomes: os nomes reais estão na seção 5.
- Uso do MCP pelo Desktop ou pela UI React: o Desktop usa o IPC diretamente.
- Skill de edição do produto (planejada; ver `docs/11-CLAUDE-MCP-AGENTES.md`).

## 11. Convivência com outros MCP

- O servidor se chama `travelcut` e usa nomes de ferramenta sem prefixo de produto. Não há conflito de nome com outro servidor
  desde que o registro use `travelcut` como chave.
- **Nunca sobrescrever** a entrada de outro servidor no `.mcp.json` ou na configuração global. Se já existir um MCP de DaVinci
  (ou outro) no mesmo `.mcp.json`, adicione `travelcut` como uma nova chave em `mcpServers`.
- Este repositório não contém, até a data deste documento, nenhum `.mcp.json` ativo nem entrada de MCP de DaVinci. A
  convivência acima é a regra, não um teste feito.
- O MCP não tem adaptador próprio do editor: usa o mesmo `IEditorAdapter` do Application Core. Sem
  `TRAVELCUT_EDITOR_ADAPTER=mock`, o adaptador é o DaVinci real (padrão, ADR-015). Com `mock`, o simulador (`SIMULATED`).

## 12. Limitações conhecidas

1. **Cache de idempotência em memória.** Reiniciar o servidor limpa o cache. A mesma chave repetida depois de reiniciar
   executa a ação de novo. Além disso, o cache mantém até 1000 entradas (as mais antigas concluídas saem primeiro). Para
   mutações com efeito no editor, não reutilize chaves entre sessões.
2. **`decision.submit` sem compare-and-swap de revisão.** `submit_review` não envia `baseRevision`; a checagem de versão
   prevista em `docs/05` não existe nesta versão. Duas revisões concorrentes podem sobrescrever uma à outra.
3. **Desktop e MCP no mesmo projeto: implementado, não validado no host.** A trava entre processos (seção 4.2) e o
   `Default Timeout=15` do SQLite estão no código e cobertos por testes de duas instâncias na mesma máquina. Não há
   teste com o Desktop e o MCP abertos ao mesmo tempo, nem em Unix ou unidade de rede. Esse teste fica pendente de
   homologação no host e depende de execução humana.
4. **Editor real não validado.** Sem `TRAVELCUT_EDITOR_ADAPTER=mock`, o MCP usa o adaptador DaVinci, que na VM de
   desenvolvimento responde `NOT_INSTALLED`. Mock prova só o caminho de código, não a integração (`mock_pass`, não `host_pass`).
5. **Confirmação humana fora do servidor.** `confirmProjectSwitch` é um booleano enviado pelo cliente. O servidor não
   distingue um clique do usuário de uma decisão do agente.
6. **Versão e ambiente.** O servidor se anuncia como `0.1.0`. Windows 10 Pro 22H2 da VM é ambiente experimental
   para .NET 10 (ver `CLAUDE.md`); validação final no host.
7. **Limite de tamanho.** `mediaIds` aceita até 200 itens; `notes` até 2000; `reviewer` até 100 caracteres.
8. **Autorização só por GUID.** Não há escopo por pasta ou por operação de leitura além do projeto.

## Referências

- `src/TravelCut.Mcp/Program.cs`, `src/TravelCut.Mcp/Configuration/McpServerConfiguration.cs`
- `src/TravelCut.Mcp/Tools/TravelCutReadTools.cs`, `src/TravelCut.Mcp/Tools/TravelCutMutationTools.cs`
- `src/TravelCut.Mcp/Facade/TravelCutMcpFacade.cs`, `McpOutcome.cs`, `McpIdempotencyCache.cs`
- `tests/TravelCut.Mcp.Tests/McpStdioSmokeTests.cs`
- `docs/05-CONTRATOS-E-INTEGRACOES.md` (seção MCP C#), `docs/11-CLAUDE-MCP-AGENTES.md`, `docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md`, `docs/20-HYPERFRAMES.md`
- `.mcp.json.example` (modelo na raiz)
