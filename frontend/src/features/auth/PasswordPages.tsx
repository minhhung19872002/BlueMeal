import { useState } from 'react';
import { Alert, Button, Form, Input, Result } from 'antd';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { zodRule, zs } from '@/shared/utils/validation';
import { AuthShell } from './AuthShell';

export function ForgotPasswordPage() {
  const { t } = useTranslation('auth');
  const [sent, setSent] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const { data: options } = useQuery({ queryKey: ['auth-options'], queryFn: async () => (await http.get<{ smtpConfigured: boolean }>('/auth/options')).data });

  return (
    <AuthShell title={t('forgot.title')} subtitle={t('forgot.intro')}>
      {options && !options.smtpConfigured && <Alert type="warning" showIcon message={t('forgot.noSmtp')} style={{ marginBottom: 16 }} />}
      {sent && <Alert type="success" showIcon message={sent} style={{ marginBottom: 16 }} />}
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
      <Form
        layout="vertical"
        onFinish={async (v: { userNameOrEmail: string }) => {
          setLoading(true);
          setError(null);
          try {
            const r = await http.post<{ message: string }>('/auth/forgot-password', v);
            setSent(r.data.message);
          } catch (e) {
            setError(problemMessage(e));
          } finally {
            setLoading(false);
          }
        }}
      >
        <Form.Item name="userNameOrEmail" label={t('forgot.userOrEmail')} rules={[zodRule(zs.required(256))]}>
          <Input size="large" autoFocus />
        </Form.Item>
        <Button type="primary" htmlType="submit" block size="large" loading={loading}>
          {t('forgot.send')}
        </Button>
      </Form>
      <div style={{ textAlign: 'center', marginTop: 16 }}>
        <Link to="/login">{t('forgot.backToLogin')}</Link>
      </div>
    </AuthShell>
  );
}

export function ResetPasswordPage() {
  const { t } = useTranslation('auth');
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const token = params.get('token');
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (!token) return <AuthShell title={t('reset.title')}><Result status="warning" title={t('reset.missingToken')} /></AuthShell>;
  if (done)
    return (
      <AuthShell title={t('reset.title')}>
        <Result status="success" title={t('reset.done')} extra={<Button type="primary" onClick={() => navigate('/login')}>{t('login.submit')}</Button>} />
      </AuthShell>
    );

  return (
    <AuthShell title={t('reset.title')} subtitle={t('changePassword.policy')}>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
      <Form
        layout="vertical"
        onFinish={async (v: { newPassword: string }) => {
          setLoading(true);
          setError(null);
          try {
            await http.post('/auth/reset-password', { token, newPassword: v.newPassword });
            setDone(true);
          } catch (e) {
            setError(problemMessage(e));
          } finally {
            setLoading(false);
          }
        }}
      >
        <PasswordFields newLabel={t('reset.newPassword')} confirmLabel={t('reset.confirmPassword')} />
        <Button type="primary" htmlType="submit" block size="large" loading={loading}>
          {t('reset.submit')}
        </Button>
      </Form>
    </AuthShell>
  );
}

/** New password + confirmation fields with zod policy validation. */
export function PasswordFields({ newLabel, confirmLabel }: { newLabel: string; confirmLabel: string }) {
  const { t } = useTranslation();
  return (
    <>
      <Form.Item name="newPassword" label={newLabel} rules={[zodRule(zs.password())]} hasFeedback>
        <Input.Password size="large" autoComplete="new-password" data-testid="new-password" />
      </Form.Item>
      <Form.Item
        name="confirmPassword"
        label={confirmLabel}
        dependencies={['newPassword']}
        hasFeedback
        rules={[
          { required: true, message: t('validation.requiredShort') },
          ({ getFieldValue }) => ({
            validator: (_, v) => (!v || getFieldValue('newPassword') === v ? Promise.resolve() : Promise.reject(new Error(t('validation.passwordMismatch')))),
          }),
        ]}
      >
        <Input.Password size="large" autoComplete="new-password" data-testid="confirm-password" />
      </Form.Item>
    </>
  );
}
