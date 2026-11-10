import type { ReactNode } from 'react';
import { ConfigProvider } from 'antd';
import { useTranslation } from 'react-i18next';
import { LogoMark } from '@/app/layout/navIcons';
import { LanguageSwitch, ThemeSwitch } from '@/app/layout/HeaderRight';

/**
 * Two-column sign-in layout from "BlueMeal Layout.dc.html" (stacks on phones): dark brand panel + form (max 380px,
 * inputs 44px, button 46px). Shared by login / MFA / forgot / reset / first-login pages.
 */
export function AuthShell({ title, subtitle, children, width = 380, hint }: { title: string; subtitle?: string; children: ReactNode; width?: number; hint?: string }) {
  const { t } = useTranslation();
  return (
    <div className="bm-auth">
      <div className="bm-auth-hero">
        <div className="bm-auth-ring" />
        <div className="bm-auth-brand">
          <LogoMark size={36} />
          <span>{t('appName')}</span>
        </div>
        <div className="bm-auth-tagline">{t('appTagline')}</div>
        <div className="bm-auth-org">{t('orgFull')}</div>
      </div>
      <div className="bm-auth-side">
        <div className="bm-auth-tools">
          <LanguageSwitch />
          <ThemeSwitch />
        </div>
        <ConfigProvider theme={{ components: { Input: { controlHeight: 44, controlHeightLG: 44 }, InputNumber: { controlHeight: 44, controlHeightLG: 44 }, Button: { controlHeight: 46, controlHeightLG: 46, fontWeight: 600 } } }}>
          <div className="bm-auth-form" style={{ maxWidth: width }}>
            <div>
              <h1 className="bm-auth-title">{title}</h1>
              {subtitle && <div className="bm-auth-sub">{subtitle}</div>}
            </div>
            <div>{children}</div>
            {hint && <div className="bm-auth-hint">{hint}</div>}
          </div>
        </ConfigProvider>
      </div>
    </div>
  );
}
