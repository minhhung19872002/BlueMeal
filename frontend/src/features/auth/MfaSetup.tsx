import { useState } from 'react';
import { Alert, Button, Checkbox, Form, Input, List, Space, Spin, Typography } from 'antd';
import { CopyOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';

/** TOTP enrolment: QR + secret, verify code, then show the 10 recovery codes once (SPEC §10.1). */
export function MfaSetup({ onDone }: { onDone: () => void }) {
  const { t } = useTranslation('auth');
  const [codes, setCodes] = useState<string[] | null>(null);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const { data, isLoading } = useQuery({
    queryKey: ['mfa-setup'],
    queryFn: async () => (await http.post<{ secret: string; otpAuthUri: string; qrPng: string }>('/profile/mfa/setup')).data,
    staleTime: Infinity,
    gcTime: 0,
  });

  if (codes)
    return (
      <Space direction="vertical" style={{ width: '100%' }}>
        <Typography.Title level={5}>{t('mfa.recoveryTitle')}</Typography.Title>
        <Typography.Paragraph type="secondary">{t('mfa.recoveryIntro')}</Typography.Paragraph>
        <List
          bordered
          size="small"
          grid={{ gutter: 8, column: 2 }}
          dataSource={codes}
          renderItem={(c) => (
            <List.Item>
              <Typography.Text code copyable data-testid="recovery-code">{c}</Typography.Text>
            </List.Item>
          )}
        />
        <Button icon={<CopyOutlined />} onClick={() => void navigator.clipboard?.writeText(codes.join('\n'))}>
          {t('common:actions.copy')}
        </Button>
        <Checkbox checked={saved} onChange={(e) => setSaved(e.target.checked)} data-testid="recovery-saved">
          {t('mfa.recoverySaved')}
        </Checkbox>
        <Button type="primary" disabled={!saved} onClick={onDone} block>
          {t('common:actions.next')}
        </Button>
      </Space>
    );

  return (
    <div>
      <Typography.Paragraph>{t('mfa.setupIntro')}</Typography.Paragraph>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
      {isLoading || !data ? (
        <Spin />
      ) : (
        <Space direction="vertical" align="center" style={{ width: '100%' }}>
          <img src={data.qrPng} alt="QR" width={200} height={200} style={{ background: '#fff', padding: 8, borderRadius: 8 }} />
          <Typography.Text type="secondary">{t('mfa.secret')}</Typography.Text>
          <Typography.Text code copyable data-testid="mfa-secret">{data.secret}</Typography.Text>
        </Space>
      )}
      <Form
        layout="vertical"
        style={{ marginTop: 16 }}
        onFinish={async (v: { code: string }) => {
          setLoading(true);
          setError(null);
          try {
            const r = await http.post<{ recoveryCodes: string[] }>('/profile/mfa/enable', { code: v.code });
            setCodes(r.data.recoveryCodes);
          } catch (e) {
            setError(problemMessage(e));
          } finally {
            setLoading(false);
          }
        }}
      >
        <Form.Item name="code" label={t('mfa.code')} rules={[{ required: true, len: 6, message: t('common:validation.requiredShort') }]}>
          <Input inputMode="numeric" maxLength={6} size="large" autoComplete="one-time-code" data-testid="mfa-setup-code" />
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={loading} block size="large">
          {t('mfa.enable')}
        </Button>
      </Form>
    </div>
  );
}
