import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

/** Versão única do app: vem do pipeline de build (TRAVELCUT_VERSION). Sem variável, o build é "dev". */
const APP_VERSION: string = process.env.TRAVELCUT_VERSION ?? 'dev';

/**
 * CSP só no build de produção. O dev server do Vite usa scripts inline (React Refresh) e websocket de HMR,
 * que essa política bloquearia; por isso a meta é injetada apenas em `vite build`.
 */
const PRODUCTION_CSP =
  "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
  "img-src 'self' data: https://media.travelcut.studio; media-src https://media.travelcut.studio blob:; " +
  "connect-src 'self'; font-src 'self' data:";

function productionCsp(): Plugin {
  return {
    name: 'travelcut-production-csp',
    apply: 'build',
    transformIndexHtml: {
      order: 'post',
      handler(html: string): string {
        const meta = `<meta http-equiv="Content-Security-Policy" content="${PRODUCTION_CSP}" />`;
        return html.replace('</head>', `    ${meta}\n  </head>`);
      }
    }
  };
}

export default defineConfig({
  plugins: [react(), productionCsp()],
  define: {
    __APP_VERSION__: JSON.stringify(APP_VERSION)
  },
  server: {
    port: 5173,
    strictPort: true
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true
  }
});
