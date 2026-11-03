# FASE 03 — Implementação visual de seis telas baseadas nos mockups


**Objetivo:** reproduzir, de modo consistente e navegável, os seis mockups em React/TypeScript renderizado pelo WPF/WebView2, **sem fingir integração pronta**.

### Atividades

1. Inspecionar todas as imagens em `referencias/mockups/`; consolidar tokens, sidebar, tipografia, componentes, estados e temas conforme `docs/08`.
2. Implementar telas: Início, Biblioteca+Transcrição, Análise/Visão da Montagem, Planejamento/Timeline, Revisão de Cortes, Configurações/Integrações. Acrescentar áreas Exportação e DaVinci com empty states coerentes se ainda não houver backend.
3. Implementar visual de player, thumbnails, waveforms, fila de revisão e timeline planejada usando fixtures TS **sinalizadas como DEMO**; botões sem funcionalidade ficam desabilitados ou demonstrativos.
4. Garantir navegação, keyboard focus, estados zero/erro/carregamento, escalas do Windows; screenshots comparáveis nas seis telas, não apenas painel inicial.
5. Passar contratos de dados versionados pelo `TravelCut.Contracts` e não acoplar estado de frontend à tecnologia da bridge.

### Aceite

Seis telas reais navegáveis, testes de componente e build TS/WPF, revisão de mockups, nenhum badge "conectado" fake em produção, QA de UI e screenshots documentadas.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/04-INVENTARIO-E-MIDIA.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
