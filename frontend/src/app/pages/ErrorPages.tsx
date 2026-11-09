import { Button, Result } from 'antd';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import axios from 'axios';

function HomeButton() {
  const { t } = useTranslation();
  return (
    <Button type="primary" href="/">
      {t('error.goHome')}
    </Button>
  );
}

export function ForbiddenPage() {
  const { t } = useTranslation();
  return <Result status="403" title={t('error.forbiddenTitle')} subTitle={t('error.forbiddenText')} extra={<HomeButton />} />;
}

export function NotFoundPage() {
  const { t } = useTranslation();
  return <Result status="404" title={t('error.notFoundTitle')} subTitle={t('error.notFoundText')} extra={<HomeButton />} />;
}

export function ServerErrorPage() {
  const { t } = useTranslation();
  return <Result status="500" title={t('error.serverTitle')} subTitle={t('error.serverText')} extra={<HomeButton />} />;
}

export function MaintenancePage() {
  const { t, i18n } = useTranslation();
  const { data } = useQuery({
    queryKey: ['system-status'],
    queryFn: async () => (await axios.get('/api/v1/system/status')).data as { maintenance: boolean; maintenanceMessageVi?: string; maintenanceMessageEn?: string },
    refetchInterval: 30_000,
  });
  const msg = i18n.language === 'en' ? data?.maintenanceMessageEn : data?.maintenanceMessageVi;
  return (
    <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', padding: 16 }}>
      <Result status="warning" title={t('error.maintenanceTitle')} subTitle={msg || t('error.maintenanceText')} extra={data && !data.maintenance ? <HomeButton /> : null} />
    </div>
  );
}

/** Router-level error boundary (unexpected render errors). */
export function RouteErrorPage() {
  return (
    <div style={{ padding: 16 }}>
      <ServerErrorPage />
    </div>
  );
}
