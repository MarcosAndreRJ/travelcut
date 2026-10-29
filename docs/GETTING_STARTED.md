# Guia de Desenvolvimento, Execucao e Testes

Este guia fornece as instrucoes necessarias para configurar o ambiente de desenvolvimento local, compilar todos os componentes e executar as suites de testes automatizados do **TravelCut Studio**.

---

## 1. Pre-requisitos do Sistema

Para compilar e executar o projeto em sua totalidade, as seguintes ferramentas sao recomendadas:

- **Node.js**: Versao 18.x ou 20.x LTS com npm (ou pnpm).
- **Rust Toolchain**: Rust 1.75+ (cargo, rustc).
- **Python**: Versao 3.10 ou superior (testado e homologado no Python 3.13).
- **FFmpeg & FFprobe**: Versao 5.x ou superior no PATH do sistema operacional (necessario para decodificacao de midia e sondagem).

---

## 2. Camada 1: Frontend Desktop (React + TypeScript + Vite)

### 2.1 Instalacao de Dependencias
Na raiz do repositorio:
```bash
npm install
```

### 2.2 Servidor de Desenvolvimento
Para iniciar a interface web com Hot Module Replacement (HMR):
```bash
npm run dev
```
A aplicacao estara acessivel em `http://localhost:5173`.

### 2.3 Verificacao de Tipos TypeScript
Para verificar a conformidade estrita de tipagem sem gerar bundles:
```bash
./node_modules/.bin/tsc --noEmit
```

### 2.4 Build de Producao
Para gerar os artefatos estaticos otimizados:
```bash
npm run build
```

---

## 3. Camada 2: Engine de Midia em Rust (`crates/travelcut-media`)

O motor nativo de midia esta localizado no workspace Cargo em `crates/travelcut-media`.

### 3.1 Navegacao ate o Modulo
```bash
cd crates/travelcut-media
```

### 3.2 Compilacao do Crate
```bash
cargo build
```

Para gerar a versao otimizada de producao:
```bash
cargo build --release
```

### 3.3 Execucao dos Testes Automatizados
O crate possui 6 suites de testes de integracao cobrindo 23 cenarios essenciais:
```bash
cargo test
```

As suites incluem:
- `cache_key_tests.rs`: Determinismo e unicidade de chaves SHA-256.
- `inventory_tests.rs`: Classificacao de arquivos de video/audio e filtragem de arquivos de sistema.
- `models_tests.rs`: Serializacao e desserializacao de configuracoes, metadados e waveforms.
- `probe_stream_tests.rs`: Parsing de framerate fracionario, perfis HDR, rotacao e proporcoes.
- `queue_tests.rs`: Fila assincrona com controle estrito de concorrencia e cancelamento de tarefas.
- `transcode_tests.rs`: Selecao automatica de encoder de hardware (NVENC/QuickSync/VAAPI) e perfis proxy.

---

## 4. Camada 3: Motor de IA e Transcricao (`research/transcription`)

O modulo de pesquisa e desenvolvimento de transcricao, VAD e alinhamento de palavras funciona de forma independente ou acoplado via CLI.

### 4.1 Instalacao de Dependencias Python
```bash
pip install -r research/transcription/requirements.txt
```
*(Para execucao basica e testes de mock, apenas NumPy e Pytest sao necessarios).*

### 4.2 Execucao da Suite de Testes Automatizados
O modulo possui 43 testes unitarios e de integracao cobrindo todos os cenarios acusticos e contratuais:
```bash
python -m pytest research/transcription/tests
```

### 4.3 Execucao da CLI de Transcricao
Para transcrever um arquivo WAV local:
```bash
python -m research.transcription.cli --input caminho/audio.wav --format phase4 --video-category drone
```

Opcoes disponiveis:
- `--input, -i`: Caminho do arquivo de audio de entrada (.wav).
- `--output, -o`: Caminho do arquivo de saida (padrao: stdout).
- `--engine`: Backend ASR (`auto`, `mock`, `faster-whisper`).
- `--model`: Tamanho do modelo Whisper (`tiny`, `base`, `small`, `medium`, `large-v3`).
- `--language`: Codigo do idioma (`pt`, `en`, etc.).
- `--format`: Formato de serializacao (`json`, `phase4`, `txt`, `srt`).
- `--video-category`: Dica de contexto de viagem (`scenery`, `drone`, `waterfall`, `road`, `speech`).
- `--bench`: Exibe resumo de benchmarking no stderr.

### 4.4 Execucao dos Benchmarks de Desempenho
Para rodar a suite de benchmarking contra os 15 cenarios acusticos:
```bash
python -m research.transcription.benchmark
```
O benchmark gera metricas de Real-Time Factor (RTF), vazao de palavras por segundo e tempo de resposta, produzindo relatorios em JSON e Markdown.
