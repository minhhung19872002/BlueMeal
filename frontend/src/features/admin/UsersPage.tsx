import { useEffect, useState } from 'react';
import { Alert, App, Button, Card, Drawer, Dropdown, Form, Grid, Input, Modal, Select, Space, Switch, Table, Tabs, Tag, Tooltip, Typography } from 'antd';
import { EditOutlined, MoreOutlined, PlusOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect } from '@/shared/components/catalog/inputs';
import { fmt } from '@/shared/utils/format';
import { zodRule, zs } from '@/shared/utils/validation';

interface UserRow { id: string; userName: string; fullName: string; email?: string; isActive: boolean; locked: boolean; mfaEnabled: boolean; mfaRequired: boolean; mustChangePassword: boolean; lastLoginAt?: string; roles: string[]; activeSessions: number; employeeId?: string }
interface Role { id: string; code: string; nameVi: string; nameEn?: string }
interface Scope { type: 'Warehouse' | 'Customer'; id: string }

function UserSessions({ userId }: { userId: string }) {
  const { t } = useTranslation('admin');
  const { data, refetch } = useQuery({ queryKey: ['user-sessions', userId], queryFn: async () => (await http.get<any[]>(`/users/${userId}/sessions`)).data });
  const { has } = usePermission();
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      {has('session.revoke') && (
        <Button danger onClick={async () => { await http.delete(`/sessions/user/${userId}`); void refetch(); }}>{t('sessions.revokeAll')}</Button>
      )}
      <Table
        size="small"
        rowKey="id"
        dataSource={data}
        pagination={false}
        scroll={{ x: 640 }}
        columns={[
          { title: t('sessions.device'), dataIndex: 'device' },
          { title: t('sessions.ip'), dataIndex: 'ip' },
          { title: t('sessions.loginAt'), dataIndex: 'createdAt', render: (v) => fmt.dateTime(v) },
          { title: t('sessions.lastSeen'), dataIndex: 'lastSeenAt', render: (v) => fmt.dateTime(v) },
          { title: t('common:fields.status'), key: 's', render: (_, r) => (r.active ? <Tag color="green">{t('users.active')}</Tag> : <Tag>{t('sessions.revoked')}{r.revokedReason ? ` (${r.revokedReason})` : ''}</Tag>) },
        ]}
      />
    </Space>
  );
}

function LoginHistory({ userId }: { userId: string }) {
  const { t } = useTranslation(['admin', 'auth']);
  const [page, setPage] = useState(1);
  const { data } = useQuery({ queryKey: ['user-logins', userId, page], queryFn: async () => (await http.get<PagedResult<any>>(`/users/${userId}/login-history`, { params: { page, pageSize: 10 } })).data });
  return (
    <Table
      size="small"
      rowKey="id"
      dataSource={data?.items}
      scroll={{ x: 600 }}
      pagination={{ current: page, total: data?.totalItems, pageSize: 10, onChange: setPage, showSizeChanger: false }}
      columns={[
        { title: t('auth:profile.time'), dataIndex: 'at', render: (v) => fmt.dateTime(v), width: 150 },
        { title: t('auth:profile.result'), dataIndex: 'result', render: (v: string) => <Tag color={v === 'Success' ? 'green' : 'red'}>{t('auth:loginResult.' + v, v)}</Tag> },
        { title: t('auth:profile.ip'), dataIndex: 'ip' },
        { title: t('admin:audit.message'), dataIndex: 'reason' },
      ]}
    />
  );
}

export default function UsersPage() {
  const { t, i18n } = useTranslation('admin');
  const { message, modal } = App.useApp();
  const { has } = usePermission();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [q, setQ] = useState('');
  const [status, setStatus] = useState<string>();
  const [roleId, setRoleId] = useState<string>();
  const [editing, setEditing] = useState<UserRow | 'new' | null>(null);
  const [temp, setTemp] = useState<string | null>(null);
  const [form] = Form.useForm();

  const roles = useQuery({ queryKey: ['roles'], queryFn: async () => (await http.get<Role[]>('/roles')).data });
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['users', page, pageSize, q, status, roleId],
    queryFn: async () => (await http.get<PagedResult<UserRow>>('/users', { params: { page, pageSize, q: q || undefined, status, roleId } })).data,
    placeholderData: keepPreviousData,
  });
  const detail = useQuery({
    queryKey: ['user', editing && editing !== 'new' ? editing.id : null],
    enabled: !!editing && editing !== 'new',
    queryFn: async () => (await http.get<any>(`/users/${(editing as UserRow).id}`)).data,
  });

  const roleOptions = roles.data?.map((r) => ({ value: r.id, label: i18n.language === 'en' && r.nameEn ? r.nameEn : r.nameVi })) ?? [];

  const openEdit = (u: UserRow | 'new') => {
    setEditing(u);
    form.resetFields();
    if (u === 'new') form.setFieldsValue({ roleIds: [], language: 'vi', mfaRequired: false, warehouses: [], customers: [] });
  };

  // populate edit form once the detail arrives
  useEffect(() => {
    if (!detail.data) return;
    const s: Scope[] = detail.data.scopes ?? [];
    form.setFieldsValue({ ...detail.data, warehouses: s.filter((x) => x.type === 'Warehouse').map((x) => x.id), customers: s.filter((x) => x.type === 'Customer').map((x) => x.id) });
  }, [detail.data, form]);

  const run = async (fn: () => Promise<unknown>, okMsg?: string) => {
    try {
      await fn();
      if (okMsg) message.success(okMsg);
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const save = async () => {
    const v = await form.validateFields();
    try {
      let id: string;
      if (editing === 'new') {
        const r = await http.post<{ id: string; temporaryPassword?: string }>('/users', v);
        id = r.data.id;
        if (r.data.temporaryPassword) setTemp(r.data.temporaryPassword);
      } else {
        id = (editing as UserRow).id;
        await http.put(`/users/${id}`, v);
      }
      const scopes: Scope[] = [...(v.warehouses ?? []).map((x: string) => ({ type: 'Warehouse', id: x })), ...(v.customers ?? []).map((x: string) => ({ type: 'Customer', id: x }))];
      await http.put(`/users/${id}/scopes`, { scopes });
      message.success(t('common:saved'));
      setEditing(null);
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const formBody = (
    <Form form={form} layout="vertical">
      <Form.Item name="id" hidden><Input /></Form.Item>
      <Form.Item name="userName" label={t('users.userName')} rules={[zodRule(z.string().regex(/^[A-Za-z0-9._@-]+$/, 'catalog:validation.code'))]}>
        <Input disabled={editing !== 'new'} autoComplete="off" />
      </Form.Item>
      <Form.Item name="fullName" label={t('users.fullName')} rules={[zodRule(zs.required(200))]}><Input /></Form.Item>
      <Form.Item name="email" label={t('users.email')} rules={[zodRule(zs.email())]}><Input /></Form.Item>
      <Form.Item name="roleIds" label={t('users.roles')}><Select mode="multiple" options={roleOptions} /></Form.Item>
      <Form.Item name="employeeId" label={t('users.employee')}><LookupSelect resource="employees" /></Form.Item>
      <Space size="large" wrap>
        <Form.Item name="mfaRequired" label={t('users.mfaRequired')} valuePropName="checked"><Switch /></Form.Item>
        <Form.Item name="language" label={t('users.language')}>
          <Select style={{ width: 140 }} options={[{ value: 'vi', label: t('common:language.vi') }, { value: 'en', label: t('common:language.en') }]} />
        </Form.Item>
      </Space>
      <Typography.Title level={5}>{t('users.scopes')}</Typography.Title>
      <Typography.Paragraph type="secondary">{t('users.scopeHint')}</Typography.Paragraph>
      <Form.Item name="warehouses" label={t('users.scopeWarehouses')}><LookupSelect resource="warehouses" multiple /></Form.Item>
      <Form.Item name="customers" label={t('users.scopeCustomers')}><LookupSelect resource="customers" multiple /></Form.Item>
    </Form>
  );

  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t('users.title')}</Typography.Title>
        {has('user.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => openEdit('new')}>{t('users.add')}</Button>}
      </div>
      <div className="bm-toolbar">
        <Input.Search allowClear placeholder={t('common:searchPlaceholder')} onSearch={(v) => { setQ(v); setPage(1); }} style={{ width: screens.md ? 300 : '100%' }} />
        <Select allowClear placeholder={t('users.roles')} options={roleOptions} value={roleId} onChange={setRoleId} style={{ width: screens.md ? 220 : '100%' }} />
        <Select allowClear placeholder={t('users.status')} value={status} onChange={setStatus} style={{ width: screens.md ? 180 : '100%' }}
          options={[{ value: 'active', label: t('users.active') }, { value: 'disabled', label: t('users.disabled') }, { value: 'locked', label: t('users.lockedOut') }]} />
      </div>
      <Table<UserRow>
        rowKey="id"
        loading={isFetching}
        dataSource={data?.items}
        scroll={{ x: 'max-content' }}
        pagination={{ current: page, pageSize, total: data?.totalItems, showSizeChanger: true, onChange: (p, s) => { setPage(p); setPageSize(s); }, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('users.userName'), dataIndex: 'userName', fixed: screens.md ? 'left' : undefined, width: 150 },
          { title: t('users.fullName'), dataIndex: 'fullName', width: 200 },
          { title: t('users.roles'), dataIndex: 'roles', render: (r: string[]) => r.map((x) => <Tag key={x}>{x}</Tag>) },
          {
            title: t('users.status'), key: 'st', width: 200,
            render: (_, u) => (
              <Space wrap size={4}>
                {u.isActive ? <Tag color="green">{t('users.active')}</Tag> : <Tag color="red">{t('users.disabled')}</Tag>}
                {u.locked && <Tag color="orange">{t('users.lockedOut')}</Tag>}
                {u.mustChangePassword && <Tag>{t('users.mustChange')}</Tag>}
              </Space>
            ),
          },
          { title: t('users.mfa'), key: 'mfa', width: 90, render: (_, u) => (u.mfaEnabled ? <Tooltip title="TOTP"><SafetyCertificateOutlined style={{ color: '#3f8f5a' }} /></Tooltip> : u.mfaRequired ? <Tag color="orange">!</Tag> : null) },
          { title: t('users.lastLogin'), dataIndex: 'lastLoginAt', width: 150, render: (v) => fmt.dateTime(v) },
          { title: t('sessions.title'), dataIndex: 'activeSessions', width: 90, align: 'right' },
          {
            title: t('common:fields.actions'), key: 'a', fixed: screens.md ? 'right' : undefined, width: 100,
            render: (_, u) => (
              <Space size={0}>
                {has('user.edit') && <Button type="text" icon={<EditOutlined />} onClick={() => openEdit(u)} />}
                <Dropdown
                  trigger={['click']}
                  menu={{
                    items: [
                      ...(has('user.lock') ? [u.isActive
                        ? { key: 'lock', danger: true, label: t('users.lock'), onClick: () => modal.confirm({ title: t('users.lock'), content: t('users.confirmLock'), onOk: () => run(() => http.post(`/users/${u.id}/lock`), t('common:saved')) }) }
                        : { key: 'unlock', label: t('users.unlock'), onClick: () => void run(() => http.post(`/users/${u.id}/unlock`), t('common:saved')) }] : []),
                      ...(has('user.reset_password') ? [
                        { key: 'reset', label: t('users.resetPassword'), onClick: () => void run(async () => setTemp((await http.post<{ temporaryPassword: string }>(`/users/${u.id}/reset-password`)).data.temporaryPassword)) },
                        { key: 'mfa', label: t('users.resetMfa'), onClick: () => modal.confirm({ title: t('users.resetMfa'), content: t('users.confirmResetMfa'), onOk: () => run(() => http.post(`/users/${u.id}/reset-mfa`), t('common:saved')) }) },
                      ] : []),
                      ...(has('user.edit') ? [{ key: 'force', label: t('users.requireChange'), onClick: () => void run(() => http.post(`/users/${u.id}/require-password-change`), t('common:saved')) }] : []),
                    ],
                  }}
                >
                  <Button type="text" icon={<MoreOutlined />} />
                </Dropdown>
              </Space>
            ),
          },
        ]}
      />
      <Drawer
        open={!!editing}
        width={screens.md ? 640 : '100%'}
        title={editing === 'new' ? t('users.add') : t('users.edit')}
        onClose={() => setEditing(null)}
        destroyOnHidden
        extra={<Button type="primary" onClick={() => void save()}>{t('common:actions.save')}</Button>}
      >
        {editing === 'new' ? formBody : editing && (
          <Tabs items={[
            { key: 'info', label: t('catalog:tabs.general'), children: formBody },
            ...(has('session.view') ? [{ key: 'sessions', label: t('users.sessions'), children: <UserSessions userId={editing.id} /> }] : []),
            { key: 'history', label: t('users.loginHistory'), children: <LoginHistory userId={editing.id} /> },
          ]} />
        )}
      </Drawer>
      <Modal open={!!temp} title={t('users.tempPassword')} onOk={() => setTemp(null)} onCancel={() => setTemp(null)} cancelButtonProps={{ hidden: true }}>
        <Alert type="warning" showIcon message={t('users.tempPasswordHint')} style={{ marginBottom: 12 }} />
        <Typography.Text code copyable style={{ fontSize: 18 }}>{temp}</Typography.Text>
      </Modal>
    </Card>
  );
}
