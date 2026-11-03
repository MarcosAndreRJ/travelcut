# Timeline planejada, revisão e sincronização

## Princípio

TravelCut mantém um **EditPlan versionado** e uma timeline de visualização/revisão. DaVinci tem uma timeline própria, editável, que pode ser modificada manualmente. Nunca presumir sincronização só porque ambas exibem o mesmo nome.

## EditPlan como fonte de verdade da proposta

- Conjunto ordenado de faixas, clipes referenciados, sourceIn/out, position, velocidade, transição, propriedades visuais suportadas, motivo e decisionId.
- Plano possui revisionId, baseline, fingerprint do conjunto de mídias e schemaVersion; aplicações reais armazenam mapeamentos externos (ClipID↔ResolveItem) quando API permitir.
- As transformações devem ser **reversíveis** e não destrutivas; nunca transformar fontes originais para efetivar cortes.

## Aprovação de cortes

Fluxo `proposed` → `approved|rejected|deferred|modified`; cada mudança registra usuário/agent, timecodes de origem, razão, momento e baseRevision. Alterar trim gera nova decisão, não sobrescreve silenciosamente histórico.

## Playback e preview

- Para prévia simples, testar bibliotecas como LibVLCSharp ou player WebView2 para codecs e seeking real; suporte a frame exato e render GPU devem ser medidos, não presumidos.
- Comparação antes/depois pode usar proxies geradas por FFmpeg; indicar quando prévia não representa todos os efeitos da timeline definitiva.
- Waveform derivada de áudio decodificado, com offsets rigorosamente alinhados ao mesmo timeline clock; buscar no player por timestamp de fonte, não por índice de palavra.
- Prévia deve contemplar trims com margem/contexto para não cortar fonemas; não tocar música de terceiros sem direitos de uso.

## Conflitos de sincronização

Antes de aplicar no DaVinci: consultar projeto aberto/capacidades, revision/fingerprint quando possível, criar backup/snapshot seguro, executar dry-run e plano idempotente. Se edição manual no Resolve alterou baseline → `PROJECT_VERSION_CONFLICT` e exigir reconciliação. Nunca sobrescrever edição manual do usuário automaticamente.

## Métricas

Contador de sugestões, aprovados, pendentes, tempo de origem removido e tempo final são derivados de decisões reais; nunca confundir duração total de takes com duração final (aceleração altera duração). `estimated` sempre rotulado e calculado com política conhecida.

## Validações por clip

Referência de mídia existe; intervalo `[in,out)` não vazio; source media não mudou; posição não negativa; tracks válidas; transição não ultrapassa limites; velocidade racional positiva; áudio associado continua sincronizado. Testes por propriedade para conversão de timebase e cenários VFR.
