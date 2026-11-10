import { useMemo, useState } from 'react';
import { Alert, App, Button, DatePicker, Input, InputNumber, Modal, Select, Table, Tag } from 'antd';
import { DownloadOutlined, FileExcelOutlined, PlayCircleOutlined, StarOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { downloadFile, http, postDownload, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect } from '@/shared/components/catalog/inputs';
import { FLabel, PageHead, Panel, XButton } from '@/app/layout/designKit';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';

interface ReportInfo { code: string; nameVi: string; nameEn: string; groupCode?: string; groupVi?: string; groupEn?: string; filters: string[]; modes: { value: string; vi: string; en: string }[] }
interface Column { key: string; title: string; type: string; decimals: number; total: boolean }
interface RunResult { columns: Column[]; rows: Record<string, unknown>[]; totalRows: number; truncated: boolean; note?: string; totals: Record<string, number> }
interface Filter { from?: string; to?: string; warehouseId?: string; customerId?: string; subjectGroupId?: string; foodGroupId?: string; ingredientId?: string; partnerId?: string; mealShiftId?: string; userId?: string; mode?: string; days?: number; q?: string }
interface ExportJob { id: string; reportCode: string; status: string; requestedAt: string; finishedAt?: string; fileName?: string; expiresAt?: string; error?: string }

/** Formats a report cell by column type (Vietnamese number format). */
function cell(v: unknown, c: Column) {
  if (v === null || v === undefined || v === '') return '';
  switch (c.type) {
    case 'money': return fmt.money(Number(v));
    case 'number': return fmt.number(Number(v), c.decimals);
    case 'percent': return fmt.percent(Number(v), c.decimals);
    case 'date': return fmt.date(String(v));
    case 'datetime': return fmt.dateTime(String(v));
    default: return String(v);
  }
}

const LOOKUP: Record<string, { key: keyof Filter; resource: string }> = {
  warehouse: { key: 'warehouseId', resource: 'warehouses' }, customer: { key: 'customerId', resource: 'customers' }, subject: { key: 'subjectGroupId', resource: 'subject-groups' },
  foodGroup: { key: 'foodGroupId', resource: 'food-groups' }, ingredient: { key: 'ingredientId', resource: 'ingredients' }, partner: { key: 'partnerId', resource: 'partners' },
  mealShift: { key: 'mealShiftId', resource: 'meal-shifts' },
};

/** Report center (SPEC §7.9) — layout: Claude Design BMAnalysis "reports" (RPT list left, filters, result table). */
export default function ReportsPage() {
  const { t, i18n } = useTranslation('analysis');
  const { message } = App.useApp();
  const qc = useQueryClient();
  const { has } = usePermission();
  const en = i18n.language === 'en';
  const [params, setParams] = useSearchParams();
  const [code, setCode] = useState<string | undefined>(params.get('code') ?? undefined);
  const [filter, setFilter] = useState<Filter>({ from: isoDate(dayjs().startOf('month')), to: isoDate(dayjs()) });
  const [result, setResult] = useState<RunResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [favName, setFavName] = useState('');
  const [favOpen, setFavOpen] = useState(false);
  const [tab, setTab] = useState(params.get('tab') ?? 'report');

  const catalog = useQuery({ queryKey: ['reports'], queryFn: async () => (await http.get<ReportInfo[]>('/reports')).data });
  const report = catalog.data?.find((r) => r.code === code);
  const favorites = useQuery({ queryKey: ['report-fav', code], enabled: !!code, queryFn: async () => (await http.get<{ name: string; filter: Filter }[]>(`/reports/${code}/favorites`)).data });
  const exports = useQuery({ queryKey: ['report-exports'], enabled: tab === 'exports' && has('report.export'), refetchInterval: 5000, queryFn: async () => (await http.get<ExportJob[]>('/reports/exports')).data });

  const groups = useMemo(() => {
    const m = new Map<string, { title: string; items: ReportInfo[] }>();
    for (const r of catalog.data ?? []) {
      const k = r.groupCode ?? '-';
      if (!m.has(k)) m.set(k, { title: (en ? r.groupEn : r.groupVi) ?? '-', items: [] });
      m.get(k)!.items.push(r);
    }
    return [...m.entries()];
  }, [catalog.data, en]);

  const select = (c: string) => { setCode(c); setResult(null); setParams({ code: c }); setTab('report'); };
  const run = async () => {
    if (!code) return;
    setBusy(true);
    try { setResult((await http.post<RunResult>(`/reports/${code}/run`, filter)).data); } catch (e) { message.error(problemMessage(e)); } finally { setBusy(false); }
  };
  const exportExcel = async () => {
    if (!code) return;
    setBusy(true);
    try {
      if (!(await postDownload(`/reports/${code}/export`, filter, `${code}.xlsx`))) {
        message.info(t('reports.queued'));
        void qc.invalidateQueries({ queryKey: ['report-exports'] });
      }
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const download = (j: ExportJob) => downloadFile(`/reports/exports/${j.id}/download`, undefined, j.fileName ?? `${j.reportCode}.xlsx`);
  const saveFavorite = async () => {
    const list = [...(favorites.data ?? []).filter((f) => f.name !== favName), { name: favName, filter }];
    try {
      await http.put(`/reports/${code}/favorites`, list);
      setFavOpen(false);
      setFavName('');
      void favorites.refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const set = (p: Partial<Filter>) => setFilter((f) => ({ ...f, ...p }));
  const reportName = report ? `${report.code} · ${en ? report.nameEn : report.nameVi}` : '';

  const filterCard = report && (
    <div className="bm-x-form">
      {report.filters.includes('dateRange') && (
        <FLabel label={t('reports.f.dateRange')} wide>
          <DatePicker.RangePicker format={DATE_FORMAT} value={[filter.from ? dayjs(filter.from) : null, filter.to ? dayjs(filter.to) : null]}
            onChange={(v) => set({ from: isoDate(v?.[0]), to: isoDate(v?.[1]) })} aria-label={t('reports.f.dateRange')} />
        </FLabel>
      )}
      {report.filters.filter((f) => LOOKUP[f]).map((f) => (
        <FLabel key={f} label={t(`reports.f.${f}`)}>
          <LookupSelect resource={LOOKUP[f].resource} placeholder={t(`reports.f.${f}`)} value={filter[LOOKUP[f].key] as string | undefined}
            onChange={(v) => set({ [LOOKUP[f].key]: v ?? undefined })} data-testid={`rf-${f}`} />
        </FLabel>
      ))}
      {report.filters.includes('mode') && report.modes.length > 0 && (
        <FLabel label={t('reports.f.mode')}>
          <Select value={filter.mode ?? report.modes[0].value} onChange={(v) => set({ mode: v })} options={report.modes.map((m) => ({ value: m.value, label: en ? m.en : m.vi }))} />
        </FLabel>
      )}
      {report.filters.includes('days') && <FLabel label={t('reports.f.days')}><InputNumber min={0} max={365} value={filter.days} onChange={(v) => set({ days: v ?? undefined })} /></FLabel>}
      {report.filters.includes('q') && <FLabel label={t('reports.f.q')}><Input allowClear value={filter.q} onChange={(e) => set({ q: e.target.value || undefined })} /></FLabel>}
      {(favorites.data?.length ?? 0) > 0 && (
        <FLabel label={t('reports.favorites')}>
          <Select placeholder={t('reports.favorites')} onChange={(n) => { const f = favorites.data!.find((x) => x.name === n); if (f) setFilter(f.filter); }}
            options={favorites.data!.map((f) => ({ value: f.name, label: f.name }))} />
        </FLabel>
      )}
    </div>
  );

  const columns = (result?.columns ?? []).map((c) => ({
    title: c.title, dataIndex: c.key, key: c.key, align: ['money', 'number', 'percent'].includes(c.type) ? ('right' as const) : undefined,
    render: (v: unknown) => cell(v, c), className: ['money', 'number', 'percent'].includes(c.type) ? 'bm-num' : undefined,
  }));

  const exportsCount = exports.data?.length;
  const actions = (
    <>
      <XButton kind={tab === 'report' ? 'soft' : 'default'} onClick={() => setTab('report')} aria-pressed={tab === 'report'}>{t('reports.title')}</XButton>
      {has('report.export') && (
        <XButton kind={tab === 'exports' ? 'soft' : 'default'} onClick={() => setTab('exports')} aria-pressed={tab === 'exports'}>
          {t('reports.exports')}{exportsCount != null ? ` (${exportsCount})` : ''}
        </XButton>
      )}
      {tab === 'report' && report && (
        <>
          <XButton onClick={() => setFavOpen(true)}><StarOutlined />{t('reports.saveFavorite')}</XButton>
          {has('report.export') && <XButton disabled={busy} onClick={() => void exportExcel()} data-testid="report-export"><FileExcelOutlined />{t('reports.export')}</XButton>}
          <Button type="primary" icon={<PlayCircleOutlined />} loading={busy} onClick={() => void run()} data-testid="report-run">{t('reports.run')}</Button>
        </>
      )}
    </>
  );

  return (
    <div className="bm-page">
      <PageHead title={t('reports.title')} actions={actions} />
      <div className="bm-x-layout">
        <nav className="bm-x-side" aria-label={t('reports.title')}>
          {groups.map(([k, g]) => (
            <div key={k}>
              <div className="bm-x-side-g">{g.title}</div>
              {g.items.map((r) => (
                <button key={r.code} type="button" className={r.code === code ? 'bm-x-side-i is-on' : 'bm-x-side-i'} aria-current={r.code === code ? 'true' : undefined} onClick={() => select(r.code)}>
                  <span className="bm-x-side-code">{r.code}</span><span className="bm-x-side-name">{en ? r.nameEn : r.nameVi}</span>
                </button>
              ))}
            </div>
          ))}
        </nav>
        <div className="bm-x-main">
          {tab === 'exports' && has('report.export') ? (
            <Panel title={t('reports.exportsTitle')} full>
              <Table<ExportJob> rowKey="id" dataSource={exports.data} loading={exports.isFetching && !exports.data} size="small" pagination={false} scroll={{ x: 'max-content' }}
                columns={[
                  { title: t('reports.report'), dataIndex: 'reportCode', width: 100 },
                  { title: t('reports.status'), dataIndex: 'status', width: 130, render: (v) => <Tag color={v === 'Succeeded' ? 'green' : v === 'Failed' ? 'red' : 'processing'}>{t(`reports.jobStatus.${v}`)}</Tag> },
                  { title: t('reports.requestedAt'), dataIndex: 'requestedAt', width: 150, render: fmt.dateTime },
                  { title: t('reports.finishedAt'), dataIndex: 'finishedAt', width: 150, render: fmt.dateTime },
                  { title: t('reports.expiresAt'), dataIndex: 'expiresAt', width: 150, render: fmt.dateTime },
                  { title: t('reports.error'), dataIndex: 'error' },
                  { key: 'd', width: 120, render: (_, j) => j.status === 'Succeeded' && <Button size="small" icon={<DownloadOutlined />} onClick={() => void download(j)}>{t('reports.download')}</Button> },
                ]} />
            </Panel>
          ) : !report ? (
            <div className="bm-x-empty">{t('reports.pick')}</div>
          ) : (
            <>
              <h2 className="bm-x-rpt-title">{reportName}</h2>
              {filterCard}
              {result?.note && <Alert type="info" showIcon message={result.note} />}
              {result?.truncated && <Alert type="warning" showIcon message={t('reports.truncated', { shown: result.rows.length, total: result.totalRows })} />}
              {!result ? (
                <div className="bm-x-empty">{t('reports.runHint', { name: reportName })}</div>
              ) : (
                <Panel title={filter.from || filter.to ? `${reportName} · ${fmt.date(filter.from)} – ${fmt.date(filter.to)}` : reportName} full>
                  <Table rowKey={(_, i) => String(i)} dataSource={result.rows} columns={columns} size="small" scroll={{ x: 'max-content' }} pagination={{ pageSize: 50, showTotal: (n) => t('reports.rows', { n }) }}
                    data-testid="report-table"
                    summary={() => Object.keys(result.totals).length === 0 ? null : (
                      <Table.Summary.Row className="bm-x-sum">
                        {result.columns.map((c, i) => (
                          <Table.Summary.Cell key={c.key} index={i} align={c.total ? 'right' : undefined}>
                            {i === 0 ? <b>{t('reports.total')}</b> : c.total ? <b>{cell(result.totals[c.key], c)}</b> : null}
                          </Table.Summary.Cell>
                        ))}
                      </Table.Summary.Row>
                    )} />
                </Panel>
              )}
            </>
          )}
        </div>
      </div>
      <Modal open={favOpen} title={t('reports.saveFavorite')} onCancel={() => setFavOpen(false)} onOk={() => void saveFavorite()} okButtonProps={{ disabled: !favName.trim() }}>
        <Input maxLength={100} value={favName} onChange={(e) => setFavName(e.target.value)} placeholder={t('reports.favoriteName')} />
      </Modal>
    </div>
  );
}
