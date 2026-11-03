---
name: travelcut-host-validation
description: "Roteiro reprodutível para instalação e testes manuais do TravelCut com DaVinci no host."
---

# Procedimento


1. Ler `docs/10-DAVINCI-VM-HOST.md`, `docs/14-BUILD-DEPLOY-HOST.md`.
2. Gerar pacote instalável, manifesto versões/hashes, mídia sintética, passos e critérios.
3. Testes do host executados **pelo usuário**; ferramenta não presume acesso ao host nem executa em segundo plano.
4. Coletar logs e formulário em `progresso/HOST_TESTS.md`: versão Resolve/Windows, evidências, IDs, screenshots e resultado.
5. Falha real vira tarefa regressão na VM, sem reescrever projeto pessoal.
