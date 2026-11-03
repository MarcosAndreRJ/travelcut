# Relatório de Auditoria de Legado, Ambiente e Matriz de Migração (Fase 01)

**Data de Execução:** 2026-11-01  
**Autor:** orchestrator (Claude Opus 5.5)  
**Status do Documento:** Aprovado e consolidado para execução das Fases 01 a 11  

---

## 1. Auditoria Documental e Comparação com o Kit

Foi realizada a leitura e análise de consistência integral dos 17 documentos de arquitetura e especificação preexistentes (`docs/00` a `docs/16`), além do `CLAUDE.md`, `MANIFESTO.md` e do conjunto de mockups em `referencias/mockups/` e `docs/images/`.

### 1.1 Decisões do Usuário Preservadas
- **ADR-001 / ADR-002:** Migração definitiva do ecossistema Rust/Tauri para **.NET 10 (C#) + Host WPF com WebView2 + SPA React (TypeScript / Vite / Tailwind)**.
- **ADR-003:** Núcleo de domínio (`TravelCut.Domain` e `TravelCut.Application`) 100% puro e agnóstico de UI e de NLE, garantindo testabilidade e consumo compartilhado por Desktop e Servidor MCP C#.
- **ADR-004:** Persistência relacional local via SQLite + EF Core para metadados, planos e jobs. Mídias brutas e assets nunca são alterados.
- **ADR-005:** Motores externos de alta fidelidade (FFmpeg, FFprobe, Python/Whisper large-v3 e HyperFrames) orquestrados como workers desacoplados via contratos JSON em stdio.
- **ADR-006 / ADR-008:** O TravelCut Studio gerencia a triagem, decupagem, revisão e planejamento da montagem. O DaVinci Resolve Studio é o editor final. Na VM de desenvolvimento, executa-se o `MockResolveAdapter` para testes determinísticos sem necessidade de hardware GPU ou DaVinci instalado.
- **ADR-007:** Hierarquia de inteligência operacional com Opus 5.5 (orquestrador e auditor), Sonnet 5.5 (análise técnica profunda e arquitetura) e executores especializados.

### 1.2 Resolução de Conflitos e Duplicações Identificadas
- **Abas de Análise e Navegação:** Constatou-se nos mockups `02_Biblioteca_e_Transcricao.png`, `04_Revisao_de_Cortes.png` e `06_Visao_Geral_da_Montagem.png` variações de rotulação ("Análise", "Cortes Inteligentes", "Biblioteca"). Ficou consolidado em `docs/08-INTERFACE-E-MOCKUPS.md` que a navegação será unificada em:
  1. *Início / Projeto*: Abertura e status das 4 pastas (`1-Bruto`, `2-Assets`, `3-Trabalho`, `4-Final`).
  2. *Biblioteca & Transcrição*: Grid de mídia, metadados, visualizador e transcrição vinculada.
  3. *Planejamento & Timeline*: Visualização cronológica multitrack e ordenação narrativa.
  4. *Revisão de Cortes*: Fila de decisão, antes/depois, forma de onda e motivos auditáveis.
  5. *Integração DaVinci*: Capacidades do adaptador, status da ponte e exportação do plano.
  6. *Configurações*: Diagnóstico de dependências, perfil de edição e preferências.
- **Representação Temporal:** Fica terminantemente vedado o uso de ponto flutuante (`double`) como fonte de verdade para marcações de corte ou timeline. A representação canônica será feita por frações racionais (`numerator/denominator`) ou contagem inteira de ticks vinculada a uma timebase explícita (e.g. 24000/1001, 30000/1001, 60fps).

---

## 2. Inventário do Repositório e Análise de Código Legado

### 2.1 Estado Atual da Árvore de Diretórios
- **Raiz do Projeto (`K:\Trabalho\Projetos\TravelCutStudio`):**
  - O repositório encontra-se limpo, contendo a infraestrutura de engenharia e governança (`CLAUDE.md`, `MANIFESTO.md`, `README.md`).
  - Não há código-fonte legado em Rust/Tauri na pasta local (nenhum `src-tauri`, `Cargo.toml` ou artefato binário obsoleto). O projeto parte para uma implementação limpa em .NET 10 e React sem resíduos legados de compilação.
  - A pasta `.claude/` contém os 9 perfis de agentes e as 11 Skills operacionais alinhadas à arquitetura .NET 10.
  - As pastas `referencias/mockups/` e `docs/images/` contêm os 6 mockups canônicos e 15 imagens de evolução de tela de alta resolução.
- **Classificação dos Componentes:**
  - *Documentação e Prompts:* Ativos e canônicos (`docs/`, `prompts/`).
  - *Governança e Checklist:* Ativos (`progresso/`).
  - *Código Rust/Tauri:* Inexistente no diretório de trabalho; nenhuma deleção necessária.
  - *Assets Visuais:* Preservados integralmente como guias de paridade visual.

---

## 3. Diagnóstico da VM e Toolchains Locais

### 3.1 Ambiente de Execução
- **Sistema Operacional:** Windows 10 Pro 22H2 (Build 19045), 4 vCPUs virtuais, 14,93 GB de RAM disponível.
- **Unidades de Disco:**
  - `K:\`: Volume local de alta performance dedicado ao projeto (`K:\Trabalho\Projetos\TravelCutStudio`).
  - `C:\`: Volume do sistema com espaço monitorado; não deve ser saturado com caches ou dumps temporários.
  - `Z:\`: Compartilhamento de rede HGFS com o host; não será utilizado para bancos SQLite ativos em runtime para evitar corrupção de lock de arquivo (WAL).

### 3.2 Toolchain de Compilação Auditada
- **.NET SDK:** Localizado em `C:\Program Files\dotnet\sdk\10.0.401\` (.NET 10.0.401). SDK oficial instalado e disponível.
- **Node.js / npm:** Localizado em `C:\Program Files\nodejs\`. Disponível para tooling do frontend (Vite, React, Tailwind).
- **Git:** Presente no subsistema MinGW64 (`/mingw64/bin/git`).
- **Python / Whisper / FFmpeg:** O executável Python atual aponta para o gerenciador do WindowsApps. Workers em Python utilizarão ambiente virtual dedicado ou fixtures sintéticas locais na VM, e a suíte de testes contemplará executores simulados para garantir 100% de cobertura nos testes unitários e de integração sem bloqueio por dependências externas de GPU.
- **Risco Registrado (.NET 10 no Windows 10 Pro 22H2):** Embora o Windows 10 22H2 seja um alvo experimental para o runtime .NET 10 (cujo suporte pleno foca no Windows 11 / Server), os testes de compilação e execução de projetos de classe e console são plenamente executáveis. Caso haja qualquer incompatibilidade com o host WPF nativo na VM, a arquitetura com núcleo desacoplado permite validação imediata via CLI, suíte de testes xUnit e servidor MCP.

---

## 4. Matriz de Arquitetura, Portas e Contratos

A transição arquitetural para .NET 10 adota Clean Architecture rigorosa:

| Camada | Projeto .NET / Pacote | Responsabilidade | Dependências Permitidas |
|---|---|---|---|
| **Domain** | `TravelCut.Domain` | Entidades puras (`Project`, `MediaAsset`, `Scene`, `Transcript`, `EditPlan`, `TimelineClip`), Value Objects (`TimeCode`, `RationalTimeBase`, `TimeRange`), Regras Editoriais e Invariantes | Nenhuma (Apenas .NET BCL) |
| **Application** | `TravelCut.Application` | Casos de uso (Orquestração de Inventário, Transcrição, Detecção de Cortes, Montagem), Interfaces de Portas (`IMediaRepository`, `IJobQueue`, `IEditorAdapter`, `IAudioAnalyzer`) | `TravelCut.Domain`, `TravelCut.Contracts` |
| **Contracts** | `TravelCut.Contracts` | DTOs tipados, Mensagens IPC JSON, Esquemas de Eventos, Códigos de Erro Enumerados | Nenhuma (Compartilhado com Frontend) |
| **Infrastructure** | `TravelCut.Infrastructure` | EF Core + SQLite (`TravelCutDbContext`), Sistema de Arquivos, Execução de Processos (`ProcessRunner`), Cache de Thumbnails e Waveforms | `TravelCut.Application`, `TravelCut.Domain`, EF Core, SQLite |
| **Editor Adapter** | `TravelCut.ResolveBridge` | Implementação do `IEditorAdapter` com `MockResolveAdapter` (VM) e `DaVinciResolveAdapter` (Host via Scripting/IPC) | `TravelCut.Application`, `TravelCut.Contracts` |
| **Desktop Host** | `TravelCut.Desktop` | Shell WPF, Controle WebView2, Ponte Bidirecional de Mensagens Tipadas, Inicialização de Serviços | WPF, WebView2, `TravelCut.Application`, `TravelCut.Infrastructure` |
| **MCP Server** | `TravelCut.McpServer` | Servidor MCP C# oficial expondo ferramentas de inspeção, análise e montagem sobre o mesmo núcleo de aplicação | MCP C# SDK, `TravelCut.Application` |
| **Frontend** | `TravelCut.Frontend` | SPA React 19 / TypeScript 5 / Vite / Tailwind CSS com componentes de timeline, visualizador e revisão | `TravelCut.Contracts` (via tipagem TS espelhada) |

---

## 5. Protocolo de IPC WebView2 e Segurança

A comunicação entre a SPA React e o host .NET utilizará uma ponte IPC JSON estruturada via `window.chrome.webview`:

1. **Envelope de Mensagem (Requisição):**
   ```json
   {
     "version": 1,
     "requestId": "uuid-v4",
     "type": "project.inventory.start",
     "payload": {
       "projectPath": "K:/Viagens/ExpedicaoSul"
     }
   }
   ```
2. **Envelope de Mensagem (Resposta):**
   ```json
   {
     "version": 1,
     "requestId": "uuid-v4",
     "ok": true,
     "payload": {
       "jobId": "job-uuid",
       "status": "queued"
     }
   }
   ```
3. **Eventos em Tempo Real (Broadcast do Host):**
   ```json
   {
     "version": 1,
     "type": "job.progress",
     "payload": {
       "jobId": "job-uuid",
       "progressPercentage": 45.5,
       "completedUnits": 91,
       "totalUnits": 200,
       "status": "running"
     }
   }
   ```

### 5.1 Regras Rígidas de Segurança
- A SPA jamais possui permissão para executar comandos shell arbitrários ou acessar o sistema de arquivos diretamente.
- O Host WPF valida rigorosamente todo `type` e sanitiza os argumentos, assegurando que apenas operações contidas dentro do diretório do projeto aberto sejam atendidas.
- Pastas `1-Bruto` e `2-Assets` são montadas com travas de software em nível de domínio, impedindo qualquer gravação, exclusão ou substituição de arquivos originais.

---

## 6. Governança de Agentes e Handoff para a Fase 02

- Os perfis de subagentes em `.claude/agents/` (`dotnet-engineer`, `frontend-engineer`, `media-engineer`, `davinci-integrator`, `mcp-engineer`, `qa-auditor`, `senior-analyst`, `orchestrator`, `documentation-engineer`) estão devidamente configurados e prontos.
- As Skills operacionais em `.claude/skills/` cobrem todo o ciclo de vida.
- O encerramento da Fase 01 atesta que a base de conhecimento está consolidada, o diagnóstico da VM está registrado, e o plano de implementação da Fase 02 (Estruturação da Solution .NET, projetos Core, Contracts, Desktop WPF/WebView2 e Frontend React) pode ser iniciado com total segurança e clareza de escopo.
