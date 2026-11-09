import { type ReactNode, useMemo } from 'react';
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
  const themeConfig = useMemo(
    () => ({
      algorithm: mode === 'dark' ? antTheme.darkAlgorithm : antTheme.defaultAlgorithm,
      token: {
        colorPrimary: BRAND_GREEN,
        colorInfo: '#2f7d8c',
        colorSuccess: '#4c9f38',
        colorWarning: '#d99a1e',
        colorError: '#c8463d',
        borderRadius: 8,
        fontFamily: "'Segoe UI', Roboto, 'Helvetica Neue', Arial, 'Noto Sans', sans-serif",
      },
      components: {
        Layout: { siderBg: mode === 'dark' ? '#141a16' : '#ffffff', headerBg: mode === 'dark' ? '#141a16' : '#ffffff' },
        Menu: { itemSelectedBg: mode === 'dark' ? '#1f3326' : '#e8f3eb' },
      },
    }),
    [mode],
  );

  return (
    <ConfigProvider locale={i18n.language === 'en' ? enUS : viVN} theme={themeConfig} csp={nonce ? { nonce } : undefined} componentSize="middle">
      <AntApp>
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
      </AntApp>
    </ConfigProvider>
  );
}
