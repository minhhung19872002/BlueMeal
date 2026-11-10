import { Spin, Table, Typography } from 'antd';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http } from '@/shared/api/http';
import { fmt } from '@/shared/utils/format';
import { PageHead, Section, StaticField } from '@/app/layout/designKit';

interface VersionInfo {
  build: { version: string; commit: string; buildTime: string; runtime: string };
  history: { version: string; releaseDate: string; notesVi: string; notesEn?: string }[];
}

/** System version screen: version, build time, commit hash, changelog (NFR-08, SPEC §7.1.10) — layout: Claude Design BMAdmin. */
export default function VersionPage() {
  const { t, i18n } = useTranslation('admin');
  const { data, isLoading } = useQuery({ queryKey: ['system-version'], queryFn: async () => (await http.get<VersionInfo>('/system/version')).data });
  if (isLoading || !data) return <Spin />;
  const web = { version: import.meta.env.VITE_APP_VERSION ?? 'dev', commit: import.meta.env.VITE_GIT_COMMIT ?? 'dev' };
  return (
    <div className="bm-page">
      <PageHead title={t('version.title')} />
      <Section title={t('version.build')}>
        <div className="bm-x-grid">
          <StaticField label={t('version.server')} value={data.build.version} />
          <StaticField label={t('version.commit')} value={<Typography.Text code>{data.build.commit}</Typography.Text>} />
          <StaticField label={t('version.buildTime')} value={fmt.dateTime(data.build.buildTime)} />
          <StaticField label={t('version.runtime')} value={data.build.runtime} />
          <StaticField label={t('version.web')} value={web.version} />
          <StaticField label={t('version.webCommit')} value={<Typography.Text code>{web.commit}</Typography.Text>} />
        </div>
      </Section>
      <Section title={t('version.changelog')} bodyClass="bm-x-sec-table">
        <Table rowKey="version" dataSource={data.history} size="small" pagination={false} scroll={{ x: 520 }}
          columns={[
            { title: t('version.version'), dataIndex: 'version', width: 110, render: (v) => <b>{v}</b> },
            { title: t('version.date'), dataIndex: 'releaseDate', width: 120, render: fmt.date },
            { title: t('version.notes'), key: 'n', render: (_, h) => <pre className="bm-x-pre">{i18n.language === 'en' && h.notesEn ? h.notesEn : h.notesVi}</pre> },
          ]} />
      </Section>
    </div>
  );
}
