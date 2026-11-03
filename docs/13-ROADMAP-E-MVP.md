# Roadmap, fases automáticas e definição de MVP

| Ordem | Entrega obrigatória | Dependências | Pode avançar sem host? |
|---|---|---|---|
| 00 | Bootstrap / estado / auditoria operacional | README + kit | Sim |
| 01 | Auditoria e migração Rust→.NET | repo real, inventário SO | Sim |
| 02 | Solução .NET + projetos + UI shell com bridge | SDK/build compatíveis | Sim (se VM compilável) |
| 03 | Mockups React navegáveis e testes UI | contratos tipados | Sim |
| 04 | Ingestão, ffprobe e cache | fixtures e ffmpeg disponível | Sim / bloqueio específico |
| 05 | Whisper, áudio, cenas, candidatos | worker válido ou testes sintéticos | Parcial: validação large-v3 pode ser no host |
| 06 | Planejamento e sugestões | análise estruturada | Sim |
| 07 | Revisão e timeline planejada | editplan, player, assets | Sim |
| 08 | Bridge DaVinci real/mocks | contratos e host | Mock sim; homologação host não |
| 09 | MCP/Claude e HyperFrames opcional | Application use-cases | Sim, exceto integrações reais |
| 10 | QA, instalador, métricas, docs | módulos integrados | Sim para build, host depois |
| 11 | MVP e homologação | artefato e roteiro | **Não**, exige teste manual host |

## Estratégia de continuidade

Ao terminar cada fase com critérios atendidos, **iniciar a próxima durante a sessão ativa**. Não prosseguir se depende de instalação/credencial/homologação de outra máquina sem autorização. Checkpoints permitem retomar noutra sessão sem perder decisões; nenhuma automação de background implícita.

## Sequência mínima de valor

MVP A: escolher projeto, inventário/cronologia, cache, transcrição ou fixture, detectar silêncios e revisão. MVP B: plano de edição e timeline visual, mock DaVinci. MVP C: integração DaVinci real no host e primeira timeline nativa editável. Não chamar MVP C concluído sem host pass.

## Critérios de aceitação final

- 4 pastas reconhecidas; originais intactos.
- Primeiro fluxo completo desde mídia até edição/revisão, com dados reais e testes reproduzíveis.
- Cortes aprovados e negativos preservados, progresso e relatório coerentes.
- DaVinci real cria timeline editável com ao menos dois arquivos e respectivos trechos no host; projeto reabre e mídia reconecta.
- Cliente abre no host com resolução e escalas válidas; não depende de rustup/Cargo ou ferramentas de dev para funcionar.
- Instalador/README e procedimentos de atualização sem sobrescrever projetos.

## Monitoramento

`progresso/CHECKLIST.md` lista todas as fases; `progresso/IMPLEMENTATION_LOG.md` tem entradas após cada microresultado; `STATE.md` é ponto de retomada. Adicionar ADR para alterações de arquitetura aprovadas; não reabrir decisões por preferência do subagente.
