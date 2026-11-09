import { useState } from 'react';
import { Alert, App, Button, Card, Checkbox, Col, DatePicker, Empty, Form, Grid, Input, InputNumber, Modal, Radio, Row, Select, Space, Statistic, Switch, Table, Tabs, Tag, Typography } from 'antd';
import { CheckOutlined, FileExcelOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, postDownload, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect } from '@/shared/components/catalog/inputs';
import { ComplianceBars, PlgDonut, type ComplianceItem } from '@/shared/components/nutrition/NutritionPanel';
import { useChartTheme } from '@/shared/components/nutrition/charts';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';

// ================================================================== nutrition analysis (SPEC §7.5)

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
    <Space wrap>
      <Radio.Group value={value.scope} onChange={(e) => set({ scope: e.target.value })} optionType="button"
        options={[{ value: 'range', label: t('nutrition.range') }, { value: 'menu', label: t('nutrition.menu') }, { value: 'dish', label: t('nutrition.dish') }]} />
      {value.scope === 'range' && (
        <>
          <DatePicker.RangePicker format={DATE_FORMAT} value={[value.from ? dayjs(value.from) : null, value.to ? dayjs(value.to) : null]} onChange={(v) => set({ from: isoDate(v?.[0]), to: isoDate(v?.[1]) })} />
          <LookupSelect resource="customers" placeholder={t('reports.f.customer')} value={value.customerId} onChange={(v) => set({ customerId: v ?? undefined })} style={{ width: 200 }} />
          <LookupSelect resource="subject-groups" placeholder={t('reports.f.subject')} value={value.subjectGroupId} onChange={(v) => set({ subjectGroupId: v ?? undefined })} style={{ width: 200 }} />
          <LookupSelect resource="meal-shifts" placeholder={t('reports.f.mealShift')} value={value.mealShiftId} onChange={(v) => set({ mealShiftId: v ?? undefined })} style={{ width: 160 }} />
        </>
      )}
      {value.scope === 'menu' && <MenuPicker value={value.menuId} onChange={(v) => set({ menuId: v })} />}
      {value.scope === 'dish' && (
        <>
          <LookupSelect resource="dishes" placeholder={t('nutrition.dish')} value={value.dishId} onChange={(v) => set({ dishId: v ?? undefined })} style={{ width: 260 }} />
          <LookupSelect resource="recommendations" placeholder={t('nutrition.recommendation')} value={value.recommendationId} onChange={(v) => set({ recommendationId: v ?? undefined })} style={{ width: 260 }} />
        </>
      )}
    </Space>
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
    <Select showSearch allowClear filterOption={false} onSearch={setQ} loading={isFetching} value={value} onChange={onChange} placeholder={t('nutrition.menu')} style={{ width: 340 }}
      options={(data ?? []).map((m) => ({ value: m.id, label: `${m.code} · ${fmt.date(m.menuDate)} · ${m.shift}${m.customer ? ' · ' + m.customer : ''}` }))} />
  );
}

function AnalysisView({ data }: { data: Analysis }) {
  const { t, i18n } = useTranslation('analysis');
  const theme = useChartTheme();
  const en = i18n.language === 'en';
  const name = (n: NutrientRow) => (en && n.nameEn ? n.nameEn : n.nameVi);
  if (data.menuCount === 0) return <Empty description={t('nutrition.empty')} />;
  const compliance: ComplianceItem[] = data.nutrients.filter((n) => n.target != null).map((n) => ({ code: n.code, actual: n.value, min: n.min, max: n.max, target: n.target, pct: n.pct, belowMin: n.belowMin, aboveMax: n.aboveMax }));
  const names = Object.fromEntries(data.nutrients.map((n) => [n.code, name(n)]));
  const micro = data.nutrients.filter((n) => (n.group === 'Mineral' || n.group === 'Vitamin') && n.pct != null).map((n) => ({ name: name(n), pct: n.pct }));
  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Row gutter={[16, 16]}>
        <Col xs={12} md={6}><Statistic title={t('nutrition.energy')} value={fmt.number(data.energyKcal, 0)} suffix="kcal" /></Col>
        <Col xs={12} md={6}><Statistic title={t('nutrition.target')} value={fmt.number(data.targetEnergyKcal, 0)} suffix="kcal" /></Col>
        <Col xs={12} md={6}><Statistic title={t('nutrition.pct')} value={fmt.percent(data.energyPct)} /></Col>
        <Col xs={12} md={6}><Statistic title={t('nutrition.menus')} value={data.menuCount} /></Col>
      </Row>
      <Row gutter={[16, 16]}>
        <Col xs={24} md={8}>
          <Card size="small" title={t('nutrition.plg')}><PlgDonut p={data.proteinPct} l={data.lipidPct} g={data.glucidPct} /></Card>
        </Col>
        <Col xs={24} md={16}>
          <Card size="small" title={t('nutrition.pct')}><ComplianceBars items={compliance} names={names} /></Card>
        </Col>
      </Row>
      {micro.length > 0 && (
        <Card size="small" title={t('nutrition.micro')}>
          <ResponsiveContainer width="100%" height={Math.max(180, micro.length * 28)}>
            <BarChart data={micro} layout="vertical" margin={{ left: 40, right: 24 }}>
              <CartesianGrid horizontal={false} stroke={theme.grid} />
              <XAxis type="number" unit="%" stroke={theme.textSecondary} fontSize={12} />
              <YAxis type="category" dataKey="name" width={140} stroke={theme.textSecondary} fontSize={12} />
              <Tooltip formatter={(v) => fmt.percent(Number(v))} />
              <ReferenceLine x={100} stroke={theme.textSecondary} strokeDasharray="4 4" />
              <Bar dataKey="pct" name={t('nutrition.pct')} fill={theme.series[0]} radius={[0, 4, 4, 0]} barSize={14} />
            </BarChart>
          </ResponsiveContainer>
        </Card>
      )}
      {data.trend.length > 1 && (
        <Card size="small" title={t('nutrition.trend')}>
          <ResponsiveContainer width="100%" height={260}>
            <LineChart data={data.trend.map((p) => ({ ...p, label: fmt.date(p.date).slice(0, 5) }))} margin={{ right: 24 }}>
              <CartesianGrid vertical={false} stroke={theme.grid} />
              <XAxis dataKey="label" stroke={theme.textSecondary} fontSize={12} />
              <YAxis stroke={theme.textSecondary} fontSize={12} unit=" kcal" width={80} />
              <Tooltip formatter={(v) => `${fmt.number(Number(v), 0)} kcal`} />
              <Legend />
              <Line type="monotone" dataKey="energy" name={t('nutrition.trendActual')} stroke={theme.series[0]} strokeWidth={2} dot={{ r: 4 }} />
              <Line type="monotone" dataKey="target" name={t('nutrition.trendTarget')} stroke={theme.textSecondary} strokeWidth={2} strokeDasharray="6 4" dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </Card>
      )}
      <Card size="small" title={t('nutrition.table')}>
        <Table<NutrientRow> rowKey="code" dataSource={data.nutrients} size="small" pagination={false} scroll={{ x: 'max-content' }}
          columns={[
            { title: t('nutrition.nutrient'), key: 'n', render: (_, n) => name(n) },
            { title: t('nutrition.unit'), dataIndex: 'unit', width: 70 },
            { title: t('nutrition.value'), dataIndex: 'value', width: 110, align: 'right', render: (v, n) => fmt.number(v, n.decimals) },
            { title: t('nutrition.min'), dataIndex: 'min', width: 100, align: 'right', render: (v, n) => fmt.number(v, n.decimals) },
            { title: t('nutrition.max'), dataIndex: 'max', width: 100, align: 'right', render: (v, n) => fmt.number(v, n.decimals) },
            { title: t('nutrition.pct'), dataIndex: 'pct', width: 110, align: 'right', render: (v, n) => <Typography.Text type={n.belowMin || n.aboveMax ? 'danger' : undefined}>{fmt.percent(v)}</Typography.Text> },
          ]} />
      </Card>
      {(data.weekly.length > 0 || data.monthly.length > 0) && (
        <Tabs items={[['weekly', data.weekly], ['monthly', data.monthly]].map(([k, rows]) => ({
          key: k as string, label: t(`nutrition.${k}`),
          children: (
            <Table rowKey="period" dataSource={rows as Analysis['weekly']} size="small" pagination={false}
              columns={[
                { title: t('nutrition.period'), dataIndex: 'period' },
                { title: t('nutrition.menus'), dataIndex: 'menus', align: 'right' },
                { title: t('nutrition.energy'), dataIndex: 'energy', align: 'right', render: (v) => fmt.number(v, 0) },
                { title: t('nutrition.target'), dataIndex: 'target', align: 'right', render: (v) => fmt.number(v, 0) },
                { title: 'P %', dataIndex: 'proteinPct', align: 'right', render: (v) => fmt.number(v, 1) },
                { title: 'L %', dataIndex: 'lipidPct', align: 'right', render: (v) => fmt.number(v, 1) },
                { title: 'G %', dataIndex: 'glucidPct', align: 'right', render: (v) => fmt.number(v, 1) },
              ]} />
          ),
        }))} />
      )}
      {data.warnings.length > 0 && (
        <Card size="small" title={t('nutrition.warnings')}>
          <Table rowKey={(w) => w.code + w.level} dataSource={data.warnings} size="small" pagination={false}
            columns={[
              { title: t('alerts.level'), dataIndex: 'level', width: 110, render: (v) => <Tag color={v === 'Critical' ? 'red' : v === 'Warning' ? 'orange' : 'blue'}>{v}</Tag> },
              { title: t('alerts.code'), dataIndex: 'code', width: 160 },
              { title: t('nutrition.count'), dataIndex: 'count', width: 90, align: 'right' },
              { title: t('alerts.message'), key: 'm', render: (_, w) => (en ? w.messageEn : w.messageVi) },
            ]} />
        </Card>
      )}
    </Space>
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
  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t('nutrition.title')}</Typography.Title>
        <Space wrap>
          <Space><Switch checked={compare} onChange={setCompare} />{t('nutrition.compare')}</Space>
          {has('nutrition.export') && qa.scope === 'range' && <Button icon={<FileExcelOutlined />} onClick={() => void postDownload('/analysis/nutrition/export', qa).catch((e) => message.error(problemMessage(e)))}>{t('reports.export')}</Button>}
        </Space>
      </div>
      {compare && <Typography.Paragraph type="secondary">{t('nutrition.compareHint')}</Typography.Paragraph>}
      <Space direction="vertical" style={{ width: '100%' }}>
        <Space wrap>{compare && <Tag color="blue">{t('nutrition.a')}</Tag>}<QueryForm value={qa} onChange={setQa} /></Space>
        {compare && <Space wrap><Tag color="orange">{t('nutrition.b')}</Tag><QueryForm value={qb} onChange={setQb} /></Space>}
        <Button type="primary" onClick={() => setRun({ a: { ...qa }, b: compare ? { ...qb } : undefined })} loading={a.isFetching || b.isFetching} data-testid="analysis-run">{t('nutrition.analyze')}</Button>
        {error && <Alert type="error" showIcon message={problemMessage(error)} />}
      </Space>
      <Row gutter={16} style={{ marginTop: 16 }}>
        {a.data && <Col xs={24} xl={compare ? 12 : 24}>{compare && <Typography.Title level={5}>{t('nutrition.a')}: {a.data.title}</Typography.Title>}<AnalysisView data={a.data} /></Col>}
        {compare && b.data && <Col xs={24} xl={12}><Typography.Title level={5}>{t('nutrition.b')}: {b.data.title}</Typography.Title><AnalysisView data={b.data} /></Col>}
      </Row>
    </Card>
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

// ================================================================== meal cost sheet (SPEC §7.7)

interface CostRow { period: string; firstDate: string; customerId?: string; customer?: string; menus: number; servings: number; plannedFood: number; plannedOverhead: number; plannedTotal: number; actualFood: number; actualTotal: number; revenue: number; variance: number; margin: number; plannedPerServing?: number; actualPerServing?: number; budget?: number }
interface CostSheet { rows: CostRow[]; totals: { servings: number; plannedTotal: number; actualTotal: number; revenue: number; margin: number; variance: number; plannedPerServing?: number; actualPerServing?: number } }

export function CostSheetPage() {
  const { t } = useTranslation('analysis');
  const navigate = useNavigate();
  const { has } = usePermission();
  const theme = useChartTheme();
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
  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t('cost.title')}</Typography.Title>
        <Space wrap>
          {has('cost.config') && <Button onClick={() => navigate('/admin/settings?key=cost')}>{t('cost.overhead')}</Button>}
          {has('report.export') && <Button icon={<FileExcelOutlined />} onClick={() => navigate('/reports?code=RPT-11')}>{t('reports.export')}</Button>}
        </Space>
      </div>
      <div className="bm-toolbar">
        <DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} allowClear={false} />
        <Radio.Group value={mode} onChange={(e) => setMode(e.target.value)} optionType="button"
          options={[{ value: 'day', label: t('cost.byDay') }, { value: 'week', label: t('cost.byWeek') }, { value: 'month', label: t('cost.byMonth') }]} />
        <LookupSelect resource="customers" value={customerId} onChange={setCustomerId} placeholder={t('cost.customer')} style={{ width: 240 }} />
      </div>
      <Typography.Paragraph type="secondary">{t('cost.hint')}</Typography.Paragraph>
      {data && (
        <Row gutter={16} style={{ marginBottom: 12 }}>
          <Col xs={12} md={4}><Statistic title={t('cost.servings')} value={fmt.number(data.totals.servings)} /></Col>
          <Col xs={12} md={5}><Statistic title={t('cost.planned')} value={fmt.money(data.totals.plannedTotal)} /></Col>
          <Col xs={12} md={5}><Statistic title={t('cost.actual')} value={fmt.money(data.totals.actualTotal)} /></Col>
          <Col xs={12} md={5}><Statistic title={t('cost.revenue')} value={fmt.money(data.totals.revenue)} /></Col>
          <Col xs={12} md={5}><Statistic title={t('cost.margin')} value={fmt.money(data.totals.margin)} /></Col>
        </Row>
      )}
      {chart.length > 0 && (
        <Card size="small" title={t('cost.chart')} style={{ marginBottom: 12 }}>
          <ResponsiveContainer width="100%" height={240}>
            <BarChart data={chart} barGap={2}>
              <CartesianGrid vertical={false} stroke={theme.grid} />
              <XAxis dataKey="period" stroke={theme.textSecondary} fontSize={12} />
              <YAxis stroke={theme.textSecondary} fontSize={12} width={90} tickFormatter={(v) => fmt.number(Number(v) / 1_000_000, 1) + ' tr'} />
              <Tooltip formatter={(v) => fmt.money(Number(v))} />
              <Legend />
              <Bar dataKey="planned" name={t('cost.planned')} fill={theme.series[0]} radius={[4, 4, 0, 0]} />
              <Bar dataKey="actual" name={t('cost.actual')} fill={theme.series[1]} radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </Card>
      )}
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
          { title: t('cost.variance'), dataIndex: 'variance', align: 'right', width: 130, render: (v) => <Typography.Text type={v > 0 ? 'danger' : undefined}>{fmt.money(v)}</Typography.Text> },
          { title: t('cost.revenue'), dataIndex: 'revenue', align: 'right', width: 140, render: fmt.money },
          { title: t('cost.margin'), dataIndex: 'margin', align: 'right', width: 140, render: (v) => <Typography.Text type={v < 0 ? 'danger' : undefined}>{fmt.money(v)}</Typography.Text> },
        ]} />
    </Card>
  );
}
