# FASE 02 — Solução compilável .NET e ponte WPF/WebView2


**Objetivo:** criar a fundação real do TravelCut em .NET 10, sem funcionalidades audiovisual completas.

### Atividades

1. Criar solução com `Domain`, `Application`, `Contracts`, `Infrastructure`, `Desktop` WPF/WebView2 e testes, seguindo `docs/03`. Separar frontend React/Vite em `src/TravelCut.Frontend`; configurar build de assets para host.
2. Implementar DI, configuração por ambiente, startup, tratamento de erros, logging `ILogger`, mensagem WebView2↔C# **tipada** e handshake mínimo com requestId/versão. Evitar navegação remota insegura.
3. Implementar SQLite via EF Core (migration inicial de entidades mínimas); banco em diretório local seguro e configuração de data dir. Repositório de projeto e um caso de uso puro que possa ser acionado por UI e testes.
4. Validar `dotnet restore/build/test`, `npm ci/build`, smoke test da janela WPF quando possível e comando da bridge. Rodar Sonnet na revisão de separação em camadas; Haiku `dotnet-engineer` e `frontend-engineer` em escopos distintos.
5. Se já existe código Rust útil, manter até equivalência dos testes; não fazer remoção antes do gate de migração.

### Aceite

Aplicativo abre (quando UI disponível), frontend recebe resposta verdadeira do backend, persistência de exemplo funciona, testes passam, CI local documentado e nenhum dado simulado é apresentado como integração real.

## Ritual obrigatório desta fase (não omitir)

1. Ler `CLAUDE.md`, fase, contratos relacionados, `progresso/STATE.md`, checklist e último log. Inspecionar arquivos antes de alterar.
2. Quebrar a fase em microtarefas verificáveis e registrar itens abertos no checklist **antes** de executar. `STATE.md` deve apontar esta fase.
3. **Orquestrador Opus** delega ao Haiku responsável; para problemas complexos/alto risco chama `senior-analyst` Sonnet; integra e verifica. Máximo 2 tarefas pesadas paralelas na VM e sem arquivos simultâneos.
4. Após cada unidade entregue/testada, **escrever imediatamente** em `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/TESTADO ou FALHOU, caminhos e resultados. Atualizar checklist quando comprovado. Não deixar tudo para o final.
5. Executar testes pertinentes (ou registrar impossibilidade real), solicitar auditoria `qa-auditor`, corrigir falhas bloqueantes, revisar diffs/contratos.
6. Atualizar `progresso/STATE.md` com status, último checkpoint, riscos e próxima ação; commits locais só conforme política. Não realizar push remoto sem autorização.
7. **Se todos os critérios passaram, inicie automaticamente** ``prompts/03-INTERFACE-E-MOCKUPS.md`` na **mesma sessão ativa**, sem perguntar novamente. Se depende de autorização/teste manual host ou houver bloqueio impeditivo, pare e deixe retomada clara. Nunca alegue execução futura/segundo plano.
