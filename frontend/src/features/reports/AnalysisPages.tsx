import { useState } from 'react';
import { Alert, App, Button, Card, Checkbox, Col, DatePicker, Form, Grid, Input, InputNumber, Modal, Radio, Row, Select, Space, Table, Tabs, Tag, Typography } from 'antd';
import { CheckOutlined, FileExcelOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, postDownload, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect } from '@/shared/components/catalog/inputs';
import { Bars, Donut, FLabel, HBars, PageHead, Panel, PillTabs, Tile, XButton, type HBarItem } from '@/app/layout/designKit';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';

// ================================================================== nutrition analysis (SPEC §7.5) — layout: Claude Design BMAnalysis

interface NutrientRow { code: string; nameVi: string; nameEn?: string; unit: string; group: string; decimals: number; value: number; min?: number; max?: number; target?: number; pct?: number; belowMin: boolean; aboveMax: boolean }
interface Analysis {
  scope: string; title: string; menuCount: number; energyKcal: number; targetEnergyKcal?: number; energyPct?: number; proteinPct: number; lipidPct: number; glucidPct: number; animalProteinPct: number;
  nutrients: NutrientRow[]; trend: { date: string; energy: number; target?: number; pct?: number; menus: number }[];
  weekly: { period: string; energy: number; target?: number; proteinPct: number; lipidPct: number; glucidPct: number; menus: number }[];
  monthly: { period: string; energy: number; target?: number; proteinPct: number; lipidPct: number; glucidPct: number; menus: number }[];
  warnings: { code: string; level: string; count: number; messageVi: string; messageEn: string }[];
}
interface Query { scope: 'dish' | 'menu' | 'range'; dishId?: string; menuId?: string; from?: string; to?: string; customerId?: string; subjectGroupId?: string; mealShiftId?: string; recommendationId?: string }

function QueryForm({ value, onChange }: { value: Query; onChange: (q: Query) => void }) {
  const { t } = useTranslation('analysis');
  const set = (p: Partial<Query>) => onChange({ ...value, ...p });
  return (
    <>
      <FLabel label={t('nutrition.scope')} wide>
        <Radio.Group value={value.scope} onChange={(e) => set({ scope: e.target.value })} optionType="button"
          options={[{ value: 'range', label: t('nutrition.range') }, { value: 'menu', label: t('nutrition.menu') }, { value: 'dish', label: t('nutrition.dish') }]} />
      </FLabel>
      {value.scope === 'range' && (
        <>
          <FLabel label={t('nutrition.range')} wide>
            <DatePicker.RangePicker format={DATE_FORMAT} value={[value.from ? dayjs(value.from) : null, value.to ? dayjs(value.to) : null]} onChange={(v) => set({ from: isoDate(v?.[0]), to: isoDate(v?.[1]) })} />
          </FLabel>
          <FLabel label={t('reports.f.customer')}><LookupSelect resource="customers" placeholder={t('reports.f.customer')} value={value.customerId} onChange={(v) => set({ customerId: v ?? undefined })} /></FLabel>
          <FLabel label={t('reports.f.subject')}><LookupSelect resource="subject-groups" placeholder={t('reports.f.subject')} value={value.subjectGroupId} onChange={(v) => set({ subjectGroupId: v ?? undefined })} /></FLabel>
          <FLabel label={t('reports.f.mealShift')}><LookupSelect resource="meal-shifts" placeholder={t('reports.f.mealShift')} value={value.mealShiftId} onChange={(v) => set({ mealShiftId: v ?? undefined })} /></FLabel>
        </>
      )}
      {value.scope === 'menu' && <FLabel label={t('nutrition.menu')} wide><MenuPicker value={value.menuId} onChange={(v) => set({ menuId: v })} /></FLabel>}
      {value.scope === 'dish' && (
        <>
          <FLabel label={t('nutrition.dish')} wide><LookupSelect resource="dishes" placeholder={t('nutrition.dish')} value={value.dishId} onChange={(v) => set({ dishId: v ?? undefined })} /></FLabel>
          <FLabel label={t('nutrition.recommendation')} wide><LookupSelect resource="recommendations" placeholder={t('nutrition.recommendation')} value={value.recommendationId} onChange={(v) => set({ recommendationId: v ?? undefined })} /></FLabel>
        </>
      )}
    </>
  );
}

function MenuPicker({ value, onChange }: { value?: string; onChange: (v?: string) => void }) {
  const { t } = useTranslation('analysis');
  const [q, setQ] = useState('');
  const { data, isFetching } = useQuery({
    queryKey: ['menu-picker', q],
    queryFn: async () => (await http.get<{ items: { id: string; code: string; menuDate: string; shift: string; customer?: string }[] }>('/menus', { params: { q: q || undefined, pageSize: 30, from: isoDate(dayjs().subtract(60, 'day')), to: isoDate(dayjs().add(60, 'day')) } })).data.items,
  });
  return (
    <Select showSearch allowClear filterOption={false} onSearch={setQ} loading={isFetching} value={value} onChange={onChange} placeholder={t('nutrition.menu')}
      options={(data ?? []).map((m) => ({ value: m.id, label: `${m.code} · ${fmt.date(m.menuDate)} · ${m.shift}${m.customer ? ' · ' + m.customer : ''}` }))} />
  );
}

// P / L / G colours of the design donut (Đạm = primary, Béo = warning, Bột đường = info — DESIGN_HANDOFF §6)
const P_COLOR = 'var(--primary)';
const L_COLOR = 'var(--warn)';
const G_COLOR = 'var(--info)';
const toneOf = (n: { belowMin: boolean; aboveMax: boolean }): HBarItem['tone'] => (n.belowMin ? 'low' : n.aboveMax ? 'high' : 'good');

function AnalysisView({ data }: { data: Analysis }) {
  const { t, i18n } = useTranslation('analysis');
  const en = i18n.language === 'en';
  const [period, setPeriod] = useState('weekly');
  const name = (n: NutrientRow) => (en && n.nameEn ? n.nameEn : n.nameVi);
  if (data.menuCount === 0) return <div className="bm-x-empty">{t('nutrition.empty')}</div>;
  const days = data.trend.filter((p) => p.pct != null);
  const scale = Math.max(125, ...days.map((p) => p.pct ?? 0));
  const okDays = days.filter((p) => (p.pct ?? 0) >= 90 && (p.pct ?? 0) <= 110).length;
  const dense = days.length > 14;
  const adequacy: HBarItem[] = data.nutrients.filter((n) => n.pct != null).map((n) => {
    const tone = toneOf(n);
    const target = n.target != null ? ` / ${fmt.number(n.target, n.decimals)}` : '';
    return { key: n.code, name: name(n), width: (n.pct ?? 0) / 1.4, tone, status: t(`nutrition.st.${tone}`), text: `${fmt.number(n.value, n.decimals)}${target} ${n.unit} · ${fmt.percent(n.pct, 0)}` };
  });
  const periods = period === 'weekly' ? data.weekly : data.monthly;
  return (
    <>
      <div className="bm-x-tiles">
        <Tile label={t('nutrition.energy')} value={`${fmt.number(data.energyKcal, 0)} kcal`} />
        <Tile label={t('nutrition.target')} value={data.targetEnergyKcal == null ? '—' : `${fmt.number(data.targetEnergyKcal, 0)} kcal`} />
        <Tile label={t('nutrition.pct')} value={data.energyPct == null ? '—' : fmt.percent(data.energyPct)} bad={data.energyPct != null && (data.energyPct < 90 || data.energyPct > 110)} />
        {days.length > 0
          ? <Tile label={t('nutrition.daysOk')} value={`${okDays}/${days.length}`} extra={t('nutrition.daysOkHint')} bad={okDays < days.length * 0.7} />
          : <Tile label={t('nutrition.menus')} value={fmt.number(data.menuCount)} />}
      </div>
      <div className="bm-x-panels">
        {days.length > 1 && (
          <Panel title={t('nutrition.byDay')} full>
            <Bars max={scale} refValue={100} refLabel="100%" minWidth={Math.max(280, days.length * 30)}
              items={days.map((p, i) => ({ key: p.date, label: dense && i % 3 ? '' : fmt.date(p.date).slice(0, 5), text: dense ? '' : fmt.percent(p.pct, 0), value: p.pct ?? 0, color: (p.pct ?? 0) < 90 ? 'var(--warn)' : 'var(--primary)' }))} />
          </Panel>
        )}
        <Panel title={t('nutrition.plg')}>
          <Donut label={t('nutrition.plg')} center="P : L : G" parts={[
            { name: t('nutrition.part.p'), color: P_COLOR, pct: data.proteinPct, text: fmt.percent(data.proteinPct) },
            { name: t('nutrition.part.l'), color: L_COLOR, pct: data.lipidPct, text: fmt.percent(data.lipidPct) },
            { name: t('nutrition.part.g'), color: G_COLOR, pct: data.glucidPct, text: fmt.percent(data.glucidPct) },
          ]} />
          <div className="bm-x-pad bm-x-sub">{t('nutrition.animal')}: <b>{fmt.percent(data.animalProteinPct)}</b></div>
        </Panel>
        {adequacy.length > 0 && <Panel title={t('nutrition.adequacy')}><HBars items={adequacy} /></Panel>}
        <Panel title={t('nutrition.table')} full>
          <Table<NutrientRow> rowKey="code" dataSource={data.nutrients} size="small" pagination={false} scroll={{ x: 'max-content' }}
            columns={[
              { title: t('nutrition.nutrient'), key: 'n', render: (_, n) => name(n) },
              { title: t('nutrition.unit'), dataIndex: 'unit', width: 70 },
              { title: t('nutrition.value'), dataIndex: 'value', width: 110, align: 'right', render: (v, n) => fmt.number(v, n.decimals) },
              { title: t('nutrition.min'), dataIndex: 'min', width: 100, align: 'right', render: (v, n) => fmt.number(v, n.decimals) },
              { title: t('nutrition.max'), dataIndex: 'max', width: 100, align: 'right', render: (v, n) => fmt.number(v, n.decimals) },
              { title: t('nutrition.pct'), dataIndex: 'pct', width: 110, align: 'right', render: (v, n) => <span className={n.belowMin || n.aboveMax ? 'bm-x-num-bad' : undefined}>{fmt.percent(v)}</span> },
            ]} />
        </Panel>
        {(data.weekly.length > 0 || data.monthly.length > 0) && (
          <Panel title={t(`nutrition.${period}`)} full extra={<PillTabs value={period} onChange={setPeriod} label={t('nutrition.period')} items={[{ key: 'weekly', label: t('nutrition.weekly') }, { key: 'monthly', label: t('nutrition.monthly') }]} />}>
            <Table rowKey="period" dataSource={periods} size="small" pagination={false} scroll={{ x: 'max-content' }}
              columns={[
                { title: t('nutrition.period'), dataIndex: 'period' },
                { title: t('nutrition.menus'), dataIndex: 'menus', align: 'right' },
                { title: t('nutrition.energy'), dataIndex: 'energy', align: 'right', render: (v) => fmt.number(v, 0) },
                { title: t('nutrition.target'), dataIndex: 'target', align: 'right', render: (v) => fmt.number(v, 0) },
                { title: 'P %', dataIndex: 'proteinPct', align: 'right', render: (v) => fmt.number(v, 1) },
                { title: 'L %', dataIndex: 'lipidPct', align: 'right', render: (v) => fmt.number(v, 1) },
                { title: 'G %', dataIndex: 'glucidPct', align: 'right', render: (v) => fmt.number(v, 1) },
              ]} />
          </Panel>
        )}
        {data.warnings.length > 0 && (
          <Panel title={t('nutrition.warnings')} full>
            <Table rowKey={(w) => w.code + w.level} dataSource={data.warnings} size="small" pagination={false} scroll={{ x: 'max-content' }}
              columns={[
                { title: t('alerts.level'), dataIndex: 'level', width: 110, render: (v) => <Tag color={v === 'Critical' ? 'red' : v === 'Warning' ? 'orange' : 'blue'}>{v}</Tag> },
                { title: t('alerts.code'), dataIndex: 'code', width: 160 },
                { title: t('alerts.message'), key: 'm', render: (_, w) => (en ? w.messageEn : w.messageVi) },
                { title: t('nutrition.count'), dataIndex: 'count', width: 90, align: 'right' },
              ]} />
          </Panel>
        )}
      </div>
    </>
  );
}

export function NutritionAnalysisPage() {
  const { t } = useTranslation('analysis');
  const { message } = App.useApp();
  const { has } = usePermission();
  const [compare, setCompare] = useState(false);
  const init: Query = { scope: 'range', from: isoDate(dayjs().subtract(6, 'day')), to: isoDate(dayjs()) };
  const [qa, setQa] = useState<Query>(init);
  const [qb, setQb] = useState<Query>(init);
  const [run, setRun] = useState<{ a?: Query; b?: Query }>({});
  const fetcher = (q?: Query) => async () => (await http.post<Analysis>('/analysis/nutrition', q)).data;
  const a = useQuery({ queryKey: ['analysis', run.a], enabled: !!run.a, queryFn: fetcher(run.a), placeholderData: keepPreviousData });
  const b = useQuery({ queryKey: ['analysis', run.b], enabled: !!run.b, queryFn: fetcher(run.b), placeholderData: keepPreviousData });
  const error = a.error ?? b.error;
  const busy = a.isFetching || b.isFetching;
  return (
    <div className="bm-page">
      <PageHead title={t('nutrition.title')} sub={compare ? t('nutrition.compareHint') : undefined} actions={
        <>
          <XButton kind={compare ? 'soft' : 'default'} aria-pressed={compare} onClick={() => setCompare(!compare)}>{compare ? t('nutrition.compareOff') : t('nutrition.compare')}</XButton>
          {has('nutrition.export') && qa.scope === 'range' && (
            <XButton onClick={() => void postDownload('/analysis/nutrition/export', qa).catch((e) => message.error(problemMessage(e)))}><FileExcelOutlined />{t('reports.export')}</XButton>
          )}
        </>
      } />
      <div className="bm-x-form">
        {compare && <div className="bm-x-form-tag"><Tag color="blue">{t('nutrition.a')}</Tag></div>}
        <QueryForm value={qa} onChange={setQa} />
        {compare && <div className="bm-x-form-tag"><Tag color="orange">{t('nutrition.b')}</Tag></div>}
        {compare && <QueryForm value={qb} onChange={setQb} />}
        <div className="bm-x-form-acts">
          <Button type="primary" onClick={() => setRun({ a: { ...qa }, b: compare ? { ...qb } : undefined })} loading={busy} data-testid="analysis-run">{t('nutrition.analyze')}</Button>
        </div>
      </div>
      {error && <Alert type="error" showIcon message={problemMessage(error)} />}
      {!a.data && !busy && !error && <div className="bm-x-empty">{t('nutrition.pickHint')}</div>}
      {compare && a.data && <div className="bm-x-form-tag"><Tag color="blue">{t('nutrition.a')}</Tag>{a.data.title}</div>}
      {a.data && <AnalysisView data={a.data} />}
      {compare && b.data && <div className="bm-x-form-tag"><Tag color="orange">{t('nutrition.b')}</Tag>{b.data.title}</div>}
      {compare && b.data && <AnalysisView data={b.data} />}
    </div>
  );
}

// ================================================================== alert center (SPEC §7.6)

interface AlertRow { id: string; code: string; level: string; category: string; messageVi: string; messageEn: string; link?: string; status: string; createdAt: string; resolvedAt?: string; resolutionNote?: string }
const LEVEL_COLOR: Record<string, string> = { Critical: 'red', Warning: 'orange', Info: 'blue' };

export function AlertsPage() {
  const { t, i18n } = useTranslation('analysis');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const screens = Grid.useBreakpoint();
  const [tab, setTab] = useState('open');
  const [filters, setFilters] = useState<{ level?: string; category?: string; q?: string }>({});
  const [page, setPage] = useState(1);
  const [resolving, setResolving] = useState<{ row: AlertRow; ack: boolean } | null>(null);
  const [note, setNote] = useState('');
  const params = { page, pageSize: 50, status: tab === 'resolved' ? 'Resolved' : undefined, ...filters };
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['alerts', params], enabled: tab !== 'config',
    queryFn: async () => (await http.get<{ items: AlertRow[]; totalItems: number; counts: { level: string; count: number }[] }>('/alerts', { params })).data, placeholderData: keepPreviousData,
  });
  const resolve = async () => {
    try {
      await http.post(`/alerts/${resolving!.row.id}/resolve`, { note: note || null, acknowledgeOnly: resolving!.ack });
      setResolving(null);
      setNote('');
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const runDaily = async () => {
    try {
      const r = await http.post<{ created: number; resolved: number }>('/alerts/run-daily');
      message.success(t('alerts.ran', r.data));
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const list = (
    <>
      <div className="bm-toolbar">
        <Select allowClear placeholder={t('alerts.level')} value={filters.level} onChange={(v) => { setFilters({ ...filters, level: v }); setPage(1); }} style={{ width: 150 }}
          options={['Critical', 'Warning', 'Info'].map((l) => ({ value: l, label: t(`nutrition:level.${l}`) }))} />
        <Select allowClear placeholder={t('alerts.category')} value={filters.category} onChange={(v) => { setFilters({ ...filters, category: v }); setPage(1); }} style={{ width: 170 }}
          options={['Stock', 'Certificate', 'Sample', 'Menu'].map((c) => ({ value: c, label: t(`alerts.cat.${c}`) }))} />
        <Input.Search allowClear placeholder={t('reports.f.q')} onSearch={(v) => { setFilters({ ...filters, q: v || undefined }); setPage(1); }} style={{ width: screens.md ? 240 : '100%' }} />
        <Space>{(data?.counts ?? []).map((c) => <Tag key={c.level} color={LEVEL_COLOR[c.level]}>{t(`nutrition:level.${c.level}`)}: {c.count}</Tag>)}</Space>
      </div>
      <Table<AlertRow> rowKey="id" loading={isFetching} dataSource={data?.items} size="middle" scroll={{ x: 'max-content' }}
        pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false }}
        columns={[
          { title: t('alerts.level'), dataIndex: 'level', width: 120, render: (v) => <Tag color={LEVEL_COLOR[v]}>{t(`nutrition:level.${v}`)}</Tag> },
          { title: t('alerts.code'), dataIndex: 'code', width: 170 },
          { title: t('alerts.category'), dataIndex: 'category', width: 130, render: (v: string) => t(`alerts.cat.${v}`, { defaultValue: v }) as string },
          { title: t('alerts.message'), key: 'm', render: (_, a) => (i18n.language === 'en' ? a.messageEn : a.messageVi) },
          { title: t('alerts.createdAt'), dataIndex: 'createdAt', width: 150, render: fmt.dateTime },
          { title: t('alerts.status'), dataIndex: 'status', width: 130, render: (v) => t(`alerts.status_.${v}`) },
          ...(tab === 'resolved' ? [{ title: t('alerts.note'), dataIndex: 'resolutionNote', width: 200 }] : []),
          { key: 'a', width: 230, render: (_, a) => (
            <Space size={4}>
              {a.link && <Button size="small" onClick={() => navigate(a.link!)}>{t('alerts.goto')}</Button>}
              {a.status !== 'Resolved' && has('alert.resolve') && a.status === 'Open' && <Button size="small" onClick={() => setResolving({ row: a, ack: true })}>{t('alerts.acknowledge')}</Button>}
              {a.status !== 'Resolved' && has('alert.resolve') && <Button size="small" type="primary" icon={<CheckOutlined />} onClick={() => setResolving({ row: a, ack: false })} data-testid="alert-resolve">{t('alerts.resolve')}</Button>}
            </Space>
          ) },
        ]} />
    </>
  );
  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t('alerts.title')}</Typography.Title>
        {has('alert.config') && <Button icon={<ThunderboltOutlined />} onClick={() => void runDaily()} data-testid="alerts-run">{t('alerts.runDaily')}</Button>}
      </div>
      <Tabs activeKey={tab} onChange={(k) => { setTab(k); setPage(1); }} items={[
        { key: 'open', label: t('alerts.open'), children: list },
        { key: 'resolved', label: t('alerts.resolvedTab'), children: list },
        ...(has('alert.config') ? [{ key: 'config', label: t('alerts.config'), children: <ThresholdForm /> }] : []),
      ]} />
      <Modal open={!!resolving} title={resolving?.ack ? t('alerts.acknowledge') : t('alerts.resolve')} onCancel={() => setResolving(null)} onOk={() => void resolve()} okButtonProps={{ 'data-testid': 'alert-resolve-ok' } as never}>
        <Input.TextArea rows={3} value={note} onChange={(e) => setNote(e.target.value)} placeholder={t('alerts.note')} maxLength={1000} />
      </Modal>
    </Card>
  );
}

const THRESHOLDS = ['energyWarnLowPct', 'energyWarnHighPct', 'energyCriticalLowPct', 'energyCriticalHighPct', 'costCriticalOverPct', 'repeatDishDays', 'certExpiryWarnDays', 'stockExpiryWarnDays'] as const;

function ThresholdForm() {
  const { t } = useTranslation('analysis');
  const { message } = App.useApp();
  const [form] = Form.useForm();
  const { data, refetch } = useQuery({ queryKey: ['alert-config'], queryFn: async () => (await http.get<Record<string, unknown>>('/alerts/config')).data });
  const save = async () => {
    const v = await form.validateFields();
    try {
      await http.put('/alerts/config', { ...data, ...v });
      message.success(t('common:saved'));
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  if (!data) return <Card loading />;
  return (
    <Form form={form} layout="vertical" initialValues={data} style={{ maxWidth: 720 }}>
      <Row gutter={16}>
        {THRESHOLDS.map((k) => (
          <Col xs={24} sm={12} key={k}><Form.Item name={k} label={t(`alerts.cfg.${k}`)} rules={[{ required: true }]}><InputNumber min={0} max={400} style={{ width: '100%' }} /></Form.Item></Col>
        ))}
        <Col xs={24}><Form.Item name="plgEnabled" valuePropName="checked"><Checkbox>{t('alerts.cfg.plgEnabled')}</Checkbox></Form.Item></Col>
      </Row>
      <Button type="primary" onClick={() => void save()}>{t('common:actions.save')}</Button>
    </Form>
  );
}

// ================================================================== meal cost sheet (SPEC §7.7) — layout: Claude Design BMAnalysis

interface CostRow { period: string; firstDate: string; customerId?: string; customer?: string; menus: number; servings: number; plannedFood: number; plannedOverhead: number; plannedTotal: number; actualFood: number; actualTotal: number; revenue: number; variance: number; margin: number; plannedPerServing?: number; actualPerServing?: number; budget?: number }
interface CostSheet { rows: CostRow[]; totals: { servings: number; plannedTotal: number; actualTotal: number; revenue: number; margin: number; variance: number; plannedPerServing?: number; actualPerServing?: number } }

/** Short money for tiles and bar labels: "1,25 tỷ" / "86,4 tr" (design `short`). */
function shortMoney(v: number, t: (k: string, o?: Record<string, unknown>) => string): string {
  const a = Math.abs(v);
  if (a >= 1e9) return t('cost.bn', { v: fmt.number(v / 1e9, 2) });
  if (a >= 1e6) return t('cost.mn', { v: fmt.number(v / 1e6, 1) });
  return fmt.money(v);
}

export function CostSheetPage() {
  const { t } = useTranslation('analysis');
  const navigate = useNavigate();
  const { has } = usePermission();
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([dayjs().startOf('month'), dayjs().endOf('month')]);
  const [mode, setMode] = useState('week');
  const [customerId, setCustomerId] = useState<string>();
  const params = { from: isoDate(range[0]), to: isoDate(range[1]), mode, customerId };
  const { data, isFetching } = useQuery({ queryKey: ['cost-sheet', params], queryFn: async () => (await http.get<CostSheet>('/cost-sheet', { params })).data, placeholderData: keepPreviousData });
  const chart = Object.values((data?.rows ?? []).reduce<Record<string, { period: string; planned: number; actual: number }>>((acc, r) => {
    acc[r.period] ??= { period: r.period, planned: 0, actual: 0 };
    acc[r.period].planned += r.plannedTotal;
    acc[r.period].actual += r.actualTotal;
    return acc;
  }, {}));
  const max = Math.max(1, ...chart.map((c) => Math.max(c.planned, c.actual)));
  const tot = data?.totals;
  const short = (v: number) => shortMoney(v, t);
  return (
    <div className="bm-page">
      <PageHead title={t('cost.title')} sub={t('cost.hint')} actions={
        <>
          {has('cost.config') && <XButton onClick={() => navigate('/admin/settings?key=cost')}>{t('cost.overhead')}</XButton>}
          {has('report.export') && <XButton onClick={() => navigate('/reports?code=RPT-11')}><FileExcelOutlined />{t('cost.exportRpt')}</XButton>}
        </>
      } />
      <div className="bm-x-form">
        <FLabel label={t('nutrition.range')} wide><DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} allowClear={false} /></FLabel>
        <FLabel label={t('cost.mode')}>
          <Select value={mode} onChange={setMode} options={[{ value: 'day', label: t('cost.byDay') }, { value: 'week', label: t('cost.byWeek') }, { value: 'month', label: t('cost.byMonth') }]} />
        </FLabel>
        <FLabel label={t('cost.customer')}><LookupSelect resource="customers" value={customerId} onChange={setCustomerId} placeholder={t('cost.allCustomers')} /></FLabel>
      </div>
      {tot && (
        <div className="bm-x-tiles">
          <Tile label={t('cost.planned')} value={short(tot.plannedTotal)} extra={`${fmt.number(tot.servings)} ${t('cost.servings').toLowerCase()}`} />
          <Tile label={t('cost.actual')} value={short(tot.actualTotal)} bad={tot.variance > 0}
            extra={tot.variance === 0 ? '' : t(tot.variance > 0 ? 'cost.over' : 'cost.under', { v: short(Math.abs(tot.variance)) })} />
          <Tile label={t('cost.revenue')} value={short(tot.revenue)} />
          <Tile label={t('cost.margin')} value={short(tot.margin)} bad={tot.margin < 0}
            extra={tot.revenue ? t('cost.ofRevenue', { v: fmt.percent((tot.margin / tot.revenue) * 100) }) : ''} />
        </div>
      )}
      <div className="bm-x-panels">
        {chart.length > 0 && (
          <Panel title={t('cost.chart')} full legend={[{ name: t('cost.planned'), color: 'var(--primary)' }, { name: t('cost.actual'), color: 'var(--info)' }]}>
            <Bars max={max} minWidth={Math.max(300, chart.length * 60)} items={chart.map((c) => ({ key: c.period, label: c.period, text: short(c.actual), value: c.planned, value2: c.actual }))} />
          </Panel>
        )}
        <Panel title={t('cost.detail')} full>
          <Table<CostRow> rowKey={(r) => r.period + (r.customerId ?? '')} loading={isFetching} dataSource={data?.rows} size="small" scroll={{ x: 'max-content' }} pagination={false} data-testid="cost-table"
            columns={[
              { title: t('cost.period'), dataIndex: 'period', width: 110, fixed: 'left' },
              { title: t('cost.customer'), dataIndex: 'customer', width: 200 },
              { title: t('cost.menus'), dataIndex: 'menus', align: 'right', width: 80 },
              { title: t('cost.servings'), dataIndex: 'servings', align: 'right', width: 90, render: (v) => fmt.number(v) },
              { title: t('cost.planned'), dataIndex: 'plannedTotal', align: 'right', width: 140, render: fmt.money },
              { title: t('cost.plannedPer'), dataIndex: 'plannedPerServing', align: 'right', width: 110, render: fmt.money },
              { title: t('cost.budget'), dataIndex: 'budget', align: 'right', width: 110, render: fmt.money },
              { title: t('cost.actualFood'), dataIndex: 'actualFood', align: 'right', width: 140, render: fmt.money },
              { title: t('cost.actual'), dataIndex: 'actualTotal', align: 'right', width: 140, render: fmt.money },
              { title: t('cost.actualPer'), dataIndex: 'actualPerServing', align: 'right', width: 110, render: fmt.money },
              { title: t('cost.variance'), dataIndex: 'variance', align: 'right', width: 130, render: (v) => <span className={v > 0 ? 'bm-x-num-bad' : undefined}>{fmt.money(v)}</span> },
              { title: t('cost.revenue'), dataIndex: 'revenue', align: 'right', width: 140, render: fmt.money },
              { title: t('cost.margin'), dataIndex: 'margin', align: 'right', width: 140, render: (v) => <span className={v < 0 ? 'bm-x-num-bad' : undefined}>{fmt.money(v)}</span> },
            ]}
            summary={() => !tot || !data?.rows.length ? null : (
              <Table.Summary.Row className="bm-x-sum">
                <Table.Summary.Cell index={0}><b>{t('reports.total')}</b></Table.Summary.Cell>
                <Table.Summary.Cell index={1} />
                <Table.Summary.Cell index={2} />
                <Table.Summary.Cell index={3} align="right"><b>{fmt.number(tot.servings)}</b></Table.Summary.Cell>
                <Table.Summary.Cell index={4} align="right"><b>{fmt.money(tot.plannedTotal)}</b></Table.Summary.Cell>
                <Table.Summary.Cell index={5} align="right"><b>{fmt.money(tot.plannedPerServing)}</b></Table.Summary.Cell>
                <Table.Summary.Cell index={6} />
                <Table.Summary.Cell index={7} />
                <Table.Summary.Cell index={8} align="right"><b>{fmt.money(tot.actualTotal)}</b></Table.Summary.Cell>
                <Table.Summary.Cell index={9} align="right"><b>{fmt.money(tot.actualPerServing)}</b></Table.Summary.Cell>
                <Table.Summary.Cell index={10} align="right"><b className={tot.variance > 0 ? 'bm-x-num-bad' : undefined}>{fmt.money(tot.variance)}</b></Table.Summary.Cell>
                <Table.Summary.Cell index={11} align="right"><b>{fmt.money(tot.revenue)}</b></Table.Summary.Cell>
                <Table.Summary.Cell index={12} align="right"><b>{fmt.money(tot.margin)}</b></Table.Summary.Cell>
              </Table.Summary.Row>
            )} />
        </Panel>
      </div>
    </div>
  );
}
