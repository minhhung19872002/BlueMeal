import { Card, Descriptions, Spin, Timeline, Typography } from 'antd';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http } from '@/shared/api/http';
import { fmt } from '@/shared/utils/format';

interface VersionInfo {
  build: { version: string; commit: string; buildTime: string; runtime: string };
  history: { version: string; releaseDate: string; notesVi: string; notesEn?: string }[];
}

/** System version screen: version, build time, commit hash, changelog (NFR-08, SPEC §7.1.10). */
export default function VersionPage() {
  const { t, i18n } = useTranslation('admin');
  const { data, isLoading } = useQuery({ queryKey: ['system-version'], queryFn: async () => (await http.get<VersionInfo>('/system/version')).data });
  if (isLoading || !data) return <Spin />;
  const web = { version: import.meta.env.VITE_APP_VERSION ?? 'dev', commit: import.meta.env.VITE_GIT_COMMIT ?? 'dev' };
  return (
    <Card title={t('version.title')}>
      <Descriptions bordered size="small" column={{ xs: 1, md: 2 }} style={{ marginBottom: 24 }}>
        <Descriptions.Item label={t('version.server')}>{data.build.version}</Descriptions.Item>
        <Descriptions.Item label={t('version.commit')}>
          <Typography.Text code>{data.build.commit}</Typography.Text>
        </Descriptions.Item>
        <Descriptions.Item label={t('version.buildTime')}>{fmt.dateTime(data.build.buildTime)}</Descriptions.Item>
        <Descriptions.Item label={t('version.runtime')}>{data.build.runtime}</Descriptions.Item>
        <Descriptions.Item label={t('version.web')}>{web.version}</Descriptions.Item>
        <Descriptions.Item label={t('version.webCommit')}>
          <Typography.Text code>{web.commit}</Typography.Text>
        </Descriptions.Item>
      </Descriptions>
      <Typography.Title level={5}>{t('version.changelog')}</Typography.Title>
      <Timeline
        items={data.history.map((h) => ({
          children: (
            <div>
              <Typography.Text strong>{h.version}</Typography.Text> <Typography.Text type="secondary">— {fmt.date(h.releaseDate)}</Typography.Text>
              <Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginTop: 4 }}>{i18n.language === 'en' && h.notesEn ? h.notesEn : h.notesVi}</Typography.Paragraph>
            </div>
          ),
        }))}
      />
    </Card>
  );
}
