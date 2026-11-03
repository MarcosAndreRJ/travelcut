# Interface, navegação, design system e mockups

## Referências visuais reais

Arquivos em `referencias/mockups/`:

1. `01_Painel_Inicial.png` — hero do projeto, cards das 4 pastas, pipeline, ações rápidas, integração/status, últimas análises.
2. `02_Biblioteca_e_Transcricao.png` — grid de clipes, categorias, thumbnails, preview, transcrição por palavras, jobs e resumo.
3. `03_Planejamento_Timeline.png` — lista de mídia cronológica, player central, painéis de análise, timeline multitrack **planejada**.
4. `04_Revisao_de_Cortes.png` — contadores de cortes, comparação antes/depois, transcrição destacada, waveform, fila de revisão e decisões.
5. `05_Configuracoes_e_Integracoes.png` — testes independentes de DaVinci/Claude/Whisper/FFmpeg/HyperFrames, perfil de edição e regras de preservação.
6. `06_Visao_Geral_da_Montagem.png` — progresso do fluxo, grid de cenas, player de prévia/transcrição e esboço narrativo.

Os seis arquivos são referências **parcialmente divergentes**, não seis propostas de design system independentes. Priorizar consistência funcional sobre cópia literal de menus incompatíveis.

## Navegação consolidada

- **Início / Projeto atual:** diretório, cards 1–4, pipeline, ações e atividade.
- **Biblioteca:** grid/lista, filtros, preview, detalhes e metadados; transcrição como aba lateral.
- **Análise:** trabalhos de inventário, Whisper, silêncios, cenas e progresso real.
- **Planejamento:** ordem narrativa, sequência de mídia e timeline planejada.
- **Revisão de cortes:** fila, antes/depois, waveform, aprovação/rejeição/adiamento e relatórios.
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

Mostrar projeto aberto, pastas, pipeline e estado de integração; sem projeto → seletor e instruções. Sem DaVinci → banner discreto "Edição real depende do host"; análise permanece acessível.

### Biblioteca e transcrição

Filtros por cena, origem, tipo e erro; grid/miniatura, seleção clara, player com tempo/duração, transcrição com busca e salto para word timestamps. Sem transcrição → opção executar worker ou explicar impedimento.

### Planejamento

Lista de fontes com tempos, player, timeline com clips e faixas. Diferenciar `source time` vs `timeline time` nos rótulos. Nunca alegar que esta é a timeline do DaVinci quando não está sincronizada.

### Revisão

Comparador antes/depois (preview gerada ou proxy), texto com faixas marcadas, waveform indicativa, motivos, confiança qualitativa, aprovar/rejeitar/adiar e `undo`; sempre manter contexto da cena.

### Configurações

Testes de conectividade por ferramenta; a UI mostra mensagem e log do teste real, inclusive "não instalado na VM". Preferências persistem em SQLite/configuração, não apenas toggles visuais.

### Visão geral da montagem

Acompanhamento de jobs, thumbnails por cena e prévia de narrativa. Contadores provenientes do backend; não assumir número fixo de vídeos/cortes como nos mockups.

## Critérios de fidelidade visual

Comparar screenshots da implementação com seis imagens de referência em resoluções equivalentes. QA verifica hierarquia, alinhamento, contraste, redimensionamento, navegação, estados e responsividade — **não** testar apenas a home.
