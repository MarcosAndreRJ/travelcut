# Estratégia de QA, segurança, desempenho e gates

## Pirâmide de testes

- **Unitário (xUnit):** value objects timebase, intervalos, paths, flags de preservação, máquinas de estado, cálculo de progresso.
- **Contratos:** JSON C# ⇄ TS ⇄ Python; versionamento; nomes/erros; codecs e fixtures de PTS.
- **Integração local:** SQLite/migração, fila/cancelamento/retomada, FFprobe sintético, captura de erro de processo, cache.
- **UI:** testes de componentes, navegação, estados vazios, carregamento, erros, comparação screenshots, teclado.
- **DaVinci mock:** sequência/contagens de chamadas, idempotência, erro em operação, capability indisponível, conflito de revisão.
- **Host:** execução real das APIs/áudio/timeline/exports, com evidência manual.

## Fixtures e casos críticos

Clipes pequenos sintéticos com FPS 23.976/25/29.97/60, um VFR, rotação de celular, clipe silencioso, com fala em PT de teste devidamente autorizada, clipe sem stream de áudio, caminho com espaço/Unicode e metadados inconsistentes. Não armazenar vídeos pessoais no repositório.

## Regras de segurança

- Somente leitura em `1-Bruto` e `2-Assets` implementada nas operações do aplicativo, não apenas na UI.
- Nunca usar path relativo fornecido por mídia para sair da raiz autorizada (canonicalize, symlink/junction, traversal).
- Subprocessos com executáveis explicitamente resolvidos e ArgumentList, timeout, sem interpolar em shell.
- WebView2 deve carregar conteúdo empacotado/trusted origin; validar mensagens, CSP quando aplicável, sem abrir navegação arbitrária.
- Dados sensíveis e credenciais não entram em logs; secrets por mecanismos seguros do Windows, quando necessários.
- SQLite local com backup/migração; não usar HGFS para banco ativo sem testes específicos.
- Render e publishing não sobrescrevem a pasta Final sem política/versionamento; temp em Trabalho; checar espaço antes de render.

## Gates por fase

Cada fase só termina com: outputs existentes, checklist coerente, log real, build/test pertinentes, análise do `qa-auditor`, correções de bloqueios, revisão Opus e `STATE.md` atualizado. Erro impeditivo → `blocked`, não marcar done. Testes do host são gate específico quando necessário; eles não podem ser substituídos por mocks.

## Matriz de severidade

`CRITICAL`: escrita em original, perda de projeto, vazamento/execução não autorizada. `HIGH`: corrupção de timeline, erro de timebase, crash frequente, dados fictícios como reais. `MEDIUM`: regressão funcional com workaround, UI quebrada em uma tela. `LOW`: desalinhamento visual, documentação incompleta sem risco técnico. Critical/High bloqueiam continuidade dependente.

## Auditoria pós-fase

Executar formatação, build, testes, lint TS, diff review, verificação do log/checklist, git status (sem mudanças geradas inadvertidamente), revisão de caminhos; registrar testes não feitos e motivo. Não afirmar que todos passaram quando algum não foi executado.
