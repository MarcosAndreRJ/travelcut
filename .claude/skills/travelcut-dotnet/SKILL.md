---
name: travelcut-dotnet
description: "Padrões de implementação C#/.NET 10, WPF WebView2, EF Core e testes."
---

# Procedimento


1. Seguir `docs/03-ARQUITETURA-DOTNET.md` e `docs/04-MODELO-DE-DADOS.md`.
2. Core sem dependências de desktop; usar DI, `ILogger`, CancellationToken, `System.Text.Json` contratos versionados, EF Core SQLite migrações.
3. Assinaturas assíncronas para IO; recursos `IDisposable`, `ProcessStartInfo.ArgumentList`, zero execução de shell com interpolação.
4. Adicionar testes xUnit para regras e falhas; validar build e `dotnet test` quando ambiente permitir; registrar output real.
5. Não declarar .NET 10 oficialmente compatível com VM Windows 10 Pro 22H2; homologação final exige host suportado.
