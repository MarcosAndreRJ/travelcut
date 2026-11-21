# Timeline planejada, revisão e sincronização

## Princípio

TravelCut mantém um **EditPlan versionado**. No [fluxo alvo](workstreams/EDICAO-POR-TRANSCRICAO.md), a Biblioteca edita intervalos por gravação e a Timeline refina as ocorrências na ordem da montagem. DaVinci tem uma timeline própria e continua sendo o editor da finalização.

## EditPlan como referência da montagem

- Conjunto ordenado de faixas, clipes referenciados, sourceIn/out, position, velocidade, transição, propriedades visuais suportadas, motivo e decisionId.
- Plano possui revisionId, baseline, fingerprint do conjunto de mídias e schemaVersion; aplicações reais armazenam mapeamentos externos (ClipID↔ResolveItem) quando API permitir.
- As transformações devem ser **reversíveis** e não destrutivas; nunca transformar fontes originais para efetivar cortes.

## Edições manuais e revisão de sugestões

Fluxo `proposed` → `approved|rejected|deferred|modified`; cada mudança registra usuário/agent, timecodes de origem, razão, momento e baseRevision. Alterar trim gera nova decisão, não sobrescreve silenciosamente histórico.

O fluxo acima vale para sugestões. Selecionar uma fala ou um intervalo In/Out e mandar cortar é uma ação direta do usuário. O plano confirma o trecho efetivamente removido e permite reverter a operação. Durante a migração, preservar decisões antigas e o [overlay de edições manuais](adr/ADR-overlay-edicoes-manuais.md).

## Transcrição na Timeline

- Intersectar os tempos das palavras na fonte com os intervalos mantidos de cada ocorrência. Converter esse resultado para frames da revisão visualizada com a base de tempo e a velocidade do clipe. Ordenar pelo tempo de montagem.
- Sincronizar texto → playhead e playhead → texto. Trechos sem fala, lacunas e mídias ainda não transcritas mantêm seu tempo na montagem com estado claro.
- Uma mesma fala duplicada aparece em cada posição em que é reproduzida. A edição pela Timeline guarda a identidade da ocorrência selecionada. Um intervalo sem correspondência clara após mudança de revisão exige recarregar ou reconciliar.

## Playback e preview

- Para prévia simples, testar bibliotecas como LibVLCSharp ou player WebView2 para codecs e seeking real; suporte a frame exato e render GPU devem ser medidos, não presumidos.
- A comparação Original / Editado usa a fonte ou o proxy e os trechos mantidos da revisão de referência. A interface indica efeitos com prévia aproximada ou indisponível.
- Waveform derivada de áudio decodificado, com offsets rigorosamente alinhados ao mesmo timeline clock; buscar no player por timestamp de fonte, não por índice de palavra.
- Prévia deve contemplar trims com margem/contexto para não cortar fonemas; não tocar música de terceiros sem direitos de uso.

## Conflitos de sincronização

Antes de aplicar no DaVinci: consultar projeto aberto/capacidades, revision/fingerprint quando possível, criar backup/snapshot seguro, executar dry-run e plano idempotente. Se edição manual no Resolve alterou baseline → `PROJECT_VERSION_CONFLICT` e exigir reconciliação. Nunca sobrescrever edição manual do usuário automaticamente.

## Métricas

Contadores de propostas e decisões vêm do estado efetivo de sugestões. O tempo de origem removido e o tempo final vêm do plano. `estimated` sempre rotulado e calculado com política conhecida.

## Validações por clip

Referência de mídia existe; intervalo `[in,out)` não vazio; source media não mudou; posição não negativa; tracks válidas; transição não ultrapassa limites; velocidade racional positiva; áudio associado continua sincronizado. Testes por propriedade para conversão de timebase e cenários VFR.
