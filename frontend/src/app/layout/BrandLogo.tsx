import { useTranslation } from 'react-i18next';
import { BRAND_GREEN } from '@/app/providers';

export function BrandLogo({ compact = false, size = 32 }: { compact?: boolean; size?: number }) {
  const { t } = useTranslation();
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, fontWeight: 700, fontSize: 18, color: BRAND_GREEN, whiteSpace: 'nowrap' }}>
      <svg width={size} height={size} viewBox="0 0 64 64" aria-hidden="true">
        <circle cx="32" cy="32" r="30" fill={BRAND_GREEN} />
        <path d="M18 34c0-8 6-14 14-14s14 6 14 14H18z" fill="#fff" />
        <rect x="14" y="36" width="36" height="4" rx="2" fill="#fff" />
        <path d="M38 14c4 0 8 3 8 8-5 0-8-3-8-8z" fill="#bfe3c8" />
      </svg>
      {!compact && <span>{t('appName')}</span>}
    </span>
  );
}
