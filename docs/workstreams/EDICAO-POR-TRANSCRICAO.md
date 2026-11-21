# Edição por transcrição

Estado: **planejado**. Este documento descreve a implementação a fazer na VM de desenvolvimento. A [ADR](../adr/ADR-edicao-por-transcricao.md) registra o modelo compartilhado entre as telas.

## Fluxo de trabalho

1. Abrir um clipe na **Biblioteca & Transcrição**. Clicar em uma fala posiciona o preview. Selecionar palavras ou marcar In/Out define o intervalo a editar.
2. Executar **Cortar trecho**. O corte confirmado pelo host aparece na transcrição e no modo **Editado** do preview, com **Desfazer** disponível na confirmação. O modo **Original** usa o mesmo contexto da fonte para comparar.
3. Conferir sugestões automáticas no painel lateral da Biblioteca. O painel permite ouvir antes/depois, ajustar limites, aprovar, rejeitar, adiar e restaurar um trecho cortado por sugestão. A análise pode continuar sugerindo silêncios e hesitações conforme as regras vigentes.
4. Abrir a **Timeline** e encontrar a fala pela busca ou pela agulha. O painel acompanha a ordem da montagem e permite refinar a ocorrência selecionada.

## Incremento 1 — Corte direto e prévia na Biblioteca

Usar o mesmo caso de uso de corte para seleção de palavras e marcação In/Out. Aproveitar `TranscriptPanel`, o player da Biblioteca e a lógica de segmentos mantidos da Revisão. O resultado confirmado atualiza a revisão de trabalho, as marcações de texto e o preview. Usar fonte ou proxy já disponível para pular o intervalo cortado. O primeiro corte precisa funcionar em um projeto sem revisão, com o mesmo planejamento e as mesmas validações do projeto.

**Aceite**

- Selecionar uma palavra e cortar. A interface confirma a edição apenas quando o intervalo está aplicado. Reproduzir pelo ponto de corte no modo **Editado** pula o intervalo. Em **Original**, o trecho fica disponível para comparação.
- Marcar In/Out em um clipe sem transcrição e cortar pelo mesmo fluxo.
- Abrir a Timeline, fechar e reabrir o projeto. O corte continua presente. Desfazer e refazer atualizam o plano, o texto e a prévia. Os originais mantêm o hash.
- Cobrir o primeiro corte sem plano, repetição da requisição, erro de gravação, revisão base obsoleta e rejeição por plano protegido. A interface deve mostrar o resultado real de cada caso.

## Incremento 2 — Sugestões e efeitos na Biblioteca

Migrar as funções da Revisão para o painel lateral. Incluir fila por clipe, acesso às sugestões do projeto, motivo, confiança qualitativa, estados, antes/depois, ações por item e as ações em lote já suportadas. Preservar decisões anteriores e o perfil de automação. Redirecionar as entradas da T03 para o contexto correspondente e retirar a tela separada após verificar essa paridade.

Começar os efeitos manuais pelo zoom por fala com escala, foco e duração tipados no host. Na base de referência, o botão de zoom cria uma sugestão de corte e descreve o zoom na justificativa. Cobrir o caminho real e a reversão do efeito, com indicação de fidelidade da prévia quando aplicável.

**Aceite**

- Em projeto existente, sugestões pendentes continuam disponíveis e as decisões anteriores mantêm efeito e histórico.
- Aprovar, rejeitar, adiar, ajustar limites e desfazer decisões pelo painel da Biblioteca. Restaurar um corte aplicado por sugestão. A seleção posiciona texto e player no mesmo intervalo.
- Aplicar zoom a uma fala e verificar a propriedade efetiva no plano e no preview. Desfazer o zoom preserva os cortes existentes.
- Acionar os antigos atalhos e links de revisão e chegar à sugestão ou ao clipe na Biblioteca.

## Incremento 3 — Transcrição sincronizada e editável na Timeline

Projetar as palavras da fonte sobre os intervalos mantidos de cada ocorrência. Usar as conversões de tempo já existentes em `programPlayback.ts`. Primeiro entregar navegação e busca na montagem. Depois habilitar cortes e restauração por fala, com o mesmo modelo de revisão e tratamento de conflito.

**Aceite**

- Clicar numa fala move a agulha. Avançar, buscar ou reproduzir na Timeline destaca a fala correspondente. Clipes reordenados aparecem na ordem da montagem.
- Clipes aparados, divididos ou com velocidade alterada mantêm o mapeamento. Trechos sem fala e lacunas mantêm seu tempo. Se um mesmo trecho aparece duas vezes, cortar em uma ocorrência preserva a outra.
- Alterar um corte feito na Biblioteca e conferir o resultado efetivo nas duas telas. A Timeline usa a revisão selecionada para leitura e a revisão-base confirmada para gravação.

## Melhorias após o fluxo principal

- **Ouvir a emenda** com contexto curto antes e depois do corte.
- Busca de falas em todos os clipes, com salto para a mídia e o tempo de origem.
- Marcação **Manter** para falas e momentos que a automação deve preservar. Termos repetidos, pausas de ritmo e reações continuam contextualizados pela [política editorial](../07-REGRAS-EDITORIAIS.md).
- **Restaurar este trecho** a partir da transcrição original mesmo após outras edições, respeitando a revisão vigente e a ocorrência selecionada.

## Implementação assistida e validação

- `frontend-engineer`: seleção, estados, foco, atalhos e sincronização entre texto, preview e Timeline.
- `dotnet-engineer`: operação editorial, DTOs IPC, revisão-base, idempotência, persistência e reconciliação.
- `media-engineer`: reprodução de intervalos, proxy e qualidade da emenda no WebView2.
- `qa-auditor`: aceite integrado, compatibilidade com projetos existentes e evidências por ambiente.

As skills existentes de frontend, .NET, mídia e QA dão suporte a esse trabalho. Uma skill de edição por transcrição pode consolidar o procedimento transversal quando houver um fluxo real para validar.

Na VM, cobrir a operação e o histórico em .NET, o contrato IPC e a atualização do texto/preview em Vitest. No host Windows, repetir o corte em mídia real, incluindo HEVC via proxy e VFR quando disponíveis, e registrar pacote, mídia de teste e resultado em [HOST_TESTS.md](../../progresso/HOST_TESTS.md). Conferir fidelidade de seek e de emenda no player real. A homologação do DaVinci segue o roteiro vigente quando a entrega envolver o envio ao editor.
