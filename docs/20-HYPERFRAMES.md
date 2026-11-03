# 20 — HyperFrames (F09.5): roteiro de homologação no host

**Estado:** `host_pending`. Nesta VM o HyperFrames **não está instalado** e nenhum render real foi executado.
Os testes automatizados (`MotionUseCases`, `HyperFramesRenderer` com runner falso, `MockMotionRenderer`) provam o
tratamento das respostas, não a renderização. Este roteiro fecha a integração real quando o log de execução no host
estiver anexado em `progresso/HOST_TESTS.md`. Mock de teste não é prova de integração.

Ver também: `workers/hyperframes/README.md` (configuração e contrato assumido), `docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md`
(roteiro irmão do DaVinci), `docs/05-CONTRATOS-E-INTEGRACOES.md`.

---

## 1. Pré-condições

| Item | Exigência | Como conferir |
|---|---|---|
| Windows | Host com SO compatível com o TravelCut (a VM Windows 10 Pro 22H2 é experimental) | `winver` |
| Node.js | Versão LTS compatível com o HyperFrames (a VM tem Node 24) | `node --version` |
| HyperFrames | Instalado **no host**, por fonte oficial | a confirmar no host: comando e versão exatos |
| Variável | `TRAVELCUT_HYPERFRAMES_CLI` = caminho absoluto de um `.exe` ou do script `.js`/`.mjs`/`.cjs` (nunca `.cmd`, `.bat` ou `.ps1`) | `echo %TRAVELCUT_HYPERFRAMES_CLI%` |
| Variável opcional | `TRAVELCUT_NODE_PATH` = caminho absoluto do `node.exe`, se o Node não estiver no PATH do host | `echo %TRAVELCUT_NODE_PATH%` |
| Assets | Material com licença adequada em `2-Assets/` do projeto de teste | conferir a licença antes |
| Build | `TravelCut.Desktop` e os testes do mesmo commit; anotar hash | `dotnet --info` |

Registrar em `progresso/HOST_TESTS.md`: Windows (build), versão do Node, versão e origem do HyperFrames, caminho do CLI
(somente o nome do arquivo, sem pasta de usuário), hash do build.

---

## 2. Preparação

1. Instalar o Node.js compatível (sem instalações globais feitas pelo TravelCut).
2. Instalar o HyperFrames conforme a documentação oficial. **Comando a confirmar no host** (não verificado nesta VM).
3. Localizar o CLI instalado e anotar o caminho absoluto. Não usar `npx` nem atalhos, nem `.cmd`/`.bat`/`.ps1`
   (o TravelCut recusa esses formatos com `CLI_UNSUPPORTED_TYPE`; para um script, apontar para o `.js` do pacote).
4. Definir `TRAVELCUT_HYPERFRAMES_CLI` (usuário) com esse caminho e reiniciar o TravelCut.
5. Opcional: se o Node não estiver no PATH, definir `TRAVELCUT_NODE_PATH` com o caminho absoluto do `node.exe`.
   Sem a variável, o TravelCut procura `node.exe` apenas nas entradas absolutas do PATH; o diretório atual nunca é
   consultado. Caminhos com `&`, espaços ou parênteses são aceitos e passados intactos (sem shell).
6. Preparar um projeto de teste `TravelCut_Harness_*` (nunca um projeto pessoal) com um PNG em `2-Assets/`.

---

## 3. Passos de verificação

| # | Ação | Resultado esperado | Registro |
|---|---|---|---|
| H1 | `node --version` no terminal | Versão impressa, código 0 | versão |
| H2 | `<cli> --version` no terminal (mesmo caminho da variável) | Versão na primeira linha, código 0 | texto exato, sem caminhos de usuário |
| H3 | Consultar as capacidades de animação pela ligação do app (requer integração IPC/MCP de motion, pendência do orquestrador) | `environmentState=READY`, `nodeVersion` e `rendererVersion` preenchidos, `isSimulated=false` | captura da resposta |
| H4 | Sem a variável (ou com caminho inexistente) | `NOT_INSTALLED` com `CLI_NOT_CONFIGURED` ou `CLI_PATH_NOT_FOUND` | captura |
| H5 | Renomear temporariamente o `node` do PATH, ou usar PATH sem Node (sem `TRAVELCUT_NODE_PATH`) | `NODE_UNAVAILABLE` com `NODE_NOT_FOUND`; com `TRAVELCUT_NODE_PATH` apontando para arquivo inexistente, `NODE_PATH_NOT_FOUND` | captura |
| H5b | Apontar `TRAVELCUT_HYPERFRAMES_CLI` para um `.cmd` (ou `.bat`/`.ps1`) existente | `ERROR` com `CLI_UNSUPPORTED_TYPE`; nenhum processo do CLI iniciado | captura |
| H6 | Pedir render `title_card` com `title` e 90 quadros a 30 fps (1920x1080) | `READY_REAL` com `outputRelativePath=3-Trabalho/motion/<renderId>/output.mp4` e o arquivo existente, não vazio | `result.json` e tamanho do arquivo |
| H7 | Repetir H6 com a mesma chave (mesmo adaptador e `output.mp4` íntegro) | Resultado reaproveitado, sem novo processo de render | log |
| H7b | Apagar ou zerar o `output.mp4` do H6 e repetir com a mesma chave | Render executado de novo; `READY_REAL` com arquivo não vazio | log e tamanho |
| H7c | Rodar com `TRAVELCUT_MOTION_ADAPTER=mock` e depois sem ele, mesma chave | O render HyperFrames não reaproveita o `SIMULATED`; `request.json` mostra `AdapterMode` `hyperframes` | `request.json` |
| H8 | Repetir H6 com a mesma chave e outro título | `IDEMPOTENCY_KEY_REUSED`, sem render | mensagem |
| H9 | Pedir render com asset fora de `2-Assets` (ex.: caminho de `1-Bruto`) | `ASSET_OUTSIDE_ASSETS`, nenhum processo iniciado | mensagem |
| H9b | Criar em `2-Assets/` uma junção (`mklink /J`) apontando para uma pasta fora do projeto com um PNG; pedir render com `backgroundAsset` por essa junção | `PERMISSION_DENIED`, nenhum processo iniciado, pasta externa sem alteração | listagem antes/depois |
| H9c | Criar `3-Trabalho/motion` (ou `3-Trabalho/motion/<renderId>`) como junção para pasta externa vazia; pedir render | `PERMISSION_DENIED`; a pasta externa continua vazia (nenhum manifesto ou vídeo) | listagem antes/depois |
| H10 | Cancelar um render longo pelo canal da ligação de motion | `RENDER_CANCELED`; nenhum `output.mp4` parcial reconhecido | captura |
| H11 | Verificar que `1-Bruto/` e `2-Assets/` não receberam escrita | Hash ou listagem antes/depois iguais | listagem |
| H12 | Inspecionar `3-Trabalho/motion/<renderId>/` | Somente `request.json`, `result.json` e, no sucesso, `output.mp4` | listagem |
| H13 | Com o app aberto, pedir o mesmo render (mesma chave) pelo app e pelo MCP ao mesmo tempo, no mesmo projeto de teste | Um pedido executa o render; o outro recebe `OPERATION_IN_PROGRESS` sem novo processo do CLI; a trava `motion-render.lock` some ao fim | log dos dois processos e listagem de `3-Trabalho/.locks` |
| H14 | Pedir render com asset em `2-Assets` que esteja como placeholder do OneDrive (somente online) | `PERMISSION_DENIED` sem download do arquivo (decisão intencional, ver 5.1) | mensagem e tamanho do arquivo antes/depois |

Se o CLI real responder de forma diferente das suposições (ver `workers/hyperframes/README.md`), registrar a
saída **exata** (sem caminhos de usuário) em `progresso/HOST_TESTS.md`. O código de erro aparecerá como
`CLI_VERSION_UNPARSEABLE`, `RENDER_FAILED` ou `OUTPUT_MISSING`. Não marcar `host_pass` nesse caso.

---

## 4. Critérios de aprovação

- H1 a H3 e H6 a H12 passam com evidência anexada.
- H4 e H5 mostram estados explícitos (nunca sucesso).
- Nenhuma saída `SIMULATED` aparece como vídeo real.
- Os templates passam de `host_pending` para `host_pass` somente depois de H6 (um por template, com amostra de cada um).

Estado de verificação dos templates após o host:

| Template | Verificação atual | Após H6 (esperado) |
|---|---|---|
| `title_card` | `host_pending` | `host_pass` se H6 passar |
| `lower_third` | `host_pending` | `host_pass` após render de amostra |
| `map_route` | `host_pending` | `host_pass` após render de amostra com `mapAsset` licenciado |

---

## 5. Limitações conhecidas

- Os testes automatizados usam runner falso: cobrem contrato e tratamento de erros, não a renderização.
- A sintaxe do CLI é uma suposição isolada em `HyperFramesRenderer` até ser confirmada no host.
- Links e junções são verificados em **todos** os segmentos entre a raiz do projeto e o alvo (`2-Assets`, subpastas,
  arquivo; em saída, `3-Trabalho`, `3-Trabalho/motion`, `3-Trabalho/motion/<renderId>` e `output.mp4`), sem seguir o link.
  A checagem ocorre antes de criar ou apagar qualquer coisa. Persiste o risco de corrida: um link criado entre a checagem
  e a escrita não é detectado (TOCTOU). Registrar como risco residual; não há bloqueio de diretório no Windows sem API dedicada.
- Arquivos simbólicos de arquivo (`mklink` sem `/J`) exigem privilégio ou modo de desenvolvedor e não foram exercitados
  nos testes automatizados; a junção de diretório (`mklink /J`) foi testada.
- O mock (`MockMotionRenderer`) não gera vídeo. Não usar a saída do mock em `4-Final/`.
- Limite de um render por vez por instância de caso de uso. Entre processos (app e MCP), o render adquire a operação
  `motion-render` (trava de arquivo em `3-Trabalho/.locks/motion-render.lock`, espera de 2 s) antes de qualquer escrita
  em `3-Trabalho/motion`. Ocupada = `OPERATION_IN_PROGRESS`, sem render e sem pasta criada. Os testes simulam dois
  processos com duas instâncias e a mesma trava de arquivo (`mock_pass`); a concorrência real entre dois processos do
  Windows fica `host_pending` (H13).

### 5.1 Riscos residuais (revisão de concorrência e caminhos)

| Risco | Situação | Decisão / tratamento |
|---|---|---|
| Trava entre processos | Implementada com `FileProjectOperationLock` (`FileShare.None` + `DeleteOnClose`). Só vale para o mesmo projeto em disco local/SMB do Windows. | Espera curta; ocupada recusa o pedido. Não há trava em Unix nem entre máquinas diferentes. A pasta `3-Trabalho/.locks` não é checada contra junção (a trava só cria `motion-render.lock`); registrar como pendência. |
| Nomes curtos 8.3 (ex.: `MYLINK~1`) | `Path.GetFullPath` expande o nome curto antes da checagem de reparse point; testes com nome 8.3 real passam em ambas as implementações (não discriminam). | Caminho relativo de asset com `~` seguido de dígito é recusado (`ASSET_OUTSIDE_ASSETS`); a checagem de atributos usa `File.GetAttributes` sem seguir o link. Validar no host se o volume tiver 8.3 desabilitado ou nomes diferentes. |
| Curingas `*` e `?` em asset | Rejeitados na validação de caminho relativo. | `ASSET_OUTSIDE_ASSETS`. Teste em `MotionPathSecurityTests`. |
| Junção quebrada (alvo removido) | `File.GetAttributes` mantém `ReparsePoint` e a checagem bloqueia. | `PERMISSION_DENIED`. Teste `BrokenJunction_InAssets_IsBlocked`. |
| Placeholders OneDrive (arquivos sob demanda) e arquivos com deduplicação | Têm o atributo `ReparsePoint`; a checagem os trata como link. | **Bloqueio intencional** (decisão conservadora): a leitura do asset poderia disparar download ou acesso a outro bloco. O usuário deve deixar o asset disponível localmente (ex.: "Sempre manter neste dispositivo") ou copiá-lo para `2-Assets`. A mensagem atual diz "link, junção ou atalho" e não distingue esse caso. |
| TOCTOU | Link criado entre a checagem e a escrita não é detectado. | Risco residual (ver tabela da seção 5). |

---

## 6. Retorno e pendências

Após executar o roteiro no host, o responsável anexa o log em `progresso/HOST_TESTS.md` e informa ao orquestrador:
resultado por passo, versões, hash do build e qualquer divergência da suposição de CLI. Sem esse retorno, a
integração HyperFrames permanece `host_pending`.
