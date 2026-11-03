---
name: travelcut-qa
description: "Auditoria de testes, integridade, segurança, modelos simulados e critérios de aceite."
---

# Procedimento


1. Ler `docs/12-TESTES-E-SEGURANCA.md` e critérios da fase.
2. Auditar implementações reais e resultados do teste, não apenas descrições; verificar bugs críticos de path, dados, timebase, UI, contratos e worker.
3. Marcar severidade, arquivo, evidência, reprodução e sugestão de correção; evitar falso positivo sem contexto.
4. Distinguir unit/contract/mock/host e `not_run`. Não declarar host aprovado com simulação.
5. Bloquear fase para CRITICAL/HIGH relevantes; QA devolve relatório, Opus resolve/atualiza log.
