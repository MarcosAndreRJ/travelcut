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
          bg: '#0a0c10',
          panel: '#101319',
          card: '#161a22',
          cardHover: '#1c222c',
          border: '#232936',
          borderLight: '#323b4e',
          accent: '#6366f1',
          accentHover: '#4f46e5',
          cyan: '#06b6d4',
          emerald: '#10b981',
          amber: '#f59e0b',
          rose: '#f43f5e',
          purple: '#a855f7',
          text: '#f1f5f9',
          muted: '#94a3b8',
          subtle: '#64748b'
        }
      },
      fontFamily: {
        mono: ['JetBrains Mono', 'Fira Code', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Monaco', 'Consolas', 'monospace'],
        sans: ['Plus Jakarta Sans', 'Inter', 'system-ui', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
      },
      boxShadow: {
        'glow-indigo': '0 0 20px -3px rgba(99, 102, 241, 0.35)',
        'glow-cyan': '0 0 20px -3px rgba(6, 182, 212, 0.35)',
        'glow-emerald': '0 0 20px -3px rgba(16, 185, 129, 0.35)',
        'panel': '0 4px 20px -2px rgba(0, 0, 0, 0.5)',
      }
    },
  },
  plugins: [],
}
