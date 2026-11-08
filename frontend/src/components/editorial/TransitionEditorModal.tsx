import React, { useState, useEffect } from 'react';
import { AlertTriangle, Check, ShieldAlert, Sparkles, X } from 'lucide-react';
import { ipc } from '../../services/ipcBridge';

export interface TransitionEditorModalProps {
  projectId: string;
  mediaIdA: string;
  mediaIdB: string;
  isOpen: boolean;
  onClose: () => void;
  onCreated?: (transi

...[conteúdo de entrada truncado (6642 chars omitidos pelo Gateway)]...

            onClick={handleCreate}
            disabled={saving || !validateResult?.isValid}
            className="px-4 py-1.5 rounded text-xs font-semibold bg-cyan-600 hover:bg-cyan-500 text-white disabled:opacity-50"
          >
            {saving ? 'Aplicando...' : 'Aplicar Transição'}
          </button>
        </div>
      </div>
    </div>
  );
};
