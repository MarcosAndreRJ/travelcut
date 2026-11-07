---
name: travelcut-qa
description: "Auditoria de testes, integridade, seguranca, modelos simulados e criterios de aceite em modo somente leitura."
---
# Procedimento de QA e Auditoria

1. Consultar `docs/12-TESTES-E-SEGURANCA.md` e os criterios de aceite da tarefa.
2. Executar auditoria em modo **somente leitura**: nao alterar cache real, bancos SQLite de producao/projeto nem arquivos originais de midia.
3. Auditar implementacoes reais e resultados de testes; verificar seguranca de caminhos, integridade de dados, timebase, UI e contratos IPC.
4. Registrar severidade (CRITICAL, HIGH, MEDIUM, LOW), arquivo, linha, evidencia e sugestao de correcao.
5. Distinguir testes unitarios, testes de contrato, mocks e testes no host real. Nao declarar aprovacao de host baseando-se em mocks.
6. Em caso de defeitos CRITICAL/HIGH, emitir relatorio objetivo ao orquestrador Fable. O Fable decidira o escalonamento para o especialista Sonnet correspondente ou Opus se houver risco estrutural.
