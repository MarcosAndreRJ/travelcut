# Visão do produto, usuários e escopo

## Problema

O criador grava com celular/GoPro (eventualmente drone) dezenas de arquivos de viagens, passeios e conversas. Preparar manualmente cronologia, transcrições, retirada de tentativas falhas, seleção de takes e montagem inicial consome tempo e atrapalha a revisão editorial. O projeto deve reduzir tarefas repetitivas **sem substituir o julgamento humano nem o DaVinci Resolve**.

## Objetivo do produto

Aplicativo Windows local que recebe um projeto nas quatro pastas padrão, inventaria as mídias, sugere narrativa e cortes auditáveis, permite revisão, materializa uma timeline editável no DaVinci Studio quando disponível e gerencia revisões, prévias, históricos e exportação.

## Persona e estilo editorial

- Vídeos de viagens podem começar com fala ou take de abertura, com progressão coerente da história.
- Conversas entre autor e Elaine, humor espontâneo, ironias; preservá-los em vez de classificá-los como erros.
- Imagens estáticas de fala: zoom discreto vinculado à entonação, sem corte excessivo de rostos nem perda de qualidade.
- Estradas, passeios, lojas, produtos, drone e outros B-roll: escolher melhores fragmentos, sugerir 2×/4× quando cabível, trilha em volumes adequados.
- Remover silêncios longos, “ééé”, recomeços; manter respiração, significado e sincronismo.
- Fade/dissolve somente quando fizer sentido; cor por cena; ruído de vento e legendas difíceis podem ficar para a segunda edição.

## Escopo do MVP

1. Abrir e reconhecer projeto com `1-Bruto`, `2-Assets`, `3-Trabalho`, `4-Final`, respeitando permissões.
2. Importar e indexar vídeos, arquivos de áudio/imagem; extrair metadados, miniaturas, waveform e datas com evidência/confiança.
3. Transcrever fala usando worker Whisper existente quando disponível; fallback de teste por fixtures, não outro modelo silenciosamente.
4. Detectar silêncios e trechos de fala candidatos a limpeza; classificar cenas iniciais e produzir sugestões editáveis.
5. Exibir biblioteca, progresso real, transcrição, revisão de cortes e timeline **planejada**.
6. Armazenar decisões, versões e evidências de origem; gerar plano de edição serializável.
7. Criar projeto/timeline básica **quando a ponte DaVinci real estiver homologada no host**; caso contrário, usar mock explicitamente identificado.
8. Exportar relatório, prévia técnica quando disponível, artefatos de homologação e logs recuperáveis.

## Versões seguintes

- Ajustes avançados de cor, estabilização e processamento inteligente de vento.
- Renderização de animações HyperFrames, zoom baseado em detecção de rosto/entonação e efeitos sofisticados.
- A/B preview sincronizado de alta precisão e variações complexas da timeline.
- Aprendizado de preferências a partir de aprovações, sem inferir permissão para enviar mídias a terceiros.

## Não objetivos

- Não recriar um NLE completo ou substituir a interface de edição do DaVinci.
- Não hospedar/transcrever em serviços de nuvem por padrão.
- Não publicar vídeos automaticamente ou trabalhar sem autorização em projetos externos.
- Não automatizar decisões ambíguas como exclusões irreversíveis.

## Sucesso do MVP

O usuário escolhe uma pasta de viagem, executa análise local, inspeciona candidatos a cortes, aprova/rejeita, produz plano rastreável, recebe timeline simulada válida na VM e, após homologação host, uma timeline **real e editável** no Resolve; consegue retomar sessão sem reprocessar mídias inalteradas.
