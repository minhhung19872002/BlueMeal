import type { ReactNode } from 'react';
import { Card, Space, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { BrandLogo } from '@/app/layout/BrandLogo';
import { LanguageSwitch, ThemeSwitch } from '@/app/layout/HeaderRight';

/** Centered card layout shared by login / forgot / reset / first-login pages (responsive ≥ 360px). */
export function AuthShell({ title, subtitle, children, width = 420 }: { title: string; subtitle?: string; children: ReactNode; width?: number }) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 16,
        background: `linear-gradient(160deg, ${token.colorPrimaryBg} 0%, ${token.colorBgLayout} 60%)`,
      }}
    >
      <div style={{ position: 'absolute', top: 12, right: 16 }}>
        <Space>
          <LanguageSwitch />
          <ThemeSwitch />
        </Space>
      </div>
      <div style={{ marginBottom: 20, textAlign: 'center' }}>
        <BrandLogo size={44} />
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('appTagline')}
        </Typography.Paragraph>
      </div>
      <Card style={{ width: '100%', maxWidth: width }} styles={{ body: { padding: 24 } }}>
        <Typography.Title level={4} style={{ marginTop: 0 }}>
          {title}
        </Typography.Title>
        {subtitle && <Typography.Paragraph type="secondary">{subtitle}</Typography.Paragraph>}
        {children}
      </Card>
    </div>
  );
}
