# Requisitos funcionais, não funcionais e aceite

## Requisitos funcionais

| ID | Requisito | Critério verificável |
|---|---|---|
| RF-01 | Reconhecer quatro pastas padrão | Mostra erro útil se faltar uma pasta; nunca altera originais |
| RF-02 | Inventariar vídeos/assets | Persistir tipo, codec, FPS, duração, hash parcial/forte conforme política e origem |
| RF-03 | Ordenação temporal | Exibir evidência da data: metadata, inferência ou pendente; resolver discrepâncias manualmente |
| RF-04 | Prévia e miniaturas | Exibir frame/seek real em mídia de teste; fallback claro em mídia incompatível |
| RF-05 | Transcrição por palavra | Persistir texto, início/fim em segundos e fonte do modelo; timestamps dentro da duração |
| RF-06 | Silêncios/hesitações | Mostrar segmentos e motivo; corte não é executado irreversivelmente |
| RF-07 | Classificação de cena | Tipos: conversa, paisagem, deslocamento, drone, loja/produto, outros; permitem correção |
| RF-08 | Planejamento narrativo | Plano ordenado por mídia/intervalo, motivação, transições e pendências |
| RF-09 | Revisão de sugestões na Biblioteca | Aprovar, rejeitar, ajustar intervalo e adiar dentro da Biblioteca. Cada decisão fica versionada e o resultado efetivo aparece na transcrição e na prévia |
| RF-10 | Timeline com transcrição | Faixas, segmentos, tempos, waveform e prévia consistentes. O texto segue a ordem da montagem e acompanha a agulha. Edições por fala afetam a ocorrência selecionada |
| RF-11 | Ponte DaVinci | Mock validado na VM e integração real apenas após teste no host |
| RF-12 | MCP TravelCut | Operações declaradas e idempotentes; alterações guardadas por permissões e versão |
| RF-13 | Logs e retomada | Tarefa reinicia após interrupção sem duplicar resultados |
| RF-14 | Exportação | Prévia em `3-Trabalho` e publicação em `4-Final` somente após aprovação |
| RF-15 | Perfil de edição | Preferências de silêncio, humor, zoom, B-roll, trilha e revisão de ambiguidades |
| RF-16 | Diagnóstico | DaVinci ausente/fechado/inacessível, sem suporte, simulado ou conectado efetivamente |
| RF-17 | Edição direta por fala ou In/Out na Biblioteca | Aplicar um corte manual, refletir o resultado no preview do clipe e permitir desfazer. O corte confirmado aparece na Timeline e sobrevive ao fechamento do projeto, inclusive quando é a primeira edição |

## Não funcionais

- **RNF-01 Integridade:** nenhuma escrita, exclusão ou substituição em `1-Bruto` e `2-Assets`. Única exceção (ADR-013): a importação explícita pode **criar arquivos novos** nessas pastas (`.tcpart` → SHA-256 verificado → rename sem sobrescrita); arquivos existentes nunca são alterados, movidos ou apagados.
- **RNF-02 Offline:** análise audiovisual local; sem upload para APIs externas por padrão.
- **RNF-03 Performance:** UI responsiva; jobs canceláveis, progresso baseado em unidades, cache persistente, limites de CPU/RAM ajustáveis.
- **RNF-04 Confiabilidade:** escrita atômica de plano e checkpoint; SQLite WAL local com integridade, backup de projeto antes de alterações.
- **RNF-05 Acessibilidade:** navegação por teclado, foco visível, contraste, textos ajustáveis/escala 125–200%.
- **RNF-06 Observabilidade:** logs estruturados sem segredos; código de erro, severidade, duração, relação com tarefa.
- **RNF-07 Extensibilidade:** contratos para workers/DaVinci; regras do Core independentes da UI e do editor.
- **RNF-08 Reprodutibilidade:** builds e versões de dependências registrados; mock não conta como homologação real.
- **RNF-09 Compatibilidade:** Windows 10 Pro VM é ambiente experimental; homologação de produto no host suportado, conforme matriz .NET.
- **RNF-10 Proveniência:** cada corte referencia mídia original por ID, intervalo de origem e análise/decisão.

## Fluxos principais

**Abrir projeto** → validar estrutura → indexar arquivos → comparar cache → iniciar jobs faltantes → atualizar Biblioteca. **Editar na Biblioteca** → selecionar fala, marcar In/Out ou revisar uma sugestão → aplicar a ação → conferir a prévia → desfazer ou ajustar se precisar. **Refinar na Timeline** → localizar uma fala ou um clipe → ajustar a ocorrência na montagem → conferir a revisão. **Enviar ao DaVinci** → consultar capacidades → validar a revisão e preparar o dry-run → aplicar pelo adaptador mock/real com as guardas vigentes → registrar evidências.

RF-09, RF-10 e RF-17 descrevem o alvo do [workstream de edição por transcrição](workstreams/EDICAO-POR-TRANSCRICAO.md). O workstream registra o estado de implementação e os critérios por incremento.

## Falhas que não podem ficar silenciosas

Mídia offline, codec incompatível, timestamp conflitante, Whisper indisponível, frame VFR divergente, banco bloqueado, versão da timeline alterada, ausência de DaVinci, recursos MCP ausentes, mídia de origem modificada, arquivo destino existente, falta de espaço em disco.

## Definição de pronto por módulo

Implementação + teste de casos normais/erro + estados UI + logs + docs + persistência quando relevante. Módulos DaVinci: `mock_pass` apenas na VM, `host_pending` até evidência de teste com host. Nenhuma aceitação por screenshot estático.
