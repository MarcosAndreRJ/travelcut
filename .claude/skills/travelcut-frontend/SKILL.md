---
name: travelcut-frontend
description: "Guias React/TypeScript e revisão fiel dos seis mockups TravelCut."
---

# Procedimento


1. Ver `docs/08-INTERFACE-E-MOCKUPS.md` e imagens em `referencias/mockups/` (seis telas).
2. Criar design tokens, sidebar única, componentes (stat-card, media-card, scene-player, transcript, waveform, review-queue, timeline, integration-status).
3. Mocks TypeScript tipados na fase visual e **DEMO visível**. Nada de "Conectado" fake; estados reais vêm da ponte C#.
4. Ações de UI devem estar conectadas ou explicitamente desabilitadas até implementação do backend; tratar carregamento/erro/vazio/sem mídia.
5. Testar responsividade, escala 125–200%, teclado, temas, foco e visual de 6 telas. Não duplicar UI em WPF.
