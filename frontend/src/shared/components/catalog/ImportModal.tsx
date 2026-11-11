import { useState, type ReactNode } from 'react';
import { Alert, App, Button, Modal, Space, Statistic, Steps, Switch, Table, Tag, Typography, Upload } from 'antd';
import { DownloadOutlined, InboxOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { downloadFile, http, problemMessage } from '@/shared/api/http';

interface ImportRow { rowNumber: number; code?: string; action: 'Create' | 'Update' | 'Skip'; errors: string[] }
interface Preview { token: string; totalRows: number; validRows: number; errorRows: number; createCount: number; updateCount: number; rows: ImportRow[] }
type CommitResult = { created: number; updated: number; skipped: number };

interface ImportModalProps {
  resource: string;
  open: boolean;
  onClose: () => void;
  onDone: () => void;
  title: string;
  /** Replaces the generic upload hint (document imports explain how rows are grouped). */
  hint?: string;
  /** Query parameters sent with the upload; the server keeps them with the preview token, so commit uses the same options. */
  params?: Record<string, string | boolean | undefined>;
  /** Option inputs shown above the upload area. */
  extra?: ReactNode;
  doneText?: (r: CommitResult) => string;
}

/**
 * Excel import following the template: upload → server validation per row (preview) → commit (SPEC §7 common features).
 * Nothing is written until the user confirms.
 */
export function ImportModal({ resource, open, onClose, onDone, title, hint, params, extra, doneText }: ImportModalProps) {
  const { t } = useTranslation('catalog');
  const { message } = App.useApp();
  const [preview, setPreview] = useState<Preview | null>(null);
  const [uploading, setUploading] = useState(false);
  const [committing, setCommitting] = useState(false);
  const [skipErrors, setSkipErrors] = useState(true);
  const [onlyErrors, setOnlyErrors] = useState(false);

  const reset = () => {
    setPreview(null);
    setOnlyErrors(false);
  };

  const upload = async (file: File) => {
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      const r = await http.post<Preview>(`/${resource}/import/preview`, fd, { params });
      setPreview(r.data);
      setOnlyErrors(r.data.errorRows > 0);
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setUploading(false);
    }
    return false;
  };

  const commit = async () => {
    if (!preview) return;
    setCommitting(true);
    try {
      const r = await http.post<CommitResult>(`/${resource}/import/commit`, { token: preview.token, skipErrors });
      message.success(doneText ? doneText(r.data) : t('import.done', r.data));
      reset();
      onDone();
      onClose();
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setCommitting(false);
    }
  };

  const rows = preview ? (onlyErrors ? preview.rows.filter((r) => r.errors.length) : preview.rows) : [];

  return (
    <Modal
      open={open}
      title={t('import.title', { name: title })}
      width={820}
      onCancel={() => {
        reset();
        onClose();
      }}
      destroyOnHidden
      footer={
        preview ? (
          <Space wrap>
            <Space>
              <Switch checked={skipErrors} onChange={setSkipErrors} size="small" />
              <Typography.Text>{t('import.skipErrors')}</Typography.Text>
            </Space>
            <Button onClick={reset}>{t('import.chooseAnother')}</Button>
            <Button type="primary" loading={committing} disabled={preview.validRows === 0 || (!skipErrors && preview.errorRows > 0)} onClick={() => void commit()} data-testid="import-commit">
              {t('import.commit', { count: preview.validRows })}
            </Button>
          </Space>
        ) : null
      }
    >
      <Steps size="small" current={preview ? 1 : 0} items={[{ title: t('import.stepUpload') }, { title: t('import.stepPreview') }, { title: t('import.stepCommit') }]} style={{ marginBottom: 16 }} />
      {!preview ? (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Alert type="info" showIcon message={hint ?? t('import.hint')} />
          {extra}
          <Button icon={<DownloadOutlined />} onClick={() => void downloadFile(`/${resource}/import/template`, undefined, `${resource}_template.xlsx`)}>
            {t('common:actions.downloadTemplate')}
          </Button>
          <Upload.Dragger accept=".xlsx" maxCount={1} showUploadList={false} beforeUpload={(f) => upload(f)} disabled={uploading}>
            <p className="ant-upload-drag-icon">
              <InboxOutlined />
            </p>
            <p>{uploading ? t('import.validating') : t('import.drop')}</p>
          </Upload.Dragger>
        </Space>
      ) : (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Space size="large" wrap>
            <Statistic title={t('import.total')} value={preview.totalRows} />
            <Statistic title={t('import.valid')} value={preview.validRows} valueStyle={{ color: '#3f8f5a' }} />
            <Statistic title={t('import.errors')} value={preview.errorRows} valueStyle={{ color: preview.errorRows ? '#c8463d' : undefined }} />
            <Statistic title={t('import.create')} value={preview.createCount} />
            <Statistic title={t('import.update')} value={preview.updateCount} />
          </Space>
          <Space>
            <Switch checked={onlyErrors} onChange={setOnlyErrors} size="small" />
            <Typography.Text>{t('import.onlyErrors')}</Typography.Text>
          </Space>
          <Table<ImportRow>
            size="small"
            rowKey="rowNumber"
            dataSource={rows}
            pagination={{ pageSize: 10, showSizeChanger: false }}
            scroll={{ x: 600 }}
            columns={[
              { title: t('import.row'), dataIndex: 'rowNumber', width: 70 },
              { title: t('common:fields.code'), dataIndex: 'code', width: 140 },
              {
                title: t('import.action'),
                dataIndex: 'action',
                width: 110,
                render: (a: string, r) => (r.errors.length ? <Tag color="red">{t('import.error')}</Tag> : <Tag color={a === 'Create' ? 'green' : 'blue'}>{t('import.' + a.toLowerCase())}</Tag>),
              },
              { title: t('import.messages'), dataIndex: 'errors', render: (e: string[]) => e.map((x, i) => <div key={i}>{x}</div>) },
            ]}
          />
        </Space>
      )}
    </Modal>
  );
}
