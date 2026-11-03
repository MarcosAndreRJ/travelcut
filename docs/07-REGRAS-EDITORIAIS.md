# Regras editoriais — perfil Viagens

## Prioridades

1. Integridade do acontecimento, sentido das falas e cronologia verdadeira.
2. Preservação do humor, brincadeiras, expressões e interações espontâneas (inclusive Elaine).
3. Fluidez/ritmo: remover hesitações, recomeços e longas pausas sem produzir cortes robóticos.
4. Visual agradável: B-roll conciso, velocidade adequada, zoom deliberado, trilhas e transições discretas.

## Categorias de sugestão

| Categoria | Decisão padrão | Revisão obrigatória quando |
|---|---|---|
| Silêncio longo | sugerir reduzir/remover | som ambiente expressivo, preparação de piada, paisagem contemplativa |
| "ééé", "humm" | sugerir cortar | corte soa artificial, perde sílaba/entonação |
| Repetição de tomada falada | manter tentativa corrigida | primeira tentativa contém humor/reação ou diferença semântica |
| Trecho de estrada | sugerir acelerar 2×/4× ou reduzir | há diálogo, câmera balança, velocidade prejudica percepção |
| Drone/paisagem | manter velocidade natural quando merecida | sugestão de corte elimina paisagem importante |
| Cenas fixas com pessoas | sugerir zoom sutil | corta rostos, perde resolução, não há motivação |
| Música | sugerir asset adequado | licenciamento desconhecido ou diálogo encoberto |
| Transição | corte direto padrão; fade/dissolve contextual | gera continuidade falsa, alonga desnecessariamente |
| Correção de cor | normalizar exposição/WB por perfil/cena | LUT aplicada sem conhecer HDR/Log ou tons de pele comprometidos |
| Vento/legenda | marcar para revisão pós-primeira edição | Whisper incerto, risco de legenda inventada |

## Esquema mínimo de sugestão

`id`, `mediaId`, `sourceInterval`, `kind`, `rationale`, `evidenceRefs`, `confidenceBand` (`low|medium|high`, **heurística não calibrada**), `needsHumanReview`, `suggestedAction`, `state` (`proposed|approved|rejected|deferred|modified`), `author` (tool/model/manual), `modelVersion`, `createdAt`.

## Regras de corte

- Intervalos de origem não podem ultrapassar duração real, nem sobrepor de modo indevido áudio/vídeo vinculados.
- Corte agressivo em conversa exige reprodução de contexto antes/depois; manter pausas que definem ritmo ou humor.
- Tentar destacar uma piada com efeitos curtos, sem meme aleatório e sem extrapolar o conteúdo que a pessoa disse.
- Aprovação registrada por ID e versão, não por tempo de timeline isolado (que pode mudar após novos cortes).
- Nunca usar IA generativa para criar falas que a pessoa não disse.

## Política para primeira edição

Permitir automaticamente apenas mudanças triviais de alta evidência em **plano reversível**; demais pendentes aparecem no painel de revisão. Correção avançada de vento e legendas quando o áudio estiver incompreensível podem ser registradas como revisão futura. Toda automação deve manter o projeto DaVinci editável e originais intactos.

## Perfil persistente

Preferências `silencePolicy`, `pace`, `jokePreservation`, `zoomStrength`, `speedBroll`, `musicPolicy`, `captionPolicy`, `colorPolicy`, `reviewAmbiguous`. Padrões conservadores; salvar versão do perfil em cada análise para reprodutibilidade. Regras críticas (integridade e origem) não podem ser desligadas por perfil.
