import React from 'react';
import {
  AlertCircle,
  Copy,
  Magnet,
  Redo2,
  RefreshCw,
  Save,
  Scissors,
  Trash2,
  Undo2
} from 'lucide-react';
import type { EditPlanDto } from '../../types/ipc';
import type { useTimelineEditor } from './useTimelineEditor';

interface TimelineEditorToolbarProps {
  editor: ReturnType<typeof useTimelineEditor>;
  activePlan: EditPlanDto | null;
}

export const TimelineEditorToolbar: React.FC<TimelineEditorToolbarProps> = ({
  editor,
  activePlan
}) => {
  if (!activePlan) return null;

  return (
    <div className="flex flex-col gap-2">
      {/* Conflitos de Reconciliação */}
      {editor.conflicts.length > 0 && (
        <div
          data-testid="reconcile-conflicts-banner"
          className="rounded-lg border border-amber-500/50 bg-amber-950/40 p-3 text-xs text-amber-200 flex flex-col gap-1.5"
        >
          <div className="flex items-center gap-2 font-medium text-amber-300">
            <AlertCircle className="w-4 h-4 text-amber-400 shrink-0" />
            <span>Conflitos de Reconciliação Detectados (F14.1/F14.2)</span>
          </div>
          <p className="text-amber-200/90 text-[11px]">
            O plano automático atualizou cortes que entram em conflito com suas edições manuais.
            Revise as divergências abaixo:
          </p>
          <div className="space-y-1 mt-1 max-h-32 overflow-y-auto">
            {editor.conflicts.map((conflict) => (
              <div
                key={conflict.conflictId}
                className="bg-black/40 rounded p-1.5 border border-amber-600/30 text-[11px] flex justify-between items-center"
              >
                <span>{conflict.message}</span>
                {conflict.suggestedResolution && (
                  <span className="text-slate-400 italic text-[10px] ml-2">
                    {conflict.suggestedResolution}
                  </span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Barra de Ferramentas Principal */}
      <div
        data-testid="timeline-editor-toolbar"
        className="flex flex-wrap items-center justify-between gap-2 p-2 rounded-xl border border-slate-800 bg-[#080d19]/90 text-xs"
      >
        <div className="flex items-center gap-1.5">
          {/* Desfazer */}
          <button
            type="button"
            onClick={editor.undo}
            disabled={!editor.canUndo}
            title="Desfazer (Ctrl+Z)"
            aria-label="Desfazer ação"
            className="flex items-center gap-1 px-2.5 py-1.5 rounded-lg border border-slate-800 bg-slate-900 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:pointer-events-none transition-colors"
          >
            <Undo2 className="w-3.5 h-3.5" />
            <span>Desfazer</span>
          </button>

          {/* Refazer */}
          <button
            type="button"
            onClick={editor.redo}
            disabled={!editor.canRedo}
            title="Refazer (Ctrl+Y)"
            aria-label="Refazer ação"
            className="flex items-center gap-1 px-2.5 py-1.5 rounded-lg border border-slate-800 bg-slate-900 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:pointer-events-none transition-colors"
          >
            <Redo2 className="w-3.5 h-3.5" />
            <span>Refazer</span>
          </button>

          <div className="h-4 w-px bg-slate-800 mx-1" />

          {/* Dividir no Playhead */}
          <button
            type="button"
            onClick={() => editor.splitClipAtPlayhead()}
            disabled={!editor.selectedClipId}
            title="Dividir clipe selecionado no cursor de reprodução (Tecla S)"
            aria-label="Dividir clipe no playhead"
            className="flex items-center gap-1 px-2.5 py-1.5 rounded-lg border border-slate-800 bg-slate-900 text-slate-300 hover:bg-slate-800 hover:text-cyan-300 disabled:opacity-40 disabled:pointer-events-none transition-colors"
          >
            <Scissors className="w-3.5 h-3.5" />
            <span>Dividir (S)</span>
          </button>

          {/* Duplicar */}
          <button
            type="button"
            onClick={() => editor.selectedClipId && editor.duplicateClip(editor.selectedClipId)}
            disabled={!editor.selectedClipId}
            title="Duplicar clipe selecionado (Ctrl+D)"
            aria-label="Duplicar clipe"
            className="flex items-center gap-1 px-2.5 py-1.5 rounded-lg border border-slate-800 bg-slate-900 text-slate-300 hover:bg-slate-800 hover:text-cyan-300 disabled:opacity-40 disabled:pointer-events-none transition-colors"
          >
            <Copy className="w-3.5 h-3.5" />
            <span>Duplicar</span>
          </button>

          {/* Excluir */}
          <button
            type="button"
            onClick={() => editor.selectedClipId && editor.deleteClip(editor.selectedClipId)}
            disabled={!editor.selectedClipId}
            title="Excluir clipe selecionado (Delete)"
            aria-label="Excluir clipe"
            className="flex items-center gap-1 px-2.5 py-1.5 rounded-lg border border-slate-800 bg-slate-900 text-rose-300 hover:bg-rose-950/40 hover:border-rose-800/50 disabled:opacity-40 disabled:pointer-events-none transition-colors"
          >
            <Trash2 className="w-3.5 h-3.5" />
            <span>Excluir</span>
          </button>

          <div className="h-4 w-px bg-slate-800 mx-1" />

          {/* Alinhamento Magnético (Snapping) */}
          <button
            type="button"
            onClick={() => editor.setSnappingEnabled(!editor.snappingEnabled)}
            title={
              editor.snappingEnabled
                ? 'Alinhamento magnético ativado (Snap) — clique para desativar'
                : 'Alinhamento magnético desativado — clique para ativar'
            }
            aria-label="Alternar alinhamento magnético"
            aria-pressed={editor.snappingEnabled}
            className={`flex items-center gap-1 px-2.5 py-1.5 rounded-lg border transition-colors ${
              editor.snappingEnabled
                ? 'border-cyan-500/50 bg-cyan-950/60 text-cyan-300'
                : 'border-slate-800 bg-slate-900 text-slate-400 hover:text-slate-300'
            }`}
          >
            <Magnet className="w-3.5 h-3.5" />
            <span>Alinhar ({editor.snappingEnabled ? 'Ligado' : 'Desligado'})</span>
          </button>
        </div>

        <div className="flex items-center gap-2">
          {/* Indicador de Alterações Pendentes / Salvar */}
          <button
            type="button"
            onClick={() => editor.saveRevision()}
            disabled={!editor.isDirty || editor.isSaving}
            title="Gravar revisão manual no plano editorial (F14.2)"
            aria-label="Salvar revisão manual"
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg font-medium border transition-colors ${
              editor.isDirty
                ? 'bg-cyan-600 hover:bg-cyan-500 text-white border-cyan-400 shadow-[0_0_12px_rgba(6,182,212,0.4)]'
                : 'bg-slate-900 border-slate-800 text-slate-400 disabled:opacity-50'
            }`}
          >
            {editor.isSaving ? (
              <RefreshCw className="w-3.5 h-3.5 animate-spin" />
            ) : (
              <Save className="w-3.5 h-3.5" />
            )}
            <span>{editor.isSaving ? 'Gravando…' : editor.isDirty ? 'Salvar Edição *' : 'Edição Salva'}</span>
          </button>
        </div>
      </div>
    </div>
  );
};
