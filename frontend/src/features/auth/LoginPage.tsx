import { useEffect, useState } from 'react';
import { Alert, Button, Form, Input, Space } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { problemMessage } from '@/shared/api/http';
import type { LoginResponse } from '@/shared/auth/authStore';
import { AuthShell } from './AuthShell';
import { applySession, captchaStatus, login, newCaptcha, sendEmailOtp, verifyMfa } from './api';

type Step = 'password' | 'mfa';

export default function LoginPage() {
  const { t } = useTranslation('auth');
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [step, setStep] = useState<Step>('password');
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(params.get('expired') ? t('login.expired') : params.get('loggedOut') ? t('login.loggedOut') : null);
  const [loading, setLoading] = useState(false);
  const [captcha, setCaptcha] = useState<{ id: string; svg: string } | null>(null);
  const [mfa, setMfa] = useState<{ token: string; methods: string[] } | null>(null);
  const [method, setMethod] = useState<'totp' | 'recovery' | 'email'>('totp');
  const [form] = Form.useForm();

  const next = params.get('next') || '/';

  const loadCaptcha = async () => setCaptcha(await newCaptcha());

  useEffect(() => {
    document.title = t('login.title') + ' · BlueMeal';
  }, [t]);

  const finish = (r: LoginResponse) => {
    applySession(r);
    navigate(r.restricted ? '/first-login' : next, { replace: true });
  };

  const onPassword = async (v: { userName: string; password: string; captchaAnswer?: string }) => {
    setLoading(true);
    setError(null);
    setInfo(null);
    try {
      const r = await login(v.userName, v.password, captcha?.id, v.captchaAnswer);
      if (r.mfaRequired && r.mfaToken) {
        setMfa({ token: r.mfaToken, methods: r.mfaMethods ?? ['totp'] });
        setStep('mfa');
      } else finish(r);
    } catch (e) {
      setError(problemMessage(e));
      form.setFieldValue('captchaAnswer', undefined);
      try {
        if (await captchaStatus(v.userName)) await loadCaptcha();
      } catch {
        /* ignore */
      }
    } finally {
      setLoading(false);
    }
  };

  const onMfa = async (v: { code: string }) => {
    if (!mfa) return;
    setLoading(true);
    setError(null);
    try {
      finish(await verifyMfa(mfa.token, v.code, method));
    } catch (e) {
      setError(problemMessage(e));
    } finally {
      setLoading(false);
    }
  };

  if (step === 'mfa' && mfa) {
    return (
      <AuthShell title={t('mfa.title')} subtitle={method === 'recovery' ? t('mfa.recoveryCode') : t('mfa.subtitle')}>
        {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
        {info && <Alert type="success" showIcon message={info} style={{ marginBottom: 16 }} />}
        <Form layout="vertical" onFinish={onMfa} autoComplete="off">
          <Form.Item name="code" label={t('mfa.code')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}>
            <Input
              size="large"
              autoFocus
              inputMode={method === 'recovery' ? 'text' : 'numeric'}
              maxLength={method === 'recovery' ? 9 : 6}
              autoComplete="one-time-code"
              data-testid="mfa-code"
            />
          </Form.Item>
          <Button type="primary" htmlType="submit" block size="large" loading={loading}>
            {t('mfa.verify')}
          </Button>
        </Form>
        <Space direction="vertical" style={{ marginTop: 16, width: '100%' }}>
          {method !== 'totp' && <Button type="link" onClick={() => setMethod('totp')}>{t('mfa.useTotp')}</Button>}
          {method !== 'recovery' && <Button type="link" onClick={() => setMethod('recovery')}>{t('mfa.useRecovery')}</Button>}
          {mfa.methods.includes('email') && method !== 'email' && (
            <Button
              type="link"
              onClick={async () => {
                try {
                  await sendEmailOtp(mfa.token);
                  setMethod('email');
                  setInfo(t('mfa.emailSent'));
                } catch (e) {
                  setError(problemMessage(e));
                }
              }}
            >
              {t('mfa.useEmail')}
            </Button>
          )}
        </Space>
      </AuthShell>
    );
  }

  return (
    <AuthShell title={t('login.title')} subtitle={t('login.subtitle')} hint={t('login.firstLoginHint')}>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} data-testid="login-error" />}
      {info && <Alert type="info" showIcon message={info} style={{ marginBottom: 16 }} />}
      <Form form={form} layout="vertical" onFinish={onPassword} requiredMark={false}>
        <Form.Item name="userName" label={t('login.userName')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}>
          <Input size="large" autoComplete="username" autoFocus data-testid="login-username" />
        </Form.Item>
        <Form.Item name="password" label={t('login.password')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}>
          <Input.Password size="large" autoComplete="current-password" data-testid="login-password" />
        </Form.Item>
        {captcha && (
          <Form.Item label={t('login.captcha')} required>
            <Space.Compact style={{ width: '100%' }}>
              <img src={'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(captcha.svg)} alt={t('login.captcha')} height={40} style={{ borderRadius: 6 }} />
              <Button icon={<ReloadOutlined />} onClick={() => void loadCaptcha()} aria-label={t('login.captchaRefresh')} style={{ height: 40 }} />
            </Space.Compact>
            <Form.Item name="captchaAnswer" noStyle rules={[{ required: true, message: t('common:validation.requiredShort') }]}>
              <Input placeholder={t('login.captchaHint')} inputMode="numeric" style={{ marginTop: 8 }} />
            </Form.Item>
          </Form.Item>
        )}
        <div style={{ display: 'flex', justifyContent: 'flex-end', fontSize: 13, margin: '-6px 0 18px' }}>
          <Link to="/forgot-password">{t('login.forgot')}</Link>
        </div>
        <Button type="primary" htmlType="submit" block size="large" loading={loading} data-testid="login-submit">
          {t('login.submit')}
        </Button>
      </Form>
    </AuthShell>
  );
}
