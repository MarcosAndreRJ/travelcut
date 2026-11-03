---
name: travelcut-mcp
description: "Cria ferramentas locais MCP do TravelCut usando SDK C# oficial e Application Core."
---

# Procedimento


1. Consultar `docs/11-CLAUDE-MCP-AGENTES.md` e contratos oficiais: https://github.com/modelcontextprotocol/csharp-sdk.
2. Ferramentas pequenas, tipadas, versionadas, idempotentes; `list/get` read-only primeiro; mutação com baseRevision, checks e logs.
3. Compartilhar Application Core com UI, não duplicar negócio; não expor exec shell genérico.
4. Tratar DaVinci ausente de forma explícita; servidor stdio local com startup e encerramento previsíveis.
5. Tests para entradas inválidas, permissões, cancelamento, status, retorno JSON e mock.
