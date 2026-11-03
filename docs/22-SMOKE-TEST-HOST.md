# 22 — Smoke test do pacote no host: 0.10.0-host-test.1

**Estado:** `host_pending`. Nada deste roteiro foi executado. Não marque nenhum item como aprovado sem evidência.
**Pacote:** `TravelCutStudio-0.10.0-host-test.1-win-x64.zip` · SHA-256 `3494021673f9bcf740e85e6fcee615dcf2ca5adc8fd9fdd23a65e31e39b18f90`
**Notas da versão:** [21-NOTAS-DE-VERSAO.md](21-NOTAS-DE-VERSAO.md) · **Limitações:** [23-LIMITACOES-E-RISCOS.md](23-LIMITACOES-E-RISCOS.md)

## Regras do teste

- Use só **projetos de teste** criados neste roteiro, em pasta dedicada, fora de raiz de unidade, de pasta pessoal e de pasta do sistema. Nunca aponte para projeto pessoal.
- Use **só mídia sintética** (gerada por `tools/host-validation/davinci/New-SyntheticMedia.ps1` em `%TEMP%`).
- Antes de qualquer envio ao DaVinci: **salve e feche o seu projeto pessoal**. A aplicação troca o projeto aberto no Resolve.
- Origem intacta: `1-Bruto` e `2-Assets` não podem mudar. Compare SHA-256 antes e depois.
- Não cole em relatórios credenciais, tokens, transcrições nem caminhos privados desnecessários.
- Anote cada item como `PASS`, `FAIL` ou `NOT_RUN` em `progresso/HOST_TESTS.md`, com versão, passos, resultado, log e captura. A tabela de cada item diz em qual seção anotar.

## Pré-requisitos

- Windows 10 ou 11 x64 coberto pelo suporte do .NET 10 (a VM Windows 10 22H2 não é suportada oficialmente).
- Microsoft Edge WebView2 Runtime.
- FFmpeg ≥ 5.1 no PATH ou em `TRAVELCUT_FFMPEG_PATH`.
- Para Whisper (opcional): Python 3.10 ou 3.11, 64 bits, de python.org, em venv isolado; modelo `large-v3` obtido manualmente. Detalhes em `workers\README.md`.
- Para DaVinci (opcional neste smoke): DaVinci Resolve Studio com *External scripting using = Local*. Detalhes em [docs/18](18-ROTEIRO-HOMOLOGACAO-DAVINCI.md).
- Para MCP (opcional): decisão do usuário de configurar. Detalhes em [docs/19](19-MCP-TRAVELCUT.md).

## Roteiro

| # | Ação | Resultado esperado | Anotar em (`HOST_TESTS.md`) |
|---|---|---|---|
| 1 | Calcular o SHA-256 do `.zip` com `Get-FileHash -Algorithm SHA256` e comparar com o `.zip.sha256`. | Valor igual a `3494021673f9bcf740e85e6fcee615dcf2ca5adc8fd9fdd23a65e31e39b18f90`. | Ambiente ("TravelCut build/hash") |
| 2 | Extrair o pacote e executar o trecho de verificação da seção 5 de `LEIA-ME-HOST.md`. | Nenhuma linha `DIVERGE:`. `MANIFEST.json` lista 713 arquivos. | Ambiente ("TravelCut build/hash") |
| 3 | Com o TravelCut fechado, copiar `%LOCALAPPDATA%\TravelCutStudio\` inteiro (inclui `travelcut.db`, `-wal` e `-shm` se existirem) para uma pasta fora dele. | Cópia criada. Se não há banco, anotar "sem banco anterior". | Roteiro mínimo (antes de tudo) |
| 4 | Abrir `app\TravelCut.Desktop.exe`. | Janela com a interface. Tela de boas-vindas, sem projeto aberto sozinho. Recentes reais, ou lista vazia. Nenhuma pasta criada. | Roteiro mínimo ("App inicia") |
| 5 | Se havia banco antigo: observar a primeira abertura. | `travelcut.db.bak-<utc>` criado se houve migração. Decisões antigas preservadas. Sem banco antigo: nada a registrar. | Roteiro F06 (primeira abertura) |
| 6 | Abrir a tela de ambiente. | Cada item mostra estado real (Disponível, Não detectado, Indisponível ou Não verificado). Nenhuma versão fixa. Nenhum selo DEMONSTRAÇÃO. O estado bate com o host (ex.: FFmpeg instalado aparece como disponível). | Roteiro F04b/F05 |
| 7 | Criar projeto de teste em pasta dedicada (ex.: `D:\TravelCutTestes\smoke-01`). | O app pede confirmação antes de criar as 4 pastas. Após confirmar, `1-Bruto`, `2-Assets`, `3-Trabalho` e `4-Final` existem. Tentar uma raiz de unidade é recusado (`INVALID_PROJECT_ROOT`). | Roteiro mínimo ("Projeto de teste com pastas 1–4") |
| 8 | Gerar duas mídias sintéticas em `%TEMP%` e importar com "Adicionar arquivos". | Itens com duração e resolução reais (FFprobe). Progresso por bytes. Hash verificado. Registrar o SHA-256 das mídias sintéticas em `%TEMP%` (origem) e das cópias em `1-Bruto`, logo após a importação (base para o item 21). | Roteiro F04b/F05 (importação) |
| 9 | Importar de novo um arquivo com o mesmo nome e depois o mesmo arquivo idêntico. Conferir as miniaturas em `3-Trabalho\cache\thumbs`. | Nome vira `nome (1).ext`. Reimportação idêntica mostra "Já existia (idêntico)". Miniaturas reais existem. | Roteiro F04b/F05 |
| 10 | Iniciar uma importação maior e cancelar no meio. | Nenhum `.tcpart` sobra em `1-Bruto`. Arquivos já concluídos continuam. | Roteiro F04b/F05 |
| 11 | "Iniciar análise" no projeto de teste. | Silêncios reais das mídias. Sem Whisper: transcrição "Indisponível" com motivo, sem transcrição inventada. Com Whisper configurado: palavras com tempos. | Roteiro F04b/F05 (análise) |
| 12 | Abrir a Revisão. Aprovar uma sugestão e rejeitar outra. | Candidatos com evidência. Estados mudam na tela. Após fechar e reabrir o app (item 22), os estados continuam iguais. | Roteiro F07 (decisões) |
| 13 | Tela 03: "Gerar plano". | Revisão R1 criada em `3-Trabalho\plans\<planId>\r0001.json`. Seções na ordem das gravações. Títulos com horário local, ou "data aproximada (arquivo)" sem data de câmera. `1-Bruto` e `2-Assets` não mudam. | Roteiro F06 |
| 14 | Prévia: tocar um clipe H.264 (celular ou GoPro); depois um HEVC, um ProRes, BRAW ou Insta360 se houver. | H.264 toca. HEVC mostra "suporte não confirmado" e toca ou falha com mensagem (anotar se a extensão HEVC está instalada). ProRes, BRAW e INSV oferecem "Gerar proxy". Selo "não frame-accurate" visível. | Roteiro F07 (codecs e prévia) |
| 15 | Desfazer e refazer uma decisão (Z e Y). Aprovar um corte com o plano já gerado. | Desfazer e refazer funcionam. Aprovar corte com efeito no plano mostra "Plano atualizado: rN". Decisão sem efeito no plano não cria revisão nova. | Roteiro F07 (decisões e plano) |
| 16 | Tela "Enviar ao DaVinci" com o Resolve fechado: preparar a simulação seca (dry-run) do plano do projeto de teste. | Ambiente mostra "Resolve fechado" ou `NOT_INSTALLED`. A simulação mostra projeto, timeline, fps, clipes em frames, rejeitados e avisos. Nenhuma mensagem de sucesso. Nenhum `.drp` criado. Nada no Resolve. | Roteiro F08 (bridge) |
| 17 | Com o Resolve aberto (scripting Local), projeto pessoal salvo e fechado: confirmar a troca e aplicar o plano de teste. Repetir com a mesma chave de idempotência. | Projeto novo `TravelCut_*` criado. Resultado `READY_REAL` ou `PARTIAL_REAL` conforme docs/18, ou erro controlado com código. Snapshot `.drp` em `3-Trabalho\editor\<planId>\`. Segunda vez: "resultado anterior reaproveitado". Sem resultado de sucesso sem `.drp`. | Roteiro F08 (apply) e [docs/18](18-ROTEIRO-HOMOLOGACAO-DAVINCI.md) |
| 18 | Reversão do DaVinci: no Project Manager, apagar só os projetos `TravelCut_*` de teste (confira o nome antes). Reabrir o projeto pessoal que estava aberto. | Só os projetos de teste somem. O projeto pessoal volta a abrir. Nenhum projeto sem o prefixo `TravelCut_` é tocado. | Roteiro F08 (reversão) — [docs/18 §6](18-ROTEIRO-HOMOLOGACAO-DAVINCI.md#6-reversão-limpeza-manual) |
| 19 | MCP read-only (só se configurado por decisão do usuário): `TravelCut.Mcp.exe --help`; depois cliente conectado com `TRAVELCUT_MCP_DATABASE` apontando para cópia descartável do banco e `TRAVELCUT_MCP_PROJECTS` com o GUID do projeto de teste, **sem** `--allow-mutations`. | `--help` vai para stderr com código 0. O cliente lista exatamente 8 ferramentas de leitura, sem `submit_review`, `queue_analysis` nem `apply_plan`. `get_editor_capabilities` mostra o estado real do host. | Evidências e erros (MCP ainda não tem seção própria) — [docs/19 §9](19-MCP-TRAVELCUT.md) |
| 20 | Abrir `%LOCALAPPDATA%\TravelCutStudio\logs`. | Arquivo do dia existe e registra as ações do roteiro. Sem senhas, tokens ou transcrições. Caminhos privados mínimos. Revisar antes de anexar. | Evidências e erros (anexar só trechos revisados) |
| 21 | Comparar o SHA-256 atual de `1-Bruto` e `2-Assets` com os valores do item 8. | Iguais. Nenhum original alterado, movido ou apagado. Só arquivos novos de importação. | Roteiro mínimo ("originais preservados") |
| 22 | Fechar o app por completo. Reabrir. Conferir recentes, decisões, desfazer e refazer, plano R1 e registro de aplicação. | Tudo igual ao que estava antes de fechar. Intervalos ajustados preservados. | Roteiro F06 e F07 (reabrir) |

## Ao final

1. Preencher `progresso/HOST_TESTS.md` (seção "Evidências e erros") com status, versão do Windows, Resolve, Python e FFmpeg, o `MANIFEST.json` do pacote e os logs revisados.
2. Informar ao orquestrador o resultado de cada item. Falhas voltam à fase correspondente para correção na VM, com teste de regressão.
3. Sem evidência, o item continua `NOT_RUN`. Não converter `mock_pass` em `host_pass`.
