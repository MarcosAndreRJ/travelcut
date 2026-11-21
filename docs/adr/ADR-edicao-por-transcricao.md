# ADR — Edição por transcrição na Biblioteca e na Timeline

Estado: direção de produto definida, implementação pendente. Ver [workstream e critérios de aceite](../workstreams/EDICAO-POR-TRANSCRICAO.md).

## Contexto

Na implementação de referência, excluir palavras na Biblioteca registra uma sugestão pendente. O painel guarda a marcação e o desfazer localmente, enquanto a aplicação no plano passa pela Revisão. A Timeline já tem revisões, edição manual e conversão entre tempo da fonte e tempo da montagem.

## Decisão

1. **Transcrição da fonte.** O texto e os timestamps da análise permanecem associados à mídia. Uma edição referencia o intervalo na fonte. Reanalisar uma transcrição pode alterar a divisão das palavras. Uma ação já confirmada continua vinculada ao intervalo de mídia original.
2. **Plano versionado.** O `EditPlan` efetivo descreve o que é reproduzido na montagem. Biblioteca, prévia e Timeline derivam suas projeções desse estado. Reaproveitar a [reconciliação das edições manuais](ADR-overlay-edicoes-manuais.md), com a precedência documentada e os mesmos controles de integridade.
3. **Intenção explícita.** As sugestões automáticas têm estado de revisão. Cortar por seleção ou In/Out é uma ação manual imediata. Quando a edição parte da Timeline, o alvo inclui a ocorrência. A operação deve sobreviver a reabrir o projeto e recusar uma revisão-base obsoleta quando houver risco de afetar o alvo errado.
4. **Falha recuperável.** Validar antes de publicar uma revisão. Se a decisão for gravada e a revisão ficar protegida ou a atualização do plano falhar, expor o estado parcial e o caminho de recuperação. Confirmações de aplicação usam o resultado efetivo. Repetir uma operação interrompida deve convergir para um único resultado.
5. **Compatibilidade.** Manter revisões, decisões e sugestões anteriores. Itens pendentes seguem pendentes. Mudanças no formato persistido devem ser aditivas quando possível e ter cobertura com dados anteriores, incluindo reabertura e regeneração. Originais e transcrição bruta permanecem preservados.

## Alternativas consideradas

- Persistir as marcações do editor de texto de forma independente. Isso exigiria reconciliar dois estados editoriais a cada mudança de tela, regeneração ou reabertura.
- Encadear a criação de uma sugestão e sua aprovação no frontend. Cada etapa teria sua própria falha e repetição, inclusive no primeiro corte de um projeto sem plano.

## Consequências

A primeira entrega precisa definir como a edição inicial materializa o plano de trabalho e como um corte confirmado é desfeito. A edição de ocorrências na Timeline precisa de identidade e reconciliação adequadas a clipes divididos, duplicados e reordenados. A prévia do WebView2 usa o plano e os proxies disponíveis. Fidelidade de reprodução e capacidades de exportação são verificadas conforme o ambiente.
