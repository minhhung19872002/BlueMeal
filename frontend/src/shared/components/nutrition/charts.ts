import { useUi } from '@/shared/hooks/useUiStore';

/**
 * Chart colors (dataviz reference palette, validated with validate_palette.js for light/dark surfaces).
 * Categorical slots are assigned in fixed order: 1 = protein, 2 = lipid, 3 = glucid.
 * Status colors are reserved for states and always ship with an icon + label.
 */
export const CHART = {
  light: { surface: '#fcfcfb', text: '#0b0b0b', textSecondary: '#52514e', grid: '#e7e6e2', series: ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#6250d6', '#e34948'] },
  dark: { surface: '#1a1a19', text: '#f0efec', textSecondary: '#c3c2b7', grid: '#383835', series: ['#3987e5', '#d95926', '#199e70', '#c98500', '#d55181', '#008300', '#9085e9', '#e66767'] },
  status: { good: '#0ca30c', warning: '#fab219', serious: '#ec835a', critical: '#d03b3b' },
} as const;

export function useChartTheme() {
  const mode = useUi((s) => s.theme);
  return mode === 'dark' ? CHART.dark : CHART.light;
}
