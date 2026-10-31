# Prompt de Desenvolvimento - Etapa 09: Testes Finais, Estabilizacao e Release Beta

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 09 - Bateria de Testes de Integracao, Otimizacao de Performance e Geracao do Pacote Release Beta
- **Objetivo Central:** Conduzir a validacao de ponta a ponta (end-to-end) de todo o fluxo operacional do TravelCut Studio utilizando filmagens reais de diferentes cameras e formatos. Executar testes de estresse e regressao, verificar o consumo controlado de memoria RAM e GPU, eliminar potenciais vazamentos de recursos (memory leaks) e empacotar o binario executavel nativo para Windows (`.msi` / `.exe`) pronto para distribuicao aos primeiros usuarios beta.
- **Resultado Esperado:** O TravelCut Studio compila em modo release sem erros, passa em todos os testes automatizados e manuais de regressao, processa com sucesso uma sessao de edicao de ponta a ponta com filmagens brutas de viagem e gera o instalador executavel Windows na pasta `src-tauri/target/release/bundle/`.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `src-tauri/tauri.conf.json`:
  - Configuracoes de pacote: `productName: "TravelCut Studio"`, `version: "1.0.0-beta"`, identificador, permissoes e definicoes de bundle para Windows (WiX `.msi` e instalador NSIS `.exe`).
- `crates/travelcut-media/`:
  - Bateria de testes unitarios e de integracao em Rust (`cargo test`).
- `package.json`:
  - Scripts de compilacao: `npm run build`, `npm run tauri build`.
- Todas as etapas anteriores concluidas (00 a 08):
  - Contratos Tauri e Rust unificados.
  - Ingestao e persistencia em `.travelcut`.
  - Player e waveform nativos.
  - Transcricao Whisper/VAD sincronizada.
  - Classificacao audiovisual de cenas.
  - Sugestoes de cortes e revisao human-in-the-loop.
  - Timeline NLE reproduzivel.
  - Pipeline de automacao com progresso.
  - Exportacao precisa para DaVinci Resolve.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** adicione novas funcionalidades experimentais ou altere a arquitetura central nesta fase de estabilizacao. O foco deve ser exclusivamente correcao de bugs, desempenho e fechamento de release.
- **NAO** ignore erros ou avisos criticos do compilador TypeScript (`tsc`) ou do Rust (`clippy`). O build de release deve ser estritamente limpo.
- **NAO** deixe dependencias de desenvolvimento ou credenciais temporarias embutidas no pacote final.

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 9.1: Bateria de Testes Automatizados de Regressao
1. **Validacao do Motor Rust:**
   - Execute a suite completa de testes em `crates/travelcut-media`:
     ```bash
     cargo test --all --release
     ```
   - Garanta que todos os testes de probe, scanner, waveform e geracao de miniaturas passem com 100% de sucesso.
2. **Validacao de Tipagem e Sintaxe Frontend:**
   - Execute a checagem de tipos e o empacotamento Vite:
     ```bash
     npm run build
     ```
   - Certifique-se de que nenhum erro de tipagem ou importacao circular permaneça.

### Tarefa 9.2: Teste de Integracao Pratico de Ponta a Ponta (End-to-End Walkthrough)
Execute o roteiro oficial de homologacao com uma pasta real de filmagens:
1. **Passo 1:** Iniciar o TravelCut Studio a partir do binario de teste.
2. **Passo 2:** Clicar em "Novo Projeto" e selecionar uma pasta real contendo videos de viagem (ex.: arquivos MP4/MOV de cameras Sony, DJI ou celulares).
3. **Passo 3:** Confirmar que a grade de midia exibe resolucao, framerate e duracao reais sem nenhum dado ficticio.
4. **Passo 4:** Tocar um dos videos no player, verificar o timecode SMPTE e os picos de audio na forma de onda.
5. **Passo 5:** Clicar em "Processar Projeto com IA" e acompanhar o progresso das tarefas na barra de status.
6. **Passo 6:** Ao finalizar, abrir o painel de revisao, inspecionar a transcricao e aprovar ao menos 3 sugestoes de cortes.
7. **Passo 7:** Navegar na timeline NLE, realizar um ajuste de alca (trim) e testar a reproducao sequencial dos trechos mantidos.
8. **Passo 8:** Acessar a tela de exportacao, selecionar o formato DaVinci Resolve XML e exportar a timeline para o disco.
9. **Passo 9:** Abrir o DaVinci Resolve Studio, importar o arquivo XML gerado e certificar-se de que a timeline monta perfeitamente com todas as midias conectadas.

### Tarefa 9.3: Auditoria de Performance e Uso de Recursos
1. Monitorar o aplicativo durante o processamento de video:
   - Uso de memoria RAM: garantir que a aplicacao nao exceda limites razoaveis (limite recomendado: < 1.5GB de RAM em operacao normal).
   - Fechamento de arquivos e handles: verificar se os descritores de arquivo abertos pelo FFprobe e FFmpeg sao devidamente liberados apos a conclusao.
   - Responsividade da interface: certificar-se de que a rolagem da biblioteca e a navegacao da timeline mantenham 60 quadros por segundo (60 FPS) sem congelamentos.

### Tarefa 9.4: Configuracao de Release e Geracao do Instalador Windows
1. No `src-tauri/tauri.conf.json`, revise os metadados finais de publicacao:
   - `version`: "1.0.0-beta"
   - `bundle`:
     - `active`: true
     - `targets`: ["nsis", "msi"]
     - `icon`: icones oficiais da aplicacao (`icons/32x32.png`, `icons/128x128.png`, `icons/icon.ico`)
2. Execute o comando de build de producao:
   ```bash
   npm run tauri build
   ```
3. Verifique a criacao dos instaladores na pasta de destino:
   - `src-tauri/target/release/bundle/nsis/TravelCutStudio_1.0.0-beta_x64-setup.exe`
   - `src-tauri/target/release/bundle/msi/TravelCutStudio_1.0.0-beta_x64_en-US.msi`

### Tarefa 9.5: Elaboracao do Manual Rapido de Inicio (Quickstart Beta)
1. Crie o arquivo `docs/BETA_QUICKSTART.md` contendo:
   - Requisitos minimos de sistema (Windows 10/11 64-bit, 16GB de RAM recomendados, presenca do DaVinci Resolve 18/19 instalado).
   - Passo a passo de instalacao e primeiro uso.
   - Como reportar problemas ou enviar sugestoes de melhoria durante o periodo de testes beta.

---

## 5. Procedimentos de Teste e Validacao
Realize as seguintes verificacoes finais:

1. **Teste de Instalacao Limpa:**
   - Instale o executavel gerado (`.exe`) em uma maquina de teste ou ambiente limpo.
   - Verifique se o atalho do TravelCut Studio e criado no Menu Iniciar e na Area de Trabalho.
   - Abra o programa instalado e execute um ciclo rapido de edicao.

2. **Teste de Desinstalacao:**
   - Remova o aplicativo pelo Painel de Controle do Windows e confirme a desinstalacao sem residuos indesejados no sistema.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] Todos os testes automatizados de Rust e checagem de tipos TypeScript passam com sucesso.
- [ ] O ciclo completo (Ingestao -> Player -> Transcricao -> Cortes -> Timeline -> Exportacao DaVinci) funciona perfeitamente sem falhas.
- [ ] O executavel Windows (`.exe` / `.msi`) e gerado com sucesso pelo Tauri sem erros de empacotamento.
- [ ] O aplicativo nao apresenta vazamentos de memoria ou processos orfaos em segundo plano.
- [ ] O arquivo `BETA_QUICKSTART.md` esta redigido e pronto para acompanhar a distribuicao da versao beta.

---

## 7. Conclusao da Trilha
A conclusao bem-sucedida desta etapa marca a conclusao da versao **v1.0.0-beta** do TravelCut Studio, tornando o software pronto para avaliacao operacional com editores de video reais.
