# Índice técnico — TravelCut Studio

| Documento | Finalidade |
|---|---|
| [01 — Visão e escopo](01-VISAO-E-ESCOPO.md) | Produto, usuários, escopo, não objetivos, MVP |
| [02 — Requisitos e aceitação](02-REQUISITOS-E-ACEITE.md) | RF, RNF, fluxos e critérios observáveis |
| [03 — Arquitetura .NET](03-ARQUITETURA-DOTNET.md) | Camadas, módulos, comunicação, dependências |
| [04 — Modelo de dados](04-MODELO-DE-DADOS.md) | Entidades, SQLite, migrações, IDs, hashes |
| [05 — Contratos e integração](05-CONTRATOS-E-INTEGRACOES.md) | C# ⇄ frontend/Python/DaVinci/MCP |
| [06 — Pipeline de mídia](06-PIPELINE-AUDIOVISUAL.md) | Inventário, transcrição, cena, cache, cronologia |
| [07 — Regras editoriais](07-REGRAS-EDITORIAIS.md) | Viagens, cortes, humor, zooms, silêncio, cor, áudio |
| [08 — UI e mockups](08-INTERFACE-E-MOCKUPS.md) | Design system, telas, estados, layout |
| [09 — Timeline e revisão](09-TIMELINE-E-REVISAO.md) | EDL, aprovação, timecode, preview, estados |
| [10 — DaVinci VM/host](10-DAVINCI-VM-HOST.md) | Adaptadores, homologação, capacitações |
| [11 — MCP/Claude](11-CLAUDE-MCP-AGENTES.md) | Operações, hierarquia, custos, autorizações |
| [12 — QA e segurança](12-TESTES-E-SEGURANCA.md) | Testes, erros, invariantes, auditabilidade |
| [13 — Execução e entrega](13-ROADMAP-E-MVP.md) | Sequência de fases, gates, resultados |
| [14 — Release/Host](14-BUILD-DEPLOY-HOST.md) | Instaladores, recursos offline, evidências |
| [15 — ADRs](15-DECISOES-ARQUITETURAIS.md) | Decisões, trade-offs, pendências |
| [16 — Guia de continuidade](16-OPERACAO-E-CHECKPOINTS.md) | Checklist, logs após cada alteração, retomada |
| [17 — Auditoria de legado e migração](17-AUDITORIA-LEGADO-E-MIGRACAO.md) | Auditoria inicial do repositório, ambiente da VM e matriz de migração (F01) |
| [18 — Roteiro de homologação DaVinci](18-ROTEIRO-HOMOLOGACAO-DAVINCI.md) | Teste no host do bridge DaVinci (`host_pending`) |
| [19 — Servidor MCP TravelCut](19-MCP-TRAVELCUT.md) | Setup do MCP stdio, ferramentas, guardas, roteiro e limitações |
| [20 — HyperFrames](20-HYPERFRAMES.md) | Roteiro de homologação de animações no host (`host_pending`) |
| [21 — Notas de versão](21-NOTAS-DE-VERSAO.md) | Versão 0.10.0-host-test.1: novidades, mudanças de comportamento, hashes do pacote e o que é simulado |
| [22 — Smoke test no host](22-SMOKE-TEST-HOST.md) | Checklist curto de 22 itens para o pacote de teste no host, com onde anotar cada evidência |
| [23 — Limitações e riscos](23-LIMITACOES-E-RISCOS.md) | Limitações e riscos consolidados, com estado (`mock_pass`, `host_pending`, `aberto`) e fonte |

## Precedência

1. Instruções explícitas do usuário e políticas de segurança.
2. `CLAUDE.md` + este kit de documentação.
3. Decisões aceitas registradas em ADR.
4. Prompts por fase (escopo operacional da execução).
5. Propostas dos subagentes (não substituem decisões aprovadas).

## Legenda de validação

- **IMPLEMENTADO:** código existe e teste automatizado passou.
- **SIMULADO:** opera contra mock/fixture; não implica sistema externo funcionando.
- **HOST_PENDENTE:** exige computador com DaVinci/GPUs adequadas.
- **HOST_VALIDADO:** evidência coletada no host, com versão e resultado documentados.
- **BLOQUEADO:** problema concreto documentado, sem alegar conclusão.
