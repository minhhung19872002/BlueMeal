import { type ReactNode, useEffect, useMemo } from 'react';
import { App as AntApp, ConfigProvider, theme as antTheme } from 'antd';
import viVN from 'antd/locale/vi_VN';
import enUS from 'antd/locale/en_US';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useUi } from '@/shared/hooks/useUiStore';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false },
    mutations: { retry: 0 },
  },
});

/** CSP nonce injected by nginx into <meta name="csp-nonce"> (SPEC §10.5): antd CSS-in-JS style tags carry it. */
function cspNonce(): string | undefined {
  const v = document.querySelector('meta[name="csp-nonce"]')?.getAttribute('content');
  return v && v !== '__CSP_NONCE__' ? v : undefined;
}

export const BRAND_GREEN = '#3f8f5a';

export function AppProviders({ children }: { children: ReactNode }) {
  const { i18n } = useTranslation();
  const mode = useUi((s) => s.theme);
  const nonce = useMemo(cspNonce, []);
  // CSS variables of the design (app/layout/layout.css) follow the same theme; sidebar light, comfortable density (design defaults).
  useEffect(() => {
    const de = document.documentElement;
    de.dataset.theme = mode;
    de.dataset.sb = 'light';
    de.dataset.density = 'comfortable';
  }, [mode]);
  // Tokens from DESIGN_HANDOFF.md §1 ("BlueMeal Layout.dc.html").
  const themeConfig = useMemo(() => {
    const dark = mode === 'dark';
    return {
      algorithm: dark ? antTheme.darkAlgorithm : antTheme.defaultAlgorithm,
      token: {
        colorPrimary: '#2f7a49',
        colorInfo: '#2f7d8c',
        colorSuccess: '#3f8f5a',
        colorWarning: '#d99a1e',
        colorError: '#c8463d',
        colorBgLayout: dark ? '#0b110d' : '#f4f6f3',
        colorBgContainer: dark ? '#141a16' : '#ffffff',
        colorBgElevated: dark ? '#19211c' : '#ffffff',
        colorFillAlter: dark ? '#19211c' : '#f7f9f6',
        colorBorderSecondary: dark ? '#27322b' : '#e2e7e1',
        colorText: dark ? '#e8efe9' : '#14201a',
        colorTextSecondary: dark ? '#a9b6ad' : '#55635a',
        colorTextTertiary: dark ? '#8b998f' : '#69766d',
        colorLink: dark ? '#7cc796' : '#2a7144',
        colorPrimaryBg: dark ? '#1f3326' : '#e8f3eb',
        borderRadius: 8,
        borderRadiusLG: 12,
        controlHeight: 38,
        fontFamily: "'Be Vietnam Pro', 'Segoe UI', sans-serif",
      },
      components: {
        Card: { boxShadowTertiary: 'none' },
        Table: { headerBg: dark ? '#19211c' : '#f7f9f6', rowSelectedBg: dark ? '#1f3326' : '#e8f3eb' },
      },
    };
  }, [mode]);

  return (
    <ConfigProvider locale={i18n.language === 'en' ? enUS : viVN} theme={themeConfig} csp={nonce ? { nonce } : undefined} componentSize="middle">
      <AntApp>
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
      </AntApp>
    </ConfigProvider>
  );
}
