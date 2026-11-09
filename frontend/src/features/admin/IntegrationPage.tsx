import { useState } from 'react';
import { Alert, App, Button, Card, Checkbox, Col, DatePicker, Descriptions, Empty, Form, Input, InputNumber, Modal, Row, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd';
import { ApiOutlined, CloudSyncOutlined, KeyOutlined, PlusOutlined, SendOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';

interface EndpointView {
  id: string; code: string; nameVi: string; nameEn?: string; baseUrl: string; authType: string; timeoutSeconds: number; syncCron?: string; mappingJson: string; isEnabled: boolean; isActive: boolean;
  lastSyncAt?: string; lastSuccessAt?: string; version: number;
  credentials: { apiKeyHeader?: string; hasApiKey: boolean; username?: string; hasPassword: boolean; tokenUrl?: string; clientId?: string; hasClientSecret: boolean; scope?: string };
}
interface SyncSummary { success: boolean; received: number; created: number; updated: number; skipped: number; conflicts: number; error?: string }
interface LogRow { id: string; endpoint?: string; operation: string; startedAt: string; durationMs: number; success: boolean; statusCode?: number; recordsReceived: number; recordsCreated: number; recordsUpdated: number; conflicts: number; requestSummary?: string; error?: string; triggeredBy?: string }
interface ConflictRow { id: string; entityType: string; entityCode: string; detectedAt: string; local: Record<string, unknown>; remote: Record<string, unknown> }
interface ClientRow { id: string; clientId: string; name: string; scopes: string; isActive: boolean; lastUsedAt?: string }

const AUTH_TYPES = ['None', 'ApiKey', 'Basic', 'OAuth2ClientCredentials'];
const SCOPES = ['menu.read', 'nutrition.read', 'food.read'];
const DEFAULT_TOKEN_PATH = '/oauth/token';

/** Flattens a canonical record (nested nutrients) into comparable rows. */
function flatten(o: Record<string, unknown>, prefix = ''): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [k, v] of Object.entries(o ?? {})) {
    if (k === 'modifiedAt') continue;
    if (v && typeof v === 'object' && !Array.isArray(v)) Object.assign(out, flatten(v as Record<string, unknown>, `${prefix}${k}.`));
    else out[prefix + k] = Array.isArray(v) ? JSON.stringify(v) : v === null || v === undefined ? '' : String(v);
  }
  return out;
}

function EndpointForm({ value, onClose, onSaved }: { value: EndpointView | 'new'; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation('integration');
  const { message } = App.useApp();
  const [form] = Form.useForm();
  const isNew = value === 'new';
  const auth = Form.useWatch('authType', form) as string | undefined;
  const init = isNew
    ? { code: '', nameVi: '', baseUrl: 'https://', authType: 'OAuth2ClientCredentials', timeoutSeconds: 30, syncCron: '0 2 * * *', isEnabled: true, isActive: true, credentials: { apiKeyHeader: 'X-Api-Key' } }
    : { ...value, credentials: { apiKeyHeader: value.credentials.apiKeyHeader, username: value.credentials.username, tokenUrl: value.credentials.tokenUrl, clientId: value.credentials.clientId, scope: value.credentials.scope } };
  const stored = (has?: boolean) => (has ? t('ep.stored') : undefined);
  const save = async () => {
    const v = await form.validateFields();
    try {
      if (isNew) await http.post('/integration/endpoints', v);
      else await http.put(`/integration/endpoints/${value.id}`, { ...v, version: value.version });
      message.success(t('common:saved'));
      onSaved();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const c = isNew ? undefined : value.credentials;
  return (
    <Modal open title={isNew ? t('ep.add') : value.code} onCancel={onClose} onOk={() => void save()} width={860} destroyOnHidden okButtonProps={{ 'data-testid': 'ep-save' } as never}>
      <Form form={form} layout="vertical" initialValues={init}>
        <Row gutter={12}>
          <Col xs={24} md={6}><Form.Item name="code" label={t('ep.code')} rules={[{ required: true, max: 50 }]}><Input disabled={!isNew} /></Form.Item></Col>
          <Col xs={24} md={18}><Form.Item name="nameVi" label={t('ep.name')} rules={[{ required: true, max: 200 }]}><Input /></Form.Item></Col>
          <Col xs={24} md={14}><Form.Item name="baseUrl" label={t('ep.baseUrl')} rules={[{ required: true, type: 'url' }]}><Input /></Form.Item></Col>
          <Col xs={12} md={5}><Form.Item name="timeoutSeconds" label={t('ep.timeout')}><InputNumber min={3} max={300} style={{ width: '100%' }} /></Form.Item></Col>
          <Col xs={12} md={5}><Form.Item name="syncCron" label={t('ep.cron')}><Input /></Form.Item></Col>
          <Col xs={24} md={10}>
            <Form.Item name="authType" label={t('ep.authType')}><Select options={AUTH_TYPES.map((a) => ({ value: a, label: t(`ep.auth.${a}`) }))} /></Form.Item>
          </Col>
          <Col xs={12} md={7}><Form.Item name="isEnabled" label={t('ep.enabled')} valuePropName="checked"><Switch /></Form.Item></Col>
          <Col xs={12} md={7}><Form.Item name="isActive" label={t('common:fields.isActive')} valuePropName="checked"><Switch /></Form.Item></Col>
          {auth === 'ApiKey' && (
            <>
              <Col xs={12}><Form.Item name={['credentials', 'apiKeyHeader']} label={t('ep.cred.apiKeyHeader')}><Input /></Form.Item></Col>
              <Col xs={12}><Form.Item name={['credentials', 'apiKey']} label={t('ep.cred.apiKey')}><Input.Password placeholder={stored(c?.hasApiKey)} autoComplete="new-password" /></Form.Item></Col>
            </>
          )}
          {auth === 'Basic' && (
            <>
              <Col xs={12}><Form.Item name={['credentials', 'username']} label={t('ep.cred.username')}><Input autoComplete="off" /></Form.Item></Col>
              <Col xs={12}><Form.Item name={['credentials', 'password']} label={t('ep.cred.password')}><Input.Password placeholder={stored(c?.hasPassword)} autoComplete="new-password" /></Form.Item></Col>
            </>
          )}
          {auth === 'OAuth2ClientCredentials' && (
            <>
              <Col xs={24} md={12}><Form.Item name={['credentials', 'tokenUrl']} label={t('ep.cred.tokenUrl')}><Input placeholder={DEFAULT_TOKEN_PATH} /></Form.Item></Col>
              <Col xs={24} md={12}><Form.Item name={['credentials', 'scope']} label={t('ep.cred.scope')}><Input /></Form.Item></Col>
              <Col xs={12}><Form.Item name={['credentials', 'clientId']} label={t('ep.cred.clientId')}><Input autoComplete="off" /></Form.Item></Col>
              <Col xs={12}><Form.Item name={['credentials', 'clientSecret']} label={t('ep.cred.clientSecret')}><Input.Password placeholder={stored(c?.hasClientSecret)} autoComplete="new-password" /></Form.Item></Col>
            </>
          )}
          <Col xs={24}>
            <Form.Item name="mappingJson" label={t('ep.mapping')} extra={t('ep.mappingHint')}
              rules={[{ validator: (_, v) => { if (!v) return Promise.resolve(); try { JSON.parse(v); return Promise.resolve(); } catch { return Promise.reject(new Error(t('ep.invalidJson'))); } } }]}>
              <Input.TextArea rows={12} style={{ fontFamily: 'ui-monospace, Consolas, monospace', fontSize: 12 }} spellCheck={false} />
            </Form.Item>
          </Col>
        </Row>
      </Form>
    </Modal>
  );
}

function ConflictDiff({ c }: { c: ConflictRow }) {
  const { t } = useTranslation('integration');
  const l = flatten(c.local);
  const r = flatten(c.remote);
  const keys = [...new Set([...Object.keys(l), ...Object.keys(r)])].sort();
  const rows = keys.map((k) => ({ key: k, local: l[k] ?? '', remote: r[k] ?? '', diff: (l[k] ?? '') !== (r[k] ?? '') }));
  return (
    <Table size="small" pagination={false} dataSource={rows.filter((x) => x.diff).concat(rows.filter((x) => !x.diff))} rowKey="key"
      rowClassName={(x) => (x.diff ? 'bm-diff-row' : '')}
      columns={[
        { title: t('conflict.field'), dataIndex: 'key', width: 220 },
        { title: t('conflict.local'), dataIndex: 'local', render: (v, x) => (x.diff ? <Typography.Text type="danger">{v}</Typography.Text> : v) },
        { title: t('conflict.remote'), dataIndex: 'remote', render: (v, x) => (x.diff ? <Typography.Text type="success" strong>{v}</Typography.Text> : v) },
      ]} />
  );
}

export default function IntegrationPage() {
  const { t, i18n } = useTranslation('integration');
  const { message, modal } = App.useApp();
  const { has } = usePermission();
  const [tab, setTab] = useState('endpoints');
  const [editing, setEditing] = useState<EndpointView | 'new' | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [pushFor, setPushFor] = useState<EndpointView | null>(null);
  const [pushRange, setPushRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([dayjs().subtract(1, 'month').startOf('month'), dayjs().subtract(1, 'month').endOf('month')]);
  const [logPage, setLogPage] = useState(1);
  const [clientOpen, setClientOpen] = useState(false);
  const [clientForm] = Form.useForm();

  const endpoints = useQuery({ queryKey: ['int-endpoints'], queryFn: async () => (await http.get<EndpointView[]>('/integration/endpoints')).data });
  const logs = useQuery({ queryKey: ['int-logs', logPage], enabled: tab === 'logs', placeholderData: keepPreviousData, queryFn: async () => (await http.get<PagedResult<LogRow>>('/integration/logs', { params: { page: logPage, pageSize: 30 } })).data });
  const conflicts = useQuery({ queryKey: ['int-conflicts'], queryFn: async () => (await http.get<PagedResult<ConflictRow>>('/integration/conflicts', { params: { pageSize: 100 } })).data });
  const clients = useQuery({ queryKey: ['int-clients'], enabled: tab === 'clients', queryFn: async () => (await http.get<ClientRow[]>('/integration/clients')).data });

  const act = async (key: string, fn: () => Promise<void>) => {
    setBusy(key);
    try { await fn(); } catch (e) { message.error(problemMessage(e)); } finally { setBusy(null); }
  };
  const test = (e: EndpointView) => act(`test-${e.id}`, async () => {
    const r = (await http.post<{ ok: boolean; nutrients: number; error?: string; durationMs: number }>(`/integration/endpoints/${e.id}/test`)).data;
    if (r.ok) message.success(t('ep.testOk', { n: r.nutrients, ms: r.durationMs })); else message.error(t('ep.testFail', { error: r.error }));
  });
  const sync = (e: EndpointView, full: boolean) => act(`sync-${e.id}-${full}`, async () => {
    const [r] = (await http.post<SyncSummary[]>('/integration/sync', { endpointId: e.id, full })).data;
    if (r.success) message.success(t('ep.synced', { ...r })); else message.error(r.error ?? 'error');
    void endpoints.refetch();
    void conflicts.refetch();
  });
  const push = () => act('push', async () => {
    const r = (await http.post<{ accepted: boolean; receiptId?: string; message?: string }>(`/integration/endpoints/${pushFor!.id}/push`, { from: isoDate(pushRange[0]), to: isoDate(pushRange[1]) })).data;
    if (r.accepted) message.success(t('ep.pushed', { id: r.receiptId })); else message.error(r.message ?? 'error');
    setPushFor(null);
  });
  const resolve = (c: ConflictRow, keep: 'local' | 'remote') => act(`res-${c.id}`, async () => {
    await http.post(`/integration/conflicts/${c.id}/resolve`, { keep });
    void conflicts.refetch();
  });
  const showSecret = (clientId: string, secret: string) => modal.success({
    title: t('client.secretOnce'), width: 560,
    content: <Space direction="vertical" style={{ width: '100%' }}><Typography.Text>{t('client.clientId')}: <Typography.Text code copyable>{clientId}</Typography.Text></Typography.Text><Typography.Text code copyable data-testid="client-secret">{secret}</Typography.Text></Space>,
  });
  const createClient = async () => {
    const v = await clientForm.validateFields();
    await act('client', async () => {
      const r = (await http.post<{ clientId: string; clientSecret: string }>('/integration/clients', v)).data;
      setClientOpen(false);
      showSecret(r.clientId, r.clientSecret);
      void clients.refetch();
    });
  };
  const pendingConflicts = conflicts.data?.totalItems ?? 0;

  return (
    <Card>
      <div className="bm-page-header"><Typography.Title level={4}>{t('title')}</Typography.Title></div>
      <Tabs activeKey={tab} onChange={setTab} items={[
        {
          key: 'endpoints', label: t('tabs.endpoints'),
          children: (
            <Space direction="vertical" style={{ width: '100%' }}>
              {has('integration.edit') && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>{t('ep.add')}</Button>}
              {(endpoints.data ?? []).length === 0 && !endpoints.isLoading && <Empty />}
              {(endpoints.data ?? []).map((e) => (
                <Card key={e.id} size="small" title={<Space><ApiOutlined />{e.code} — {i18n.language === 'en' && e.nameEn ? e.nameEn : e.nameVi}{!e.isEnabled && <Tag>{t('common:status.inactive')}</Tag>}</Space>}
                  extra={has('integration.edit') && <a onClick={() => setEditing(e)}>{t('common:actions.edit')}</a>}>
                  <Descriptions size="small" column={{ xs: 1, md: 3 }}>
                    <Descriptions.Item label={t('ep.baseUrl')}>{e.baseUrl}</Descriptions.Item>
                    <Descriptions.Item label={t('ep.authType')}>{t(`ep.auth.${e.authType}`)}</Descriptions.Item>
                    <Descriptions.Item label={t('ep.cron')}>{e.syncCron}</Descriptions.Item>
                    <Descriptions.Item label={t('ep.lastSync')}>{fmt.dateTime(e.lastSyncAt)}</Descriptions.Item>
                    <Descriptions.Item label={t('ep.lastSuccess')}>{fmt.dateTime(e.lastSuccessAt)}</Descriptions.Item>
                  </Descriptions>
                  <Space wrap>
                    {has('integration.edit') && <Button icon={<ApiOutlined />} loading={busy === `test-${e.id}`} onClick={() => void test(e)} data-testid="ep-test">{t('ep.test')}</Button>}
                    {has('integration.sync') && <Button type="primary" icon={<CloudSyncOutlined />} loading={busy === `sync-${e.id}-false`} onClick={() => void sync(e, false)} data-testid="ep-sync">{t('ep.sync')}</Button>}
                    {has('integration.sync') && <Button loading={busy === `sync-${e.id}-true`} onClick={() => void sync(e, true)}>{t('ep.fullSync')}</Button>}
                    {has('integration.sync') && <Button icon={<SendOutlined />} onClick={() => setPushFor(e)}>{t('ep.push')}</Button>}
                  </Space>
                </Card>
              ))}
            </Space>
          ),
        },
        {
          key: 'logs', label: t('tabs.logs'),
          children: (
            <Table<LogRow> rowKey="id" size="small" loading={logs.isFetching} dataSource={logs.data?.items} scroll={{ x: 'max-content' }}
              pagination={{ current: logPage, pageSize: 30, total: logs.data?.totalItems, onChange: setLogPage, showSizeChanger: false }}
              expandable={{ expandedRowRender: (l) => <pre style={{ margin: 0, whiteSpace: 'pre-wrap', fontSize: 12 }}>{l.requestSummary}{l.error ? `\n${t('log.error')}: ${l.error}` : ''}</pre> }}
              columns={[
                { title: t('log.at'), dataIndex: 'startedAt', width: 150, render: fmt.dateTime },
                { title: t('log.endpoint'), dataIndex: 'endpoint', width: 120 },
                { title: t('log.operation'), dataIndex: 'operation', width: 170 },
                { title: t('log.result'), dataIndex: 'success', width: 120, render: (v, l) => <Tag color={v ? 'green' : 'red'}>{v ? t('log.ok') : t('log.fail')}{l.statusCode ? ` ${l.statusCode}` : ''}</Tag> },
                { title: t('log.duration'), dataIndex: 'durationMs', width: 110, align: 'right', render: (v) => fmt.number(v) },
                { title: t('log.received'), dataIndex: 'recordsReceived', width: 80, align: 'right' },
                { title: t('log.created'), dataIndex: 'recordsCreated', width: 80, align: 'right' },
                { title: t('log.updated'), dataIndex: 'recordsUpdated', width: 90, align: 'right' },
                { title: t('log.conflicts'), dataIndex: 'conflicts', width: 90, align: 'right', render: (v) => (v > 0 ? <Tag color="orange">{v}</Tag> : v) },
                { title: t('log.by'), dataIndex: 'triggeredBy', width: 120, render: (v) => v ?? t('log.scheduled') },
              ]} />
          ),
        },
        {
          key: 'conflicts', label: <span data-testid="tab-conflicts">{t('tabs.conflicts')}{pendingConflicts > 0 && <Tag color="orange" style={{ marginInlineStart: 6 }}>{pendingConflicts}</Tag>}</span>,
          children: (
            <Space direction="vertical" style={{ width: '100%' }}>
              <Alert type="info" showIcon message={t('conflict.hint')} />
              {pendingConflicts === 0 && <Empty description={t('conflict.none')} />}
              {(conflicts.data?.items ?? []).map((c) => {
                const fl = flatten(c.local);
                const fr = flatten(c.remote);
                const diff = [...new Set([...Object.keys(fl), ...Object.keys(fr)])].filter((k) => (fl[k] ?? '') !== (fr[k] ?? '')).length;
                return (
                  <Card key={c.id} size="small" data-testid="conflict-card"
                    title={<Space wrap><Tag>{t(`conflict.${c.entityType}`, { defaultValue: c.entityType })}</Tag>{c.entityCode}<Typography.Text type="secondary">{fmt.dateTime(c.detectedAt)} · {t('conflict.diffCount', { n: diff })}</Typography.Text></Space>}
                    extra={has('integration.resolve') && (
                      <Space>
                        <Button loading={busy === `res-${c.id}`} onClick={() => void resolve(c, 'local')} data-testid="conflict-keep-local">{t('conflict.keepLocal')}</Button>
                        <Button type="primary" loading={busy === `res-${c.id}`} onClick={() => void resolve(c, 'remote')} data-testid="conflict-accept-remote">{t('conflict.acceptRemote')}</Button>
                      </Space>
                    )}>
                    <ConflictDiff c={c} />
                  </Card>
                );
              })}
            </Space>
          ),
        },
        {
          key: 'clients', label: t('tabs.clients'),
          children: (
            <Space direction="vertical" style={{ width: '100%' }}>
              <Alert type="info" showIcon message={t('client.doc')} />
              {has('integration.edit') && <Button type="primary" icon={<PlusOutlined />} onClick={() => { clientForm.resetFields(); setClientOpen(true); }}>{t('client.add')}</Button>}
              <Table<ClientRow> rowKey="id" size="small" loading={clients.isFetching} dataSource={clients.data} pagination={false}
                columns={[
                  { title: t('client.name'), dataIndex: 'name' },
                  { title: t('client.clientId'), dataIndex: 'clientId', render: (v) => <Typography.Text code copyable>{v}</Typography.Text> },
                  { title: t('client.scopes'), dataIndex: 'scopes', render: (v: string) => v.split(' ').map((s) => <Tag key={s}>{s}</Tag>) },
                  { title: t('client.active'), dataIndex: 'isActive', width: 100, render: (v) => <Tag color={v ? 'green' : 'default'}>{v ? '✓' : '✗'}</Tag> },
                  { title: t('client.lastUsed'), dataIndex: 'lastUsedAt', width: 150, render: fmt.dateTime },
                  { key: 'a', width: 200, render: (_, c) => has('integration.edit') && (
                    <Space size={4}>
                      <Button size="small" icon={<KeyOutlined />} onClick={() => void act(`rot-${c.id}`, async () => { const r = (await http.post<{ clientId: string; clientSecret: string }>(`/integration/clients/${c.id}/rotate`)).data; showSecret(r.clientId, r.clientSecret); })}>{t('client.rotate')}</Button>
                      <Button size="small" onClick={() => void act(`tog-${c.id}`, async () => { await http.post(`/integration/clients/${c.id}/toggle`); void clients.refetch(); })}>{t('client.toggle')}</Button>
                    </Space>
                  ) },
                ]} />
            </Space>
          ),
        },
      ]} />
      {editing && <EndpointForm value={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); void endpoints.refetch(); }} />}
      <Modal open={!!pushFor} title={t('ep.push')} onCancel={() => setPushFor(null)} onOk={() => void push()} confirmLoading={busy === 'push'}>
        <DatePicker.RangePicker value={pushRange} onChange={(v) => v && setPushRange(v as never)} format={DATE_FORMAT} style={{ width: '100%' }} />
      </Modal>
      <Modal open={clientOpen} title={t('client.add')} onCancel={() => setClientOpen(false)} onOk={() => void createClient()}>
        <Form form={clientForm} layout="vertical" initialValues={{ scopes: ['menu.read'] }}>
          <Form.Item name="name" label={t('client.name')} rules={[{ required: true, max: 200 }]}><Input /></Form.Item>
          <Form.Item name="scopes" label={t('client.scopes')} rules={[{ required: true, type: 'array', min: 1 }]}>
            <Checkbox.Group options={SCOPES.map((s) => ({ value: s, label: `${s} — ${t(`client.scope.${s}`)}` }))} style={{ display: 'flex', flexDirection: 'column', gap: 4 }} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
}
