---
name: travelcut-davinci
description: "Valida contratos mock/real com DaVinci Studio e prepara homologação manual no host."
---

# Procedimento


1. Ler `docs/10-DAVINCI-VM-HOST.md` e `docs/09-TIMELINE-E-REVISAO.md`.
2. Nunca instalar/abrir DaVinci na VM. Mock usa mesmas interfaces mas marca resultados `SIMULATED`; não gera `.drp` fictício.
3. Para método real, confirmar suporte na API/MCP da versão específica do host; matriz de capacidades e erros.
4. Preparar projeto temporário e mídias sintéticas no host, read-only primeiro, snapshot antes de mutações.
5. Separar `mock_pass`, `host_pending`, `host_pass`, logar evidências em `progresso/HOST_TESTS.md`; não usar dados pessoais para homologação destrutiva.
