import { useState } from 'react';
import { App, Button, Space, Typography, Upload } from 'antd';
import { PaperClipOutlined, UploadOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';

/** Uploads a PDF/image through the authorized files API and keeps only the returned id (SPEC §10.4). */
export function FileField({ value, onChange, ownerType, accept = '.pdf,.png,.jpg,.jpeg,.webp' }: { value?: string | null; onChange?: (v: string | null) => void; ownerType: string; accept?: string }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [name, setName] = useState<string | null>(null);
  const open = async () => {
    const r = await http.get(`/files/${value}`, { params: { inline: true }, responseType: 'blob' });
    const url = URL.createObjectURL(r.data as Blob);
    window.open(url, '_blank', 'noopener');
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  };
  return (
    <Space wrap>
      <Upload
        accept={accept}
        showUploadList={false}
        customRequest={async ({ file, onSuccess, onError }) => {
          try {
            const fd = new FormData();
            fd.append('file', file as File);
            fd.append('ownerType', ownerType);
            const r = await http.post<{ id: string; originalName: string }>('/files', fd);
            setName(r.data.originalName);
            onChange?.(r.data.id);
            onSuccess?.({});
          } catch (e) {
            message.error(problemMessage(e));
            onError?.(e as Error);
          }
        }}
      >
        <Button icon={<UploadOutlined />}>{t('actions.upload')}</Button>
      </Upload>
      {value && (
        <Typography.Link onClick={() => void open()}>
          <PaperClipOutlined /> {name ?? t('actions.view')}
        </Typography.Link>
      )}
      {value && <Button size="small" type="text" onClick={() => onChange?.(null)}>{t('actions.clear')}</Button>}
    </Space>
  );
}
