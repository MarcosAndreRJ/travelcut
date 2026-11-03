---
name: travelcut-architecture
description: "Aplica arquitetura em camadas .NET e ADRs existentes ao implementar ou revisar módulos."
---

# Procedimento


1. Consultar `docs/03-ARQUITETURA-DOTNET.md`, `docs/05-CONTRATOS-E-INTEGRACOES.md` e `docs/15-DECISOES-ARQUITETURAIS.md` somente nas seções relevantes.
2. Domain sem WPF, Python, DaVinci e SDK MCP; Application usa interfaces; Infrastructure contém implementação; UI/MCP compartilham casos de uso.
3. Para mudança de contrato, registrar versão/migração, consumidores afetados e testes. Propor ADR para mudança de decisão aprovada.
4. Finalizar com paths, testes, blockers; não redesenhar a arquitetura silenciosamente.
