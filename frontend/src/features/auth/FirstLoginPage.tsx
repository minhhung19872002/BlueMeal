import { useEffect, useState } from 'react';
import { Alert, Button, Form, Input, Result, Steps } from 'antd';
import { Navigate, useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { useAuth } from '@/shared/auth/authStore';
import { useBootstrapSession, FullPageSpin } from '@/shared/components/Guards';
import { AuthShell } from './AuthShell';
import { PasswordFields } from './PasswordPages';
import { MfaSetup } from './MfaSetup';
import { refreshAfterSetup } from './api';

/** Mandatory first-login flow: change password (if required) then enable MFA (if required by role) — SPEC §12. */
export default function FirstLoginPage() {
  const { t } = useTranslation('auth');
  const ready = useBootstrapSession();
  const navigate = useNavigate();
  const token = useAuth((s) => s.accessToken);
  const mustChange = useAuth((s) => s.mustChangePassword);
  const mfaSetup = useAuth((s) => s.mfaSetupRequired);
  const restricted = useAuth((s) => s.restricted);
  const [step, setStep] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!mustChange && mfaSetup) setStep(1);
  }, [mustChange, mfaSetup]);

  if (!ready) return <FullPageSpin />;
  if (!token) return <Navigate to="/login" replace />;
  if (!restricted && step < 2) return <Navigate to="/" replace />;

  const afterStep = async () => {
    const r = await refreshAfterSetup();
    if (r.mfaSetupRequired) setStep(1);
    else setStep(2);
  };

  return (
    <AuthShell title={t('firstLogin.title')} width={520}>
      <Steps
        size="small"
        current={step}
        style={{ marginBottom: 24 }}
        items={[{ title: t('firstLogin.stepPassword') }, { title: t('firstLogin.stepMfa') }, { title: t('firstLogin.stepDone') }]}
      />
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
      {step === 0 && (
        <>
          <Alert type="info" showIcon message={t('changePassword.firstLogin')} description={t('changePassword.policy')} style={{ marginBottom: 16 }} />
          <Form
            layout="vertical"
            onFinish={async (v: { currentPassword: string; newPassword: string }) => {
              setLoading(true);
              setError(null);
              try {
                await http.post('/profile/change-password', { currentPassword: v.currentPassword, newPassword: v.newPassword });
                await afterStep();
              } catch (e) {
                setError(problemMessage(e));
              } finally {
                setLoading(false);
              }
            }}
          >
            <Form.Item name="currentPassword" label={t('changePassword.current')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}>
              <Input.Password size="large" autoComplete="current-password" data-testid="current-password" />
            </Form.Item>
            <PasswordFields newLabel={t('changePassword.new')} confirmLabel={t('changePassword.confirm')} />
            <Button type="primary" htmlType="submit" block size="large" loading={loading} data-testid="change-password-submit">
              {t('changePassword.submit')}
            </Button>
          </Form>
        </>
      )}
      {step === 1 && <MfaSetup onDone={() => void afterStep()} />}
      {step === 2 && (
        <Result
          status="success"
          title={t('firstLogin.done')}
          extra={
            <Button type="primary" size="large" onClick={() => navigate('/', { replace: true })} data-testid="first-login-continue">
              {t('firstLogin.continue')}
            </Button>
          }
        />
      )}
    </AuthShell>
  );
}
