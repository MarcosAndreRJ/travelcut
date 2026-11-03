# Operação sequencial, registros após cada ação e retomada

## Arquivos de controle

- `progresso/CHECKLIST.md`: itens por fase e microtarefas; somente marcar `[x]` quando existir evidência.
- `progresso/IMPLEMENTATION_LOG.md`: **append-only**, cronológico; gravar **assim que** uma tarefa for concluída/testada ou uma falha ocorrer, antes da próxima tarefa.
- `progresso/STATE.md`: fase ativa, tentativas, status, commit, último evento, próximo passo, bloqueios e evidências.
- `progresso/HOST_TESTS.md`: resultados dos testes manuais no host e pendências.

## Protocolo do orquestrador (Opus)

```text
LER FASE -> DEFINIR MICROTAREFAS (checklist) -> DELEGAR (Haiku/Sonnet)
-> VALIDAR OUTPUT -> REGISTRAR LOG IMEDIATAMENTE -> ATUALIZAR CHECKLIST
-> AUDITAR QA -> CORRIGIR -> GATE -> CHECKPOINT -> PRÓXIMA FASE
```

## Formato obrigatório do log

```text
[2026-10-31 11:00:00 -03:00] FASE-02 | dotnet-engineer | FEITO
Ação: contrato de inventário adicionado
Arquivos: src/.../IInventoryService.cs
Validação: dotnet test ... — aprovado (5 testes)
Observação: nenhuma alteração em 1-Bruto
Próximo: criar adaptador SQLite
```

A hora do exemplo é ilustrativa; usar o relógio real de execução. Nunca inserir entradas de sucesso por intenção; uma falha deve gerar registro `FALHOU/BLOQUEADO` com evidência. Logs devem explicar **fez / escreveu / verificou**. Atualizar de modo incremental, não no fim da fase.

## Retomada após interrupção

Ao reabrir Claude Code: ler `STATE.md`, últimas 20 entradas do log, checklist e git status. Verificar arquivos reais; tarefas concluídas sem log podem ser reconciliadas apenas com evidência. Não reexecutar export/instalação/alteração no DaVinci sem conferir idempotência e projeto aberto.

## Gates para prosseguir sozinho

Se fase atual tem tarefas entregues, testes pertinentes aprovados, QA sem `critical/high`, docs/log/checklist atualizados → `done`, iniciar próximo prompt **na mesma sessão** sem mensagem de confirmação. Caso contrário, corrigir ou marcar bloqueio; não avançar ao dependente, mas pode fazer tarefa independente segura. Nunca alegar execução assíncrona.

## Risco de espaço e recursos VM

Não usar caminhos fixos C:\\ para cache/compilação pesada sem inspeção. Preferir K: local; Z: HGFS para transferência, não banco vivo. Limitar tasks paralelas, evitar model weights na VM. Não instalar ferramentas globalmente sem consentimento quando necessário.
