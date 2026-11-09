import { create } from 'zustand';

type Theme = 'light' | 'dark';
const THEME_KEY = 'bm.theme';

function initialTheme(): Theme {
  try {
    const v = localStorage.getItem(THEME_KEY);
    if (v === 'light' || v === 'dark') return v;
  } catch {
    /* ignore */
  }
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

interface UiState {
  theme: Theme;
  collapsed: boolean;
  setTheme: (t: Theme) => void;
  toggleCollapsed: () => void;
  setCollapsed: (c: boolean) => void;
}

/** Per-viewer UI conveniences (theme, sidebar) — persisted in localStorage best-effort. */
export const useUi = create<UiState>((set) => ({
  theme: initialTheme(),
  collapsed: false,
  setTheme: (theme) => {
    try {
      localStorage.setItem(THEME_KEY, theme);
    } catch {
      /* ignore */
    }
    set({ theme });
  },
  toggleCollapsed: () => set((s) => ({ collapsed: !s.collapsed })),
  setCollapsed: (collapsed) => set({ collapsed }),
}));
