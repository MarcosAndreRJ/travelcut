# Interface, navegação, design system e mockups

## Referências visuais reais

Arquivos em `referencias/mockups/`:

1. `01_Painel_Inicial.png` — hero do projeto, cards das 4 pastas, pipeline, ações rápidas, integração/status, últimas análises.
2. `02_Biblioteca_e_Transcricao.png` — grid de clipes, categorias, thumbnails, preview, transcrição por palavras, jobs e resumo.
3. `03_Planejamento_Timeline.png` — lista de mídia cronológica, player central, painéis de análise, timeline multitrack **planejada**.
4. `04_Revisao_de_Cortes.png` — referência histórica para comparação antes/depois, fila e decisões que passam para a Biblioteca.
5. `05_Configuracoes_e_Integracoes.png` — testes independentes de DaVinci/Claude/Whisper/FFmpeg/HyperFrames, perfil de edição e regras de preservação.
6. `06_Visao_Geral_da_Montagem.png` — progresso do fluxo, grid de cenas, player de prévia/transcrição e esboço narrativo.

Os seis arquivos são referências **parcialmente divergentes**, não seis propostas de design system independentes. Priorizar consistência funcional sobre cópia literal de menus incompatíveis.

## Navegação consolidada

Fluxo alvo do [workstream de edição por transcrição](workstreams/EDICAO-POR-TRANSCRICAO.md), entregue em incrementos.

- **Início / Projeto atual:** diretório, cards 1–4, pipeline, ações e atividade.
- **Biblioteca & Transcrição:** lista de clipes, preview e transcrição como editor principal. Sugestões e decisões ficam no painel lateral.
- **Análise:** trabalhos de inventário, Whisper, silêncios, cenas e progresso real.
- **Timeline:** ordem narrativa, faixas, prévia da montagem e painel de transcrição em ordem de edição.
- **Integração DaVinci:** status/capacidades, simulação/host, snapshot, aplicação de plano e mapeamentos.
- **Exportações:** prévias e artefatos aprovados.
- **Configurações:** integrações, perfil, desempenho, pastas e segurança.

Não duplicar "Análise", "Cortes inteligentes", "Mídia e análise" e "Transcrição" como se fossem funcionalidades isoladas sem relação. Contexto da tela deve determinar a aba, não múltiplas cópias do mesmo recurso.

## Visual

- Tema escuro, painéis em navy/grafite, acento azul/ciano para ações ativas; verde só para sucesso verdadeiro; laranja para revisão/pendência; vermelho para falha/exclusão proposta.
- Definir tokens de cor (`--bg`, `--surface`, `--surface-raised`, `--border`, `--text`, `--muted`, `--accent`, `--success`, `--warning`, `--danger`) e espaçamento (4/8/12/16/24/32).
- Tipografia consistente, sem fontes externas obrigatórias; tamanhos responsivos para 1366×768, 1536×1024 e 1920×1080; scroll em painéis densos e hierarquia clara.
- Ícones de uma mesma biblioteca (Lucide ou similar). Controles com rótulos, foco visível, atalhos, estados disabled/loading/error.
- Player e timeline são componentes reutilizáveis; múltiplas faixas e waveforms carregadas sob demanda, sem processar todo o vídeo no DOM.

## Regras para dados de UI

- **Mocks tipados**, só durante Fase 03, com indicador visível `DEMONSTRAÇÃO`; não simular conexões reais por badge verde em produção.
- `IntegrationStatus` por ferramenta; `JobProgress` com `completedUnits/totalUnits` (sem percentuais inventados); `ProjectSummary` lido do SQLite.
- Botão `Iniciar análise` deve realmente disparar caso de uso quando backend implementado; até lá fica rotulado "Demo" ou desabilitado, nunca parecer funcionar sem ação.
- Ações de aprovação aplicam versão do item e mostram conflito ao atualizar a timeline externa.

## Comportamento de tela

### Inicial

Mostrar projeto aberto, pastas, pipeline e estado de integração. Sem projeto, exibir seletor e instruções. Se o DaVinci estiver indisponível, explicar o efeito sobre o envio ao editor.

### Biblioteca & Transcrição

Manter a lista de clipes e o preview no topo. Apresentar a transcrição em blocos de fala com timestamp e seleção por palavra. Clicar em uma fala posiciona o player. Selecionar palavras ou marcar In/Out permite aplicar um corte direto. Exibir a ação **Desfazer** junto à confirmação do host e oferecer a comparação **Original / Editado**. Em clipe sem transcrição, explicar a disponibilidade da análise e manter a edição por In/Out quando houver mídia reproduzível.

### Sugestões na Biblioteca

Migrar a fila, motivo, confiança qualitativa, ajuste de limites, antes/depois, aprovar, rejeitar, adiar e recuperação de trechos. Selecionar uma sugestão foca o clipe e a fala ou intervalo correspondente. Após a migração, os atalhos que apontam para a T03 abrem esse contexto na Biblioteca e a tela separada sai da navegação.

### Timeline

Exibir faixas, waveform, player e painel de transcrição sincronizado com a agulha. A busca na montagem encontra falas na ordem em que são reproduzidas. A seleção identifica a ocorrência ativa, inclusive quando a fonte aparece mais de uma vez. Mostrar tempo de origem e tempo de montagem onde a distinção ajudar a ajustar o corte.

### Configurações

Testes de conectividade por ferramenta; a UI mostra mensagem e log do teste real, inclusive "não instalado na VM". Preferências persistem em SQLite/configuração, não apenas toggles visuais.

### Visão geral da montagem

Acompanhamento de jobs, thumbnails por cena e prévia de narrativa. Contadores provenientes do backend; não assumir número fixo de vídeos/cortes como nos mockups.

## Critérios de fidelidade visual

Comparar a implementação das telas ativas com as referências e com o fluxo alvo. QA verifica hierarquia, alinhamento, contraste, redimensionamento, navegação, estados, foco e atalhos. A antiga revisão serve de referência para as funções absorvidas pela Biblioteca.
