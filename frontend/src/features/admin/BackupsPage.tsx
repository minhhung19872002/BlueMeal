import { useState } from 'react';
import { Alert, App, Button, Form, Input, Modal, Space, Table, Tag, Typography } from 'antd';
import { CloudDownloadOutlined, DatabaseOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { downloadFile, http, problemMessage } from '@/shared/api/http';
import { fmt } from '@/shared/utils/format';
import { PageHead, Section, XButton } from '@/app/layout/designKit';

interface BackupRow { id: string; type: string; requestedAt: string; finishedAt?: string; sizeBytes?: number; sha256?: string; location?: string; status: string; error?: string; note?: string; requestedBy?: string; available: boolean }
interface BackupList { items: BackupRow[]; totalItems: number; policy: { cron: string; keepDaily: number; keepWeekly: number; keepMonthly: number; mirrorPath?: string } }

const STATUS_COLOR: Record<string, string> = { Queued: 'default', Running: 'processing', Succeeded: 'green', Failed: 'red' };
const size = (b?: number) => (b == null ? '' : b > 1_048_576 ? `${fmt.number(b / 1_048_576, 1)} MB` : `${fmt.number(b / 1024, 0)} KB`);

/** Backup history, manual backup and download after password + MFA re-authentication (SPEC §10.7) — layout: Claude Design BMAdmin. */
export default function BackupsPage() {
  const { t } = useTranslation('admin');
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const [note, setNote] = useState('');
  const [manualOpen, setManualOpen] = useState(false);
  const [dl, setDl] = useState<BackupRow | null>(null);
  const [form] = Form.useForm();
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['backups', page], queryFn: async () => (await http.get<BackupList>('/backups', { params: { page, pageSize: 30 } })).data,
    refetchInterval: (q) => (q.state.data?.items.some((x) => x.status === 'Queued' || x.status === 'Running') ? 3000 : false),
  });
  const manual = async () => {
    try {
      await http.post('/backups', { note: note || null });
      message.success(t('backups.queued'));
      setManualOpen(false);
      setNote('');
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const download = async () => {
    const v = await form.validateFields();
    try {
      const r = await http.post<{ token: string }>(`/backups/${dl!.id}/download-token`, v);
      await downloadFile(`/backups/${dl!.id}/download`, { token: r.data.token }, dl!.location ?? 'backup.tar.enc');
      setDl(null);
      form.resetFields();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  return (
    <div className="bm-page">
      <PageHead title={t('backups.title')}
        sub={data && <>{t('backups.policy', { cron: data.policy.cron, d: data.policy.keepDaily, w: data.policy.keepWeekly, m: data.policy.keepMonthly })} {t('backups.restoreHint')}</>}
        actions={<XButton kind="primary" onClick={() => setManualOpen(true)} data-testid="backup-manual"><DatabaseOutlined />{t('backups.manual')}</XButton>} />
      <Section title={t('backups.history')} bodyClass="bm-x-sec-table">
        <Table<BackupRow> rowKey="id" loading={isFetching && !data} dataSource={data?.items} size="small" scroll={{ x: 'max-content' }}
          pagination={{ current: page, pageSize: 30, total: data?.totalItems, onChange: setPage, showSizeChanger: false }}
          columns={[
            { title: t('backups.requestedAt'), dataIndex: 'requestedAt', width: 150, render: fmt.dateTime },
            { title: t('backups.type'), dataIndex: 'type', width: 110, render: (v) => <Tag>{t(`backups.${v}`)}</Tag> },
            { title: t('backups.size'), dataIndex: 'sizeBytes', width: 100, align: 'right', render: size },
            { title: t('backups.sha'), dataIndex: 'sha256', width: 160, render: (v?: string) => v && <Typography.Text code copyable={{ text: v }}>{v.slice(0, 12)}…</Typography.Text> },
            { title: t('backups.note'), dataIndex: 'note', width: 200, render: (v?: string) => v || '—' },
            { title: t('backups.by'), dataIndex: 'requestedBy', width: 120 },
            { title: t('backups.file'), dataIndex: 'location', width: 260 },
            { title: t('backups.finishedAt'), dataIndex: 'finishedAt', width: 150, render: fmt.dateTime },
            { title: t('backups.status'), dataIndex: 'status', width: 120, render: (v, r) => <Tag color={STATUS_COLOR[v]} title={r.error}>{t(`backups.jobStatus.${v}`)}</Tag> },
            { key: 'a', width: 130, align: 'right', render: (_, r) => (r.available
              ? <Button size="small" icon={<CloudDownloadOutlined />} onClick={() => setDl(r)} data-testid="backup-download">{t('backups.download')}</Button>
              : r.status === 'Succeeded' && <Typography.Text type="secondary">{t('backups.notAvailable')}</Typography.Text>) },
          ]} />
      </Section>
      <Modal open={manualOpen} title={t('backups.manual')} onCancel={() => setManualOpen(false)} onOk={() => void manual()} okButtonProps={{ 'data-testid': 'backup-manual-ok' } as never}>
        <Input.TextArea rows={2} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} placeholder={t('backups.note')} />
      </Modal>
      <Modal open={!!dl} title={t('backups.reauth')} onCancel={() => setDl(null)} onOk={() => void download()} destroyOnHidden>
        <Space direction="vertical" style={{ width: '100%' }}>
          <Typography.Text type="secondary">{dl?.location}</Typography.Text>
          <Form form={form} layout="vertical">
            <Form.Item name="password" label={t('backups.password')} rules={[{ required: true }]}><Input.Password autoComplete="current-password" /></Form.Item>
            <Form.Item name="code" label={t('backups.code')}><Input autoComplete="one-time-code" inputMode="numeric" maxLength={20} /></Form.Item>
          </Form>
          <Alert type="warning" showIcon message={t('backups.reauthHint')} />
        </Space>
      </Modal>
    </div>
  );
}
