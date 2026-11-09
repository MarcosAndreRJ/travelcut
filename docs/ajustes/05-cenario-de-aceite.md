# Prompt 05 — Cenário de aceite (Checkpoints G7, J1, K)

Executor: Sonnet B (fixtures de mídia sintética com FFmpeg, sem tocar originais) + Haiku `qa-auditor` (somente leitura) para conferir.

Criar teste de integração executável: importar → transcrever (ou cache válido) → detectar silêncio → gerar corte → aplicar automaticamente → plano atualizado → preview não reproduz o trecho removido → desfazer (trecho volta) → refazer (removido de novo) → fechar/reabrir (persistência). Repetir para zoom e para transição suportada.

Cenários J1:
1. Silêncio evidente.
2. Repetição com significado ("Não, não foi isso") → preservada ou enviada a Review.
3. Múltiplas mídias, cache reaproveitado, sem jobs duplicados.
4. Zoom automático em fala direta à câmera.
5. Reprocessamento sem duplicar decisões nem destruir edições manuais.
6. Falha parcial em uma mídia.
7. Persistência, com originais intactos (comparar hash antes e depois).

Registrar métricas reais por fixture: candidatos, aplicados automaticamente, pendentes, restaurados, tempo removido, falas preservadas, efeitos, conflitos. Sem taxa-alvo artificial.

Pronto quando: testes verdes e métricas no doc F14.8; marcar `integration_pass` (não `host_pass`).
