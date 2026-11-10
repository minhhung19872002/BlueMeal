import { Table, Tag } from 'antd';
import { useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { fmt, isoDate } from '@/shared/utils/format';
import { Bars, PageHead, Panel, Tile } from '@/app/layout/designKit';

interface Step1Row { id: string; inspectedAt: string; ingredientName: string; ingredientNameEn?: string; receiptNo?: string; result: string }
interface StepRow { id: string; inspectDate: string; dishName: string; dishNameEn?: string; menuCode?: string; result: string }
interface AlertRow { id: string; code: string; messageVi: string; messageEn: string; createdAt: string; status: string }
interface Note { key: string; date: string; step: number; subject: string; result: string }

const DAYS = 7;
const RESULT_COLOR: Record<string, string> = { Pass: 'green', ConditionalPass: 'orange', Fail: 'red' };

/**
 * Food-safety overview (nav "safety-dashboard") — layout: Claude Design BMAnalysis "safety-dashboard".
 * Read-only: built from the existing inspection, sample and alert list APIs.
 */
export default function SafetyDashboardPage() {
  const { t, i18n } = useTranslation('analysis');
  const navigate = useNavigate();
  const { has } = usePermission();
  const en = i18n.language === 'en';
  const today = dayjs();
  const from = isoDate(today.subtract(DAYS - 1, 'day'));
  const to = isoDate(today);
  const canInspect = has('inspection.view');
  const canSample = has('sample.view');
  const canAlert = has('alert.view');
  const list = <T,>(url: string, params: Record<string, unknown>) => async () => (await http.get<PagedResult<T>>(url, { params })).data;

  const s1 = useQuery({ queryKey: ['safety-dash', 'step1', from], enabled: canInspect, queryFn: list<Step1Row>('/inspections/step1', { from, to, pageSize: 200 }) });
  const s2 = useQuery({ queryKey: ['safety-dash', 'step2', from], enabled: canInspect, queryFn: list<StepRow>('/inspections/step2', { from, to, pageSize: 200 }) });
  const s3 = useQuery({ queryKey: ['safety-dash', 'step3', from], enabled: canInspect, queryFn: list<StepRow>('/inspections/step3', { from, to, pageSize: 200 }) });
  const kept = useQuery({ queryKey: ['safety-dash', 'kept'], enabled: canSample, queryFn: list<unknown>('/food-samples', { state: 'kept', pageSize: 1 }) });
  const due = useQuery({ queryKey: ['safety-dash', 'due'], enabled: canSample, queryFn: list<unknown>('/food-samples', { state: 'due', pageSize: 1 }) });
  const certs = useQuery({ queryKey: ['safety-dash', 'certs'], enabled: canAlert, queryFn: list<AlertRow>('/alerts', { category: 'Certificate', pageSize: 20 }) });

  const rows = [
    ...(s1.data?.items ?? []).map((r) => ({ date: dayjs(r.inspectedAt).format('YYYY-MM-DD'), step: 1, subject: `${en && r.ingredientNameEn ? r.ingredientNameEn : r.ingredientName}${r.receiptNo ? ' · ' + r.receiptNo : ''}`, result: r.result, id: r.id })),
    ...(s2.data?.items ?? []).map((r) => ({ date: r.inspectDate, step: 2, subject: `${en && r.dishNameEn ? r.dishNameEn : r.dishName}${r.menuCode ? ' · ' + r.menuCode : ''}`, result: r.result, id: r.id })),
    ...(s3.data?.items ?? []).map((r) => ({ date: r.inspectDate, step: 3, subject: `${en && r.dishNameEn ? r.dishNameEn : r.dishName}${r.menuCode ? ' · ' + r.menuCode : ''}`, result: r.result, id: r.id })),
  ];
  const todayIso = isoDate(today);
  const todayCount = rows.filter((r) => r.date === todayIso).length;
  const notes: Note[] = rows.filter((r) => r.result !== 'Pass').map((r) => ({ key: `${r.step}-${r.id}`, date: r.date, step: r.step, subject: r.subject, result: r.result })).sort((a, b) => b.date.localeCompare(a.date));
  const rate = Array.from({ length: DAYS }, (_, i) => {
    const d = isoDate(today.subtract(DAYS - 1 - i, 'day'))!;
    const day = rows.filter((r) => r.date === d);
    return { d, pct: day.length ? (day.filter((r) => r.result === 'Pass').length / day.length) * 100 : null };
  });
  const openCerts = (certs.data?.items ?? []).filter((a) => a.status !== 'Resolved');

  return (
    <div className="bm-page">
      <PageHead title={t('safety.title')} />
      <div className="bm-x-tiles">
        {canInspect && <Tile label={t('safety.today')} value={fmt.number(todayCount)} extra={t('safety.openLog')} onClick={() => navigate('/safety/inspections')} />}
        {canInspect && <Tile label={t('safety.notPass')} value={fmt.number(notes.length)} extra={t('safety.last7')} bad={notes.length > 0} onClick={() => navigate('/safety/inspections')} />}
        {canSample && <Tile label={t('safety.kept')} value={fmt.number(kept.data?.totalItems ?? 0)} onClick={() => navigate('/safety/samples')} />}
        {canSample && <Tile label={t('safety.due')} value={fmt.number(due.data?.totalItems ?? 0)} extra={(due.data?.totalItems ?? 0) > 0 ? t('safety.dueHint') : ''} bad={(due.data?.totalItems ?? 0) > 0} onClick={() => navigate('/safety/samples?state=due')} />}
        {canAlert && <Tile label={t('safety.certs')} value={fmt.number(openCerts.length)} bad={openCerts.length > 0} onClick={() => navigate('/alerts')} />}
      </div>
      <div className="bm-x-panels">
        {canInspect && (
          <Panel title={t('safety.rate')}>
            <Bars max={100} minWidth={280} items={rate.map((r) => ({ key: r.d, label: fmt.date(r.d).slice(0, 5), text: r.pct == null ? '—' : fmt.percent(r.pct, 0), value: r.pct ?? 0, color: (r.pct ?? 100) < 95 ? 'var(--warn)' : 'var(--primary)' }))} />
          </Panel>
        )}
        {canAlert && (
          <Panel title={t('safety.certTable')}>
            <Table<AlertRow> rowKey="id" size="small" pagination={false} dataSource={openCerts} loading={certs.isLoading} scroll={{ x: 'max-content' }} locale={{ emptyText: t('safety.none') }}
              columns={[
                { title: t('alerts.message'), key: 'm', render: (_, a) => (en ? a.messageEn : a.messageVi) },
                { title: t('alerts.createdAt'), dataIndex: 'createdAt', width: 150, render: fmt.dateTime },
              ]} />
          </Panel>
        )}
        {canInspect && (
          <Panel title={t('safety.attention')} full>
            <Table<Note> rowKey="key" size="small" pagination={false} dataSource={notes} scroll={{ x: 480 }} locale={{ emptyText: t('safety.none') }}
              onRow={() => ({ onClick: () => navigate('/safety/inspections'), style: { cursor: 'pointer' } })}
              columns={[
                { title: t('safety.date'), dataIndex: 'date', width: 110, render: fmt.date },
                { title: t('safety.step'), dataIndex: 'step', width: 200, render: (v: number) => t(`inspection:step${v}`) },
                { title: t('safety.subject'), dataIndex: 'subject' },
                { title: t('safety.result'), dataIndex: 'result', width: 160, render: (v: string) => <Tag color={RESULT_COLOR[v]}>{t(`enums:QualityResult.${v}`, v)}</Tag> },
              ]} />
          </Panel>
        )}
      </div>
    </div>
  );
}
