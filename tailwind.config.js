/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        studio: {
          // Deep desktop backgrounds
          bg: '#080b11',
          panel: '#0d121c',
          sidebar: '#0a0e17',
          card: '#121824',
          cardHover: '#182030',
          cardActive: '#1f293d',
          border: '#1e2638',
          borderLight: '#2b364e',
          borderFocus: '#3b82f6',

          // Primary brand & desktop actions (Blue / Turquoise)
          primary: '#2563eb',
          primaryHover: '#1d4ed8',
          primaryActive: '#1e40af',
          turquoise: '#06b6d4',
          turquoiseHover: '#0891b2',
          accent: '#6366f1',
          accentHover: '#4f46e5',
          cyan: '#06b6d4',

          // Semantic status colors
          success: '#10b981',
          successHover: '#059669',
          warning: '#f59e0b',
          warningHover: '#d97706',
          error: '#ef4444',
          errorHover: '#dc2626',
          danger: '#ef4444',
          info: '#38bdf8',

          // Legacy mappings for compatibility with existing views
          emerald: '#10b981',
          amber: '#f59e0b',
          rose: '#f43f5e',
          purple: '#a855f7',

          // Professional typography tokens
          text: '#f8fafc',
          textSecondary: '#cbd5e1',
          muted: '#94a3b8',
          subtle: '#64748b',
          disabled: '#475569'
        }
      },
      fontFamily: {
        mono: ['JetBrains Mono', 'Fira Code', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Monaco', 'Consolas', 'monospace'],
        sans: ['Plus Jakarta Sans', 'Inter', 'system-ui', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
      },
      boxShadow: {
        'glow-blue': '0 0 16px -2px rgba(37, 99, 235, 0.45)',
        'glow-cyan': '0 0 16px -2px rgba(6, 182, 212, 0.45)',
        'glow-emerald': '0 0 16px -2px rgba(16, 185, 129, 0.45)',
        'glow-indigo': '0 0 20px -3px rgba(99, 102, 241, 0.35)',
        'glow-amber': '0 0 16px -2px rgba(245, 158, 11, 0.45)',
        'panel': '0 4px 20px -2px rgba(0, 0, 0, 0.5)',
        'elevated': '0 10px 30px -10px rgba(0, 0, 0, 0.7)',
        'inner-light': 'inset 0 1px 0 0 rgba(255, 255, 255, 0.05)',
      }
    },
  },
  plugins: [],
}
