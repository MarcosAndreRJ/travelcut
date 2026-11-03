# Guia de execução das fases

**Entrar por:** `README.md` na raiz, depois `CLAUDE.md` e `prompts/00-INICIAR-AQUI.md`.

As fases 01–11 são **ordens executáveis** para a sessão principal Opus, não apenas documentação. Uma vez iniciada a sequência, cada fase deverá criar microtarefas, delegar, implementar, compilar/testar, registrar log após cada unidade concluída, auditar, salvar checkpoint e **iniciar automaticamente a próxima fase enquanto a sessão estiver ativa**.

Não aguardar aprovação humana para escolhas menores e seguras, mas interromper diante de instalação global, destruição, falta de acesso, mudanças arquiteturais que anulam decisões aprovadas ou validação manual no host. A conclusão real do MVP requer a homologação host; uma sessão na VM não tem acesso ao DaVinci.

Antes de iniciar qualquer nova fase, verificar `progresso/STATE.md`, `progresso/CHECKLIST.md`, evidências e git diff. Não repetir fase concluída nem presumir que execução foi terminada se log não tem teste.
