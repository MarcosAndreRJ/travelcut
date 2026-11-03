# Worker HyperFrames (opcional, F09.5)

Renderização isolada de títulos, lower thirds e rotas animadas, usada apenas quando a animação é necessária.
O núcleo do TravelCut não depende de Node.js nem de HyperFrames: sem eles, o app mostra o estado real
(`NODE_UNAVAILABLE`, `NOT_INSTALLED`) ou o simulador (`mock`, `SIMULATED`).

Roteiro de homologação no host: `docs/20-HYPERFRAMES.md`.

## Estado nesta VM

- Node.js 24 presente em `C:\Program Files\nodejs`.
- **HyperFrames não instalado.** Nada foi instalado, nenhum pacote foi baixado e não há `npx` automático.
- Verificação de capacidade: `host_pending`. Nenhum template é `host_pass` até o roteiro ser executado no host.

## Como o TravelCut detecta o HyperFrames

Ordem de checagem (sem shell, `ArgumentList`, timeout curto):

1. Node.js, sempre por caminho absoluto (o diretório atual nunca é consultado):
   - `TRAVELCUT_NODE_PATH` definida → deve ser arquivo existente em caminho absoluto; senão `NODE_UNAVAILABLE` (`NODE_PATH_NOT_FOUND`);
   - sem a variável → primeiro `node.exe` (Windows) ou `node` encontrado nas entradas **absolutas** do PATH; senão `NODE_UNAVAILABLE` (`NODE_NOT_FOUND`).
   - Depois, `<node> --version`. Falha → `NODE_UNAVAILABLE` (`NODE_NOT_FOUND` ou `NODE_CHECK_FAILED`).
2. Variável `TRAVELCUT_HYPERFRAMES_CLI`:
   - ausente → `NOT_INSTALLED` (`CLI_NOT_CONFIGURED`);
   - não é caminho absoluto para arquivo existente → `NOT_INSTALLED` (`CLI_PATH_NOT_FOUND`);
   - extensão `.cmd`, `.bat` ou `.ps1` → `ERROR` (`CLI_UNSUPPORTED_TYPE`): apontar para o `.js` do pacote (executado pelo node) ou para um `.exe`.
3. `<node> <cli> --version` (quando o caminho é `.js`, `.mjs` ou `.cjs`) ou `<cli> --version` (executável).
   - Versão lida → `READY` com `rendererVersion`.
   - Código diferente de zero, tempo esgotado ou saída sem versão reconhecível → `ERROR` com código explícito.

Todos os processos saem com `ArgumentList` e sem shell: caminhos com `&`, espaços ou parênteses chegam intactos.

A versão só aparece quando foi lida do CLI. Nunca é inventada.

## Configuração no host

1. Instalar o Node.js LTS compatível no host (a VM usa Node 24).
2. Instalar o HyperFrames **no host**, fora do repositório. O comando de instalação e a versão exata
   **a confirmar** na documentação oficial do projeto HyperFrames no momento do teste. Não há comando verificado
   nesta VM.
3. Localizar o executável ou script de entrada do CLI instalado (`a confirmar no host`).
4. Definir a variável de ambiente do usuário apontando para esse caminho absoluto:

   ```powershell
   [Environment]::SetEnvironmentVariable("TRAVELCUT_HYPERFRAMES_CLI", "C:\caminho\para\cli-local", "User")
   ```

   Reiniciar o TravelCut após alterar a variável.

   Se o Node não estiver no PATH do host, definir também `TRAVELCUT_NODE_PATH` com o caminho absoluto do `node.exe`.
   Nunca apontar `TRAVELCUT_HYPERFRAMES_CLI` para `.cmd`, `.bat` ou `.ps1`: o TravelCut recusa com `CLI_UNSUPPORTED_TYPE`.

5. Conferir o estado de capacidade (`READY` com
   versões preenchidas) quando a ligação de animações ao app e ao MCP estiver ativa (pendência do orquestrador).

Não aponte a variável para `npx`, para um atalho ou para um comando que baixe pacotes.

## Contrato assumido com o CLI (a confirmar no host)

| Item | Suposição usada no código | Como o código reage se for diferente |
|---|---|---|
| Versão | `<cli> --version` imprime `x.y.z` na primeira linha (aceita `nome x.y.z`) | `ERROR` `CLI_VERSION_UNPARSEABLE`, sem versão |
| Render | `<cli> render --manifest <request.json> --assets-root <2-Assets> --output <output.mp4>` | `RENDER_FAILED` se código ≠ 0 |
| Saída | Sucesso = código 0 **e** `output.mp4` existente e não vazio | `OUTPUT_MISSING` (erro, nunca sucesso) |
| Escrita | Só dentro de `3-Trabalho/motion/<renderId>/`; nenhum segmento desde o projeto pode ser link ou junção | `PERMISSION_DENIED` antes de criar, apagar ou executar qualquer coisa |
| Reaproveitamento | `SIMULATED` ou `READY_REAL` só são reaproveitados se o `AdapterMode` do manifesto for o mesmo; `READY_REAL` exige `output.mp4` existente e não vazio | Render executado de novo |

Se o CLI real usar outra sintaxe, altere **somente** `HyperFramesRenderer` (constantes `RenderSubcommand`,
`ManifestOption`, `AssetsRootOption`, `OutputOption`) e atualize esta tabela e `docs/20-HYPERFRAMES.md`
com a evidência do host.

## Templates previstos

| templateId | Parâmetros (nome: obrigatório, máximo) |
|---|---|
| `title_card` | `title` (obrigatório, 120), `subtitle` (160), `accentColor` (#RRGGBB), `backgroundAsset` (asset) |
| `lower_third` | `line1` (obrigatório, 80), `line2` (80), `accentColor` (#RRGGBB) |
| `map_route` | `origin` (obrigatório, 80), `destination` (obrigatório, 80), `routeLabel` (120), `accentColor` (#RRGGBB), `mapAsset` (asset) |

Limites comuns: largura 320 a 3840 e altura 320 a 2160, ambas pares; 1 a 120 fps (num/den, den até 1001);
1 a 7200 quadros e no máximo 120 s; até 16 parâmetros. Parâmetros fora da lista são recusados.

## Assets autorizados

- Somente arquivos dentro de `2-Assets/` do projeto (somente leitura), por caminho relativo.
- Extensões: PNG, JPG, JPEG ou WebP. SVG não é aceito, porque pode conter script.
- Caminhos absolutos, `..`, unidade de disco são recusados (`ASSET_OUTSIDE_ASSETS`).
- Link, junção ou atalho em **qualquer** segmento entre o projeto e o asset (`2-Assets`, subpastas ou o arquivo) é
  recusado com `PERMISSION_DENIED`.
- Mídia de `1-Bruto/` nunca é usada como asset de animação.
- Mapas e marcas: usar somente material com licença que permita uso no vídeo. A licença é responsabilidade do
  usuário e deve ser conferida antes de colocar o asset em `2-Assets/`.
- Mídia local não é enviada a APIs pagas.

## Saídas

- Manifesto: `3-Trabalho/motion/<renderId>/request.json` (escrita atômica).
- Resultado: `3-Trabalho/motion/<renderId>/result.json`.
- Vídeo: `3-Trabalho/motion/<renderId>/output.mp4`, só quando o CLI termina com sucesso e o arquivo existe.
- `4-Final/` recebe apenas material aprovado pelo usuário, nunca a saída bruta de animação.

## Modo mock

`TravelCut.Infrastructure.Motion.MockMotionRenderer` responde `SIMULATED` com `isSimulated=true`, sem Node, sem CLI
e sem arquivo de vídeo. A mensagem é "DEMONSTRAÇÃO: nenhum vídeo foi renderizado." O mock não substitui o HyperFrames
e não é apresentado como render real.

## Testes

```powershell
dotnet test tests\TravelCut.UnitTests --filter "FullyQualifiedName~TravelCut.Tests.Motion"
```

Esses testes usam runner falso. Não provam que o HyperFrames renderiza, apenas que o adaptador trata
corretamente cada resposta. A prova real exige o roteiro de `docs/20-HYPERFRAMES.md` executado no host.
