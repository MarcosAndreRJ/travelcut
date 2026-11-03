# Modelo de dados e persistência

## Entidades e chaves

| Entidade | Identidade e atributos críticos | Relacionamentos |
|---|---|---|
| Project | `ProjectId` UUID, rootPath, status, profileVersion | mídias, jobs, planos, exportações |
| MediaAsset | `MediaId` UUID, relativePath, byteLength, lastWriteUtc, fingerprint, kind | metadata, scene, transcript |
| MediaMetadata | codec, width, height, timebase, avg/r fps, duration, creationTime + source | 1:1 MediaAsset |
| Scene | SceneId, source interval, class, reason, confidenceLabel | mídia, sugestão |
| Transcript | TranscriptId, modelId, language, version, audioDuration | MediaAsset e palavras |
| TranscriptWord | Seq, startSeconds, endSeconds, text | Transcript |
| SegmentCandidate | SegmentId, kind, sourceInterval, evidence, reviewerRequired | MediaAsset e suggestion |
| EditSuggestion | SuggestionId, operation, rationale, inputIds, confidenceBand | EditDecision |
| EditDecision | DecisionId, action, reviewer, timestamp, version | EditPlan |
| EditPlan | PlanId, revision, createdAt, baseProjectVersion | Track/Clip |
| TimelineClip | ClipId, MediaId, sourceIn/out, timelineStart, speed, trackId | EditPlan |
| ProcessingJob | JobId, type, status, progress, error, attempt, outputRefs | Project |
| Snapshot | SnapshotId, planVersion, dbRevision, editorProjectRef | Project |
| IntegrationCheck | id, capability, environment, status, evidenceAt | Project/system |
| ExportJob | id, outputRef, sha256, approvalId, renderStatus | Project |

## Identidade de mídia e mutação externa

- IDs UUID persistentes gerados na primeira importação. Caminho não é chave primária.
- Fingerprint em duas etapas: tamanho+metadados para avaliação barata; SHA-256 para confirmação de duplicata/conflito (streaming; não armazenar arquivo inteiro em memória).
- Movimentação externa: path mudou mas hash igual → sugerir relink; hash mudou → invalidar derivados e exigir nova análise.
- Guardar original `relativePath` e raiz externa, validar canonicalização e impedir traversal/junções inseguras.

## Tempo e sincronismo

- Representar intervalos **semiabertos** `[inicio, fim)` com racional `numerator/denominator` ou ticks inteiros de uma timebase definida; nunca usar `double` como fonte de verdade da timeline.
- `sourceIn`, `sourceOut` referem-se à mídia original; `timelineStart` refere-se ao projeto; `sourceTimeBase` e `timelineTimeBase` separadas.
- Vídeos VFR: preferir PTS/timestamps do stream e tabela de mapeamento quando necessário; FPS médio é informativo, não supor grade fixa.
- Palavras do Whisper usam segundos decimais + alinhamento ao arquivo; ao gerar cortes, converter com política de arredondamento documentada e validar limites.
- Aplicar velocidade ao trecho muda sua duração na timeline; áudio deverá acompanhar apenas se apropriado, evitando aceleração de conversas.

## JSON de plano, exemplo ilustrativo

```json
{
  "schemaVersion": 1,
  "planId": "a47d9bd5-e8ae-4ad9-8aeb-490e7820b2ff",
  "projectVersion": 3,
  "timeline": {"timeBase": {"numerator": 1, "denominator": 30000}},
  "clips": [{
    "id": "clip-0001", "mediaId": "media-001",
    "sourceStartTicks": 90000, "sourceEndTicks": 240000,
    "sourceTimeBase": {"numerator": 1, "denominator": 30000},
    "track": "V1", "speed": {"numerator": 1, "denominator": 1},
    "decisionId": "decision-100"
  }]
}
```

**Exemplo de contrato, não payload da API DaVinci.** IDs de demonstração não precisam ser UUID em todos os objetos, mas produção deve usar regras consistentes.

## SQLite / EF Core

- Banco em `3-Trabalho/estado/` **somente quando essa pasta for local e adequada**, senão em diretório de dados por usuário com referência ao projeto; nunca em C: sem avaliar espaço nem em Z: HGFS como padrão.
- Migrações EF Core versionadas no repositório; backup antes de migrar banco existente.
- Tabelas normalizadas para dados consultados; arquivos JSON imutáveis para planos versionados e transcrições completas; binários (thumbnail, waveform, proxy) no filesystem.
- Transações para decisões e atualizações; logs e checkpoints atomizados quando possível. Reconciliar saída de subprocessos com job persistido após crash.
- Índices em ProjectId/MediaId/JobId/status/hash e timestamps; testes para uniqueness e cascades sem excluir mídia física.

## Cache

Chave: `media fingerprint + tool version + profile parameters + schemaVersion + language/model id`. Cache válido só para inputs iguais. TTL não substitui hash. Evitar reprocessar transcrição large-v3 ou previews por alteração de UI. Exclusão de cache deve ser explícita, reversível se possível, restrita ao diretório gerenciado.
