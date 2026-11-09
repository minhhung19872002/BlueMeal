import { Card, Col, Row, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/shared/auth/authStore';

/** Role-based home dashboard. Widgets are completed in Phase P6. */
export default function DashboardPage() {
  const { t } = useTranslation('dashboard');
  const user = useAuth((s) => s.user);
  return (
    <div>
      <Typography.Title level={4}>{t('welcome', { name: user?.fullName })}</Typography.Title>
      <Row gutter={[16, 16]}>
        <Col xs={24} md={12}>
          <Card>{t('intro')}</Card>
        </Col>
      </Row>
    </div>
  );
}
