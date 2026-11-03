# Integração DaVinci — VM sem editor, host com Studio

## Ambientes

**VM (dev):** Windows 10 Pro 22H2, 4 vCPUs, 14,93 GB RAM, VMware SVGA 3D sem CUDA, K: projeto local, Z: HGFS com host, sem DaVinci. FFmpeg/Whisper não presumidos instalados. **Host:** DaVinci Resolve Studio licenciado, recursos e versão reais devem ser auditados na homologação; Claude/Whisper/HyperFrames configurados anteriormente não equivalem a teste aprovado.

## Adaptadores

`IEditorAdapter` define `DetectCapabilities`, `CreateProject`, `ImportMedia`, `CreateTimeline`, `ApplyEditPlan`, `QueryTimeline`, `ExportPreview`, `ExportProject`, `CreateSnapshot`. `MockResolveAdapter` roda na VM, produz eventos e assertions; `DaVinciResolveAdapter` usa bridge Python, API oficial e/ou MCP disponível no host, mediante verificação.

**Estados de ambiente:** `NOT_INSTALLED`, `NOT_RUNNING`, `SCRIPTING_DISABLED`, `MCP_UNAVAILABLE`, `READY_PARTIAL`, `READY`, `MOCK`; capabilities por operação e versão. Mock **não** emite `.drp` real para simular sucesso; pode emitir manifesto de montagem `.json` claramente nomeado.

## API/MCP: verificação de capacidades

O SDK de scripting do DaVinci historicamente usa Python/Lua. Métodos e limites variam por versão; documentação **da instalação real** determina o suporte. Para cada operação registrar `documented`, `mock_pass`, `host_pending`, `host_pass` ou `unsupported`. Operações complexas (zooms por keyframe, efeitos, velocidade/reverse, títulos, Fairlight) podem exigir estratégia diferente da simples inserção de clipes. Não prometer manipulação de todos os recursos.

## Protocolo de homologação no host

1. Registrar Windows, versão/edição Studio, status licença, Python, APIs, MCP e caminho executável.
2. Abrir Resolve e criar projeto **TEMPORÁRIO** com mídia sintética pequena. Não usar projetos pessoais.
3. Testar read-only de versão e projeto; capturar logs.
4. Executar CreateProject → ImportMedia → CreateTimeline → ApplyEditPlan(2 clips de origem; áudio sincronizado).
5. Fechar/reabrir projeto, verificar offsets/duração e integridade de mídias.
6. Testar snapshot e export `.drp`/prévia somente se disponíveis; registrar capacidades faltantes.
7. Testar timeout, Resolve fechado, arquivo ausente e uma operação não suportada.
8. Registrar captura de tela, log e checklist em `progresso/HOST_TESTS.md` ou pasta de evidências, sem segredos.
9. Retornar erros ao desenvolvimento na VM; **não marcar fase como HOST_VALIDADO até evidencia real**.

## Distribuição VM→host

- Construir artefato **self-contained ou framework-dependent explicitamente identificado**; incluir dependências conhecidas e checar compatibilidade do runtime/Windows no host.
- Não embutir pesos Whisper/arquivos grandes no EXE por padrão; descoberta de instalações e paths configuráveis.
- Usar Z: para transferência de artefatos quando permitido; não armazenar SQLite em HGFS como padrão.
- Testes reais só no host. Na VM, mocks podem aprovar contratos, mas não provar áudio, vídeo ou APIs do Resolve.

## Segurança operacional

Validar que o projeto criado é temporário e próprio do TravelCut; snapshot antes de alteração; travar acesso concorrente à timeline; confirmar capability e revision; nunca excluir mídia de origem; não usar UI automation para contornar API sem avaliação e consentimento.
