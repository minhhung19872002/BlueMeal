import { useState } from 'react';
import { App, Button, Card, DatePicker, Descriptions, Drawer, Grid, Input, Popconfirm, Select, Space, Table, Tag, Typography } from 'antd';
import { DownloadOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { downloadFile, http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { useEnumLabel } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { DATE_FORMAT, fmt } from '@/shared/utils/format';

// ------------------------------------------------------------------ active sessions (SPEC §7.1.7)

interface SessionRow { id: string; userId: string; userName: string; fullName: string; ip?: string; device?: string; createdAt: string; lastSeenAt: string; expiresAt: string; isMfaSetupOnly: boolean; current: boolean }

export function SessionsPage() {
  const { t } = useTranslation('admin');
  const { message } = App.useApp();
  const { has } = usePermission();
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['sessions', page, q],
    queryFn: async () => (await http.get<PagedResult<SessionRow>>('/sessions', { params: { page, pageSize: 20, q: q || undefined } })).data,
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  });
  const revoke = async (url: string) => {
    try {
      await http.delete(url);
      message.success(t('common:saved'));
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  return (
    <Card>
      <div className="bm-page-header"><Typography.Title level={4}>{t('sessions.title')}</Typography.Title></div>
      <div className="bm-toolbar"><Input.Search allowClear placeholder={t('common:searchPlaceholder')} onSearch={(v) => { setQ(v); setPage(1); }} style={{ maxWidth: 320 }} /></div>
      <Table<SessionRow>
        rowKey="id"
        loading={isFetching}
        dataSource={data?.items}
        scroll={{ x: 'max-content' }}
        pagination={{ current: page, total: data?.totalItems, pageSize: 20, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('sessions.user'), key: 'u', render: (_, r) => <Space direction="vertical" size={0}><b>{r.userName}</b><Typography.Text type="secondary">{r.fullName}</Typography.Text></Space> },
          { title: t('sessions.ip'), dataIndex: 'ip' },
          { title: t('sessions.device'), dataIndex: 'device' },
          { title: t('sessions.loginAt'), dataIndex: 'createdAt', render: (v) => fmt.dateTime(v) },
          { title: t('sessions.lastSeen'), dataIndex: 'lastSeenAt', render: (v) => fmt.dateTime(v) },
          { title: t('common:fields.status'), key: 's', render: (_, r) => <Space>{r.current && <Tag color="green">{t('sessions.current')}</Tag>}{r.isMfaSetupOnly && <Tag color="orange">{t('sessions.restricted')}</Tag>}</Space> },
          {
            title: t('common:fields.actions'), key: 'a',
            render: (_, r) => has('session.revoke') && !r.current && (
              <Space>
                <Popconfirm title={t('sessions.confirmRevoke')} onConfirm={() => void revoke(`/sessions/${r.id}`)}><Button size="small" danger>{t('sessions.revoke')}</Button></Popconfirm>
                <Popconfirm title={t('sessions.revokeAll')} onConfirm={() => void revoke(`/sessions/user/${r.userId}`)}><Button size="small">{t('sessions.revokeAll')}</Button></Popconfirm>
              </Space>
            ),
          },
        ]}
      />
    </Card>
  );
}

// ------------------------------------------------------------------ function list (read-only, generated from code) — SPEC §7.1.8

interface PermRow { id: string; code: string; module: string; action: string; nameVi: string; nameEn: string; moduleNameVi: string; moduleNameEn: string; actionNameVi: string; actionNameEn: string; roleCount: number }

export function PermissionsPage() {
  const { t, i18n } = useTranslation('admin');
  const en = i18n.language === 'en';
  const [q, setQ] = useState('');
  const { data, isLoading } = useQuery({ queryKey: ['permissions'], queryFn: async () => (await http.get<PermRow[]>('/permissions')).data });
  const f = q.trim().toLowerCase();
  const rows = (data ?? []).filter((p) => !f || p.code.includes(f) || p.nameVi.toLowerCase().includes(f) || p.nameEn.toLowerCase().includes(f));
  return (
    <Card>
      <div className="bm-page-header"><Typography.Title level={4}>{t('permissions.title')}</Typography.Title><Tag>{data?.length ?? 0}</Tag></div>
      <Input.Search allowClear placeholder={t('common:searchPlaceholder')} onChange={(e) => setQ(e.target.value)} style={{ maxWidth: 320, marginBottom: 12 }} />
      <Table<PermRow>
        rowKey="id"
        size="small"
        loading={isLoading}
        dataSource={rows}
        scroll={{ x: 800 }}
        pagination={{ pageSize: 50, showSizeChanger: false }}
        columns={[
          { title: t('permissions.code'), dataIndex: 'code', render: (v) => <Typography.Text code>{v}</Typography.Text>, width: 260 },
          { title: t('permissions.module'), key: 'm', render: (_, p) => (en ? p.moduleNameEn : p.moduleNameVi), width: 220 },
          { title: t('permissions.action'), key: 'a', render: (_, p) => (en ? p.actionNameEn : p.actionNameVi), width: 200 },
          { title: t('permissions.name'), key: 'n', render: (_, p) => (en ? p.nameEn : p.nameVi) },
          { title: t('permissions.roleCount'), dataIndex: 'roleCount', align: 'right', width: 110 },
        ]}
      />
    </Card>
  );
}

// ------------------------------------------------------------------ audit log (SPEC §10.3, RPT-16)

interface AuditRow { id: string; at: string; userName?: string; ip?: string; userAgent?: string; action: string; entityType?: string; entityId?: string; oldValues?: string; newValues?: string; success: boolean; message?: string; traceId?: string }

function JsonBlock({ value }: { value?: string }) {
  if (!value) return <Typography.Text type="secondary">∅</Typography.Text>;
  let pretty = value;
  try {
    pretty = JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    /* keep raw */
  }
  return <pre style={{ maxHeight: 320, overflow: 'auto', fontSize: 12, margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{pretty}</pre>;
}

export function AuditLogsPage() {
  const { t } = useTranslation('admin');
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs] | null>([dayjs().subtract(7, 'day').startOf('day'), dayjs().endOf('day')]);
  const [action, setAction] = useState<string>();
  const [entityType, setEntityType] = useState<string>();
  const [userName, setUserName] = useState('');
  const [success, setSuccess] = useState<string>();
  const [detail, setDetail] = useState<AuditRow | null>(null);
  const params = {
    page, pageSize: 50, from: range?.[0]?.toISOString(), to: range?.[1]?.toISOString(), action, entityType, userName: userName || undefined, success,
  };
  const { data, isFetching } = useQuery({
    queryKey: ['audit', params],
    queryFn: async () => (await http.get<PagedResult<AuditRow>>('/audit-logs', { params })).data,
    placeholderData: keepPreviousData,
  });
  const types = useQuery({ queryKey: ['audit-types'], queryFn: async () => (await http.get<string[]>('/audit-logs/entity-types')).data });
  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t('audit.title')}</Typography.Title>
        {has('audit.export') && <Button icon={<DownloadOutlined />} onClick={() => void downloadFile('/audit-logs/export', { ...params, page: undefined, pageSize: undefined })}>{t('common:actions.export')}</Button>}
        {has('audit.export') && <Button icon={<DownloadOutlined />} onClick={() => void downloadFile('/audit-logs/export', { ...params, page: undefined, pageSize: undefined, format: 'csv' }, 'audit-logs.csv')}>{t('common:actions.exportCsv')}</Button>}
        {has('audit.export') && <Button icon={<DownloadOutlined />} onClick={() => void downloadFile('/audit-logs/export', { ...params, page: undefined, pageSize: undefined, format: 'ods' }, 'audit-logs.ods')}>{t('common:actions.exportOds')}</Button>}
      </div>
      <div className="bm-toolbar">
        <DatePicker.RangePicker value={range} onChange={(v) => { setRange(v as never); setPage(1); }} format={DATE_FORMAT} allowClear style={{ width: screens.md ? 280 : '100%' }} />
        <Input.Search allowClear placeholder={t('audit.user')} onSearch={(v) => { setUserName(v); setPage(1); }} style={{ width: screens.md ? 180 : '100%' }} />
        <Select aria-label={t('audit.action')} allowClear showSearch placeholder={t('audit.action')} value={action} onChange={(v) => { setAction(v); setPage(1); }} style={{ width: screens.md ? 200 : '100%' }}
          options={ENUMS.AuditAction.map((a) => ({ value: a, label: enumLabel('AuditAction', a) }))} />
        <Select aria-label={t('audit.entity')} allowClear showSearch placeholder={t('audit.entity')} value={entityType} onChange={(v) => { setEntityType(v); setPage(1); }} style={{ width: screens.md ? 200 : '100%' }}
          options={types.data?.map((x) => ({ value: x, label: x }))} />
        <Select aria-label={t('audit.result')} allowClear placeholder={t('audit.result')} value={success} onChange={(v) => { setSuccess(v); setPage(1); }} style={{ width: screens.md ? 140 : '100%' }}
          options={[{ value: 'true', label: t('audit.success') }, { value: 'false', label: t('audit.failure') }]} />
      </div>
      <Table<AuditRow>
        rowKey="id"
        size="small"
        loading={isFetching}
        dataSource={data?.items}
        scroll={{ x: 'max-content' }}
        onRow={(r) => ({ onClick: () => setDetail(r), style: { cursor: 'pointer' } })}
        pagination={{ current: page, total: data?.totalItems, pageSize: 50, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('audit.time'), dataIndex: 'at', render: (v) => fmt.dateTime(v), width: 140 },
          { title: t('audit.user'), dataIndex: 'userName', width: 140 },
          { title: t('audit.ip'), dataIndex: 'ip', width: 130 },
          { title: t('audit.action'), dataIndex: 'action', render: (v) => <Tag>{enumLabel('AuditAction', v)}</Tag>, width: 150 },
          { title: t('audit.entity'), dataIndex: 'entityType', width: 160 },
          { title: t('audit.result'), dataIndex: 'success', render: (v) => (v ? <Tag color="green">{t('audit.success')}</Tag> : <Tag color="red">{t('audit.failure')}</Tag>), width: 110 },
          { title: t('audit.message'), dataIndex: 'message', ellipsis: true },
        ]}
      />
      <Drawer open={!!detail} onClose={() => setDetail(null)} width={screens.md ? 640 : '100%'} title={detail ? `${enumLabel('AuditAction', detail.action)} — ${fmt.dateTime(detail.at)}` : ''}>
        {detail && (
          <Descriptions column={1} bordered size="small">
            <Descriptions.Item label={t('audit.user')}>{detail.userName}</Descriptions.Item>
            <Descriptions.Item label={t('audit.ip')}>{detail.ip}</Descriptions.Item>
            <Descriptions.Item label={t('audit.userAgent')}>{detail.userAgent}</Descriptions.Item>
            <Descriptions.Item label={t('audit.entity')}>{detail.entityType} {detail.entityId}</Descriptions.Item>
            <Descriptions.Item label={t('audit.message')}>{detail.message}</Descriptions.Item>
            <Descriptions.Item label={t('audit.trace')}><Typography.Text code>{detail.traceId}</Typography.Text></Descriptions.Item>
            <Descriptions.Item label={t('audit.before')}><JsonBlock value={detail.oldValues} /></Descriptions.Item>
            <Descriptions.Item label={t('audit.after')}><JsonBlock value={detail.newValues} /></Descriptions.Item>
          </Descriptions>
        )}
      </Drawer>
    </Card>
  );
}
