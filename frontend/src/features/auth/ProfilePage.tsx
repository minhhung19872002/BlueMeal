import { useState } from 'react';
import { App, Button, Card, Descriptions, Form, Input, Modal, Popconfirm, Segmented, Space, Switch, Table, Tabs, Tag, Typography } from 'antd';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { useAuth } from '@/shared/auth/authStore';
import { changeLanguage, type Lang } from '@/shared/i18n';
import { useUi } from '@/shared/hooks/useUiStore';
import { fmt } from '@/shared/utils/format';
import { PasswordFields } from './PasswordPages';
import { MfaSetup } from './MfaSetup';
import { fetchMe } from './api';

interface LoginRow { id: string; at: string; result: string; ip?: string; device?: string; reason?: string }
interface SessionRow { id: string; ip?: string; device?: string; createdAt: string; lastSeenAt: string; current: boolean }

export default function ProfilePage() {
  const { t } = useTranslation('auth');
  const { message } = App.useApp();
  const qc = useQueryClient();
  const user = useAuth((s) => s.user);
  const setUser = useAuth((s) => s.setUser);
  const theme = useUi((s) => s.theme);
  const setTheme = useUi((s) => s.setTheme);
  const [mfaOpen, setMfaOpen] = useState(false);
  const [codes, setCodes] = useState<string[] | null>(null);
  const [page, setPage] = useState(1);
  const [pwdForm] = Form.useForm();

  const reloadMe = async () => setUser(await fetchMe());

  const history = useQuery({
    queryKey: ['login-history', page],
    queryFn: async () => (await http.get<PagedResult<LoginRow>>('/profile/login-history', { params: { page, pageSize: 10 } })).data,
  });
  const sessions = useQuery({ queryKey: ['my-sessions'], queryFn: async () => (await http.get<SessionRow[]>('/profile/sessions')).data });

  const changePwd = useMutation({
    mutationFn: (v: { currentPassword: string; newPassword: string }) => http.post('/profile/change-password', v),
    onSuccess: () => {
      message.success(t('changePassword.done'));
      pwdForm.resetFields();
    },
    onError: (e) => message.error(problemMessage(e)),
  });

  const promptCode = (title: string, onOk: (code: string) => Promise<void>) => {
    let code = '';
    Modal.confirm({
      title,
      content: <Input autoFocus maxLength={6} inputMode="numeric" placeholder={t('mfa.code')} onChange={(e) => (code = e.target.value)} />,
      onOk: async () => {
        try {
          await onOk(code);
        } catch (e) {
          message.error(problemMessage(e));
          throw e;
        }
      },
    });
  };

  if (!user) return null;

  return (
    <Card title={t('profile.title')}>
      <Tabs
        items={[
          {
            key: 'info',
            label: t('profile.info'),
            children: (
              <Space direction="vertical" size="large" style={{ width: '100%', maxWidth: 640 }}>
                <Descriptions column={1} bordered size="small">
                  <Descriptions.Item label={t('login.userName')}>{user.userName}</Descriptions.Item>
                  <Descriptions.Item label={t('profile.fullName')}>{user.fullName}</Descriptions.Item>
                  <Descriptions.Item label={t('common:fields.email')}>{user.email}</Descriptions.Item>
                  <Descriptions.Item label={t('profile.roles')}>{user.roles.map((r) => <Tag key={r}>{r}</Tag>)}</Descriptions.Item>
                </Descriptions>
                <Space wrap>
                  <Typography.Text>{t('profile.language')}</Typography.Text>
                  <Segmented
                    value={user.language}
                    options={[{ label: t('common:language.vi'), value: 'vi' }, { label: t('common:language.en'), value: 'en' }]}
                    onChange={async (v) => {
                      await changeLanguage(v as Lang);
                      await http.put('/profile/preferences', { language: v });
                      await reloadMe();
                    }}
                  />
                </Space>
                <Space wrap>
                  <Typography.Text>{t('profile.theme')}</Typography.Text>
                  <Segmented
                    value={theme}
                    options={[{ label: t('common:theme.light'), value: 'light' }, { label: t('common:theme.dark'), value: 'dark' }]}
                    onChange={async (v) => {
                      setTheme(v as 'light' | 'dark');
                      await http.put('/profile/preferences', { theme: v });
                    }}
                  />
                </Space>
              </Space>
            ),
          },
          {
            key: 'security',
            label: t('profile.security'),
            children: (
              <Space direction="vertical" size="large" style={{ width: '100%', maxWidth: 520 }}>
                <Card size="small" title={t('changePassword.title')}>
                  <Typography.Paragraph type="secondary">{t('changePassword.policy')}</Typography.Paragraph>
                  <Form form={pwdForm} layout="vertical" onFinish={(v) => changePwd.mutate({ currentPassword: v.currentPassword, newPassword: v.newPassword })}>
                    <Form.Item name="currentPassword" label={t('changePassword.current')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}>
                      <Input.Password autoComplete="current-password" />
                    </Form.Item>
                    <PasswordFields newLabel={t('changePassword.new')} confirmLabel={t('changePassword.confirm')} />
                    <Button type="primary" htmlType="submit" loading={changePwd.isPending}>{t('changePassword.submit')}</Button>
                  </Form>
                </Card>
                <Card size="small" title={t('mfa.title')} extra={user.mfaEnabled ? <Tag color="green">{t('mfa.enabled')}</Tag> : <Tag>{t('mfa.disabled')}</Tag>}>
                  {user.mfaRequired && <Typography.Paragraph type="secondary">{t('mfa.required')}</Typography.Paragraph>}
                  <Space wrap>
                    {!user.mfaEnabled && <Button type="primary" onClick={() => setMfaOpen(true)}>{t('mfa.enable')}</Button>}
                    {user.mfaEnabled && (
                      <Button onClick={() => promptCode(t('mfa.regenerate'), async (code) => setCodes((await http.post<{ recoveryCodes: string[] }>('/profile/mfa/recovery-codes', { code })).data.recoveryCodes))}>
                        {t('mfa.regenerate')}
                      </Button>
                    )}
                    {user.mfaEnabled && !user.mfaRequired && (
                      <Button danger onClick={() => promptCode(t('mfa.disable'), async (code) => { await http.post('/profile/mfa/disable', { code }); await reloadMe(); })}>
                        {t('mfa.disable')}
                      </Button>
                    )}
                  </Space>
                  <div style={{ marginTop: 12 }}>
                    <Space>
                      <Switch onChange={(v) => void http.post('/profile/email-otp', null, { params: { enabled: v } })} />
                      <Typography.Text>{t('profile.emailOtp')}</Typography.Text>
                    </Space>
                  </div>
                </Card>
              </Space>
            ),
          },
          {
            key: 'history',
            label: t('profile.loginHistory'),
            children: (
              <Table<LoginRow>
                rowKey="id"
                size="small"
                scroll={{ x: 640 }}
                loading={history.isLoading}
                dataSource={history.data?.items}
                pagination={{ current: page, pageSize: 10, total: history.data?.totalItems, onChange: setPage, showSizeChanger: false }}
                columns={[
                  { title: t('profile.time'), dataIndex: 'at', render: (v) => fmt.dateTime(v), width: 150 },
                  { title: t('profile.result'), dataIndex: 'result', render: (v: string) => <Tag color={v === 'Success' ? 'green' : v === 'MfaRequired' ? 'blue' : 'red'}>{t('loginResult.' + v, v)}</Tag> },
                  { title: t('profile.ip'), dataIndex: 'ip' },
                  { title: t('profile.device'), dataIndex: 'device' },
                ]}
              />
            ),
          },
          {
            key: 'sessions',
            label: t('profile.sessions'),
            children: (
              <Table<SessionRow>
                rowKey="id"
                size="small"
                scroll={{ x: 640 }}
                loading={sessions.isLoading}
                dataSource={sessions.data}
                pagination={false}
                columns={[
                  { title: t('profile.device'), dataIndex: 'device', render: (v, r) => <Space>{v}{r.current && <Tag color="green">{t('profile.current')}</Tag>}</Space> },
                  { title: t('profile.ip'), dataIndex: 'ip' },
                  { title: t('common:fields.createdAt'), dataIndex: 'createdAt', render: (v) => fmt.dateTime(v) },
                  { title: t('profile.lastSeen'), dataIndex: 'lastSeenAt', render: (v) => fmt.dateTime(v) },
                  {
                    title: '',
                    key: 'a',
                    render: (_, r) =>
                      !r.current && (
                        <Popconfirm title={t('profile.signOutSession')} onConfirm={async () => { await http.delete(`/profile/sessions/${r.id}`); void qc.invalidateQueries({ queryKey: ['my-sessions'] }); }}>
                          <Button size="small" danger>{t('profile.signOutSession')}</Button>
                        </Popconfirm>
                      ),
                  },
                ]}
              />
            ),
          },
        ]}
      />
      <Modal open={mfaOpen} title={t('mfa.setupTitle')} footer={null} onCancel={() => setMfaOpen(false)} destroyOnHidden>
        <MfaSetup onDone={async () => { setMfaOpen(false); await reloadMe(); }} />
      </Modal>
      <Modal open={!!codes} title={t('mfa.recoveryTitle')} onOk={() => setCodes(null)} onCancel={() => setCodes(null)}>
        <Typography.Paragraph type="secondary">{t('mfa.recoveryIntro')}</Typography.Paragraph>
        {codes?.map((c) => <Typography.Text key={c} code style={{ display: 'inline-block', margin: 4 }}>{c}</Typography.Text>)}
      </Modal>
    </Card>
  );
}
