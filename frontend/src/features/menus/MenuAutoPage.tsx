import { useEffect, useState } from 'react';
import { Alert, App, Button, Card, Checkbox, Col, DatePicker, Empty, Form, InputNumber, Radio, Row, Select, Slider, Space, Switch, Tag, Typography } from 'antd';
import { SaveOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { DATE_FORMAT, fmt, isoDate, mondayOf } from '@/shared/utils/format';

interface Proposal {
  dishes: { dishId: string; code: string; name: string; nameEn?: string; type: string; method: string; energy?: number; cost?: number | null }[];
  score: number; energyKcal: number; costPerServing?: number | null; proteinPct: number; lipidPct: number; glucidPct: number; animalProteinPct: number;
}
interface DayResult { date: string; best: Proposal; alternatives: Proposal[]; explanationVi: string; explanationEn: string }
interface GenResult { days: DayResult[]; problems: string[]; elapsedMs: number; seed: number; candidateCount: number; recommendationId?: string }
interface JobStatus { status: string; menuIds?: string[] | null; error?: string | null }

const ALLERGENS = ['gluten', 'milk', 'egg', 'peanut', 'soy', 'fish', 'shellfish', 'treenut', 'sesame'];

/** Automatic menu generation (SPEC §7.4.3, algorithm §6.4): preview best + 2 alternatives per day, then save as drafts. */
export default function MenuAutoPage() {
  const { t, i18n } = useTranslation('menu');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const enumLabel = useEnumLabel();
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([mondayOf(dayjs().add(1, 'week')), mondayOf(dayjs().add(1, 'week')).add(4, 'day')]);
  const [req, setReq] = useState({
    mealShiftIds: [] as string[], subjectGroupId: undefined as string | undefined, customerId: undefined as string | undefined, recommendationId: undefined as string | undefined,
    structureTemplateId: undefined as string | undefined, budgetPerServing: null as number | null, noRepeatDays: 7, requiredDishIds: [] as string[], excludedDishIds: [] as string[],
    allergens: [] as string[], preferExpiringStock: false, newDishRatio: 0.2, seed: null as number | null, servings: 100, skipWeekends: true,
  });
  const [result, setResult] = useState<GenResult | null>(null);
  const [chosen, setChosen] = useState<Record<string, number>>({});
  const [busy, setBusy] = useState(false);
  const [jobId, setJobId] = useState<string | null>(null);
  const set = (p: Partial<typeof req>) => setReq((r) => ({ ...r, ...p }));
  const request = (seed?: number | null) => ({ ...req, fromDate: isoDate(range[0]), toDate: isoDate(range[1]), seed: seed ?? req.seed ?? null });

  const run = async (newSeed: boolean) => {
    if (req.mealShiftIds.length === 0) { message.error(t('fixErrors')); return; }
    setBusy(true);
    try {
      const seed = newSeed ? Math.floor(Math.random() * 1_000_000) : req.seed;
      if (newSeed) set({ seed });
      // preview uses one shift at a time (first selected); saving covers all shifts
      const r = await http.post<GenResult>('/menus/generate', { request: { ...request(seed), mealShiftIds: req.mealShiftIds.slice(0, 1) }, save: false });
      setResult(r.data);
      setChosen({});
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const save = async () => {
    setBusy(true);
    try {
      const seed = result?.seed ?? req.seed;
      const r = await http.post<{ saved?: string[]; jobId?: string; problems?: string[] }>('/menus/generate', { request: request(seed), save: true, chosen: req.mealShiftIds.length === 1 ? chosen : null }, { validateStatus: (s) => s < 300 });
      if (r.data.jobId) {
        setJobId(r.data.jobId);
        message.info(t('gen.queued'));
      } else {
        message.success(t('gen.saved', { n: r.data.saved?.length ?? 0 }));
        navigate('/menus');
      }
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const job = useQuery({
    queryKey: ['gen-job', jobId], enabled: !!jobId,
    queryFn: async () => (await http.get<JobStatus>(`/menus/generate/${jobId}`)).data,
    refetchInterval: (q) => (q.state.data && ['Succeeded', 'Failed'].includes(q.state.data.status) ? false : 2000),
  });
  useEffect(() => {
    if (job.data?.status === 'Succeeded') { message.success(t('gen.saved', { n: job.data.menuIds?.length ?? 0 })); setJobId(null); navigate('/menus'); }
    if (job.data?.status === 'Failed') { message.error(job.data.error ?? 'error'); setJobId(null); }
  }, [job.data, message, navigate, t]);

  const dishName = (d: Proposal['dishes'][number]) => (i18n.language === 'en' && d.nameEn ? d.nameEn : d.name);
  const summaryLine = (p: Proposal) => [
    `${fmt.number(p.energyKcal, 0)} kcal`, `P/L/G ${fmt.number(p.proteinPct, 0)}/${fmt.number(p.lipidPct, 0)}/${fmt.number(p.glucidPct, 0)}%`,
    p.costPerServing != null ? fmt.money(p.costPerServing) : null, `${t('score')} ${fmt.number(p.score, 1)}`,
  ].filter(Boolean).join(' · ');
  const ProposalView = ({ p }: { p: Proposal }) => (
    <div>
      <Space size={[4, 4]} wrap style={{ marginBottom: 6 }}>{p.dishes.map((d) => <Tag key={d.dishId} title={enumLabel('DishType', d.type)}>{dishName(d)}</Tag>)}</Space>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>{summaryLine(p)}</Typography.Text>
    </div>
  );

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Card title={t('gen.params')}>
        <Form layout="vertical">
          <Row gutter={12}>
            <Col xs={24} md={8}><Form.Item label={t('gen.period')} required><DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={t('gen.shifts')} required><LookupSelect resource="meal-shifts" multiple value={req.mealShiftIds} onChange={(v) => set({ mealShiftIds: v ?? [] })} data-testid="gen-shifts" /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={t('subject')}><LookupSelect resource="subject-groups" value={req.subjectGroupId} onChange={(v) => set({ subjectGroupId: v })} data-testid="gen-subject" /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={t('customer')}><LookupSelect resource="customers" value={req.customerId} onChange={(v) => set({ customerId: v })} /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={t('recommendation')}><LookupSelect resource="recommendations" value={req.recommendationId} onChange={(v) => set({ recommendationId: v })} data-testid="gen-rec" /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={t('structure')}><LookupSelect resource="meal-structures" value={req.structureTemplateId} onChange={(v) => set({ structureTemplateId: v })} /></Form.Item></Col>
            <Col xs={12} md={4}><Form.Item label={t('servings')}><InputNumber min={1} value={req.servings} onChange={(v) => set({ servings: Number(v) || 1 })} style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12} md={4}><Form.Item label={t('budget')}><InputNumber min={0} step={1000} value={req.budgetPerServing ?? undefined} onChange={(v) => set({ budgetPerServing: v ?? null })} style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12} md={4}><Form.Item label={t('gen.noRepeat')}><InputNumber min={0} max={60} value={req.noRepeatDays} onChange={(v) => set({ noRepeatDays: Number(v) || 0 })} style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12} md={4}><Form.Item label={t('gen.seed')}><InputNumber min={0} value={req.seed ?? undefined} onChange={(v) => set({ seed: v ?? null })} style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={`${t('gen.newRatio')}: ${Math.round(req.newDishRatio * 100)}%`}><Slider min={0} max={1} step={0.05} value={req.newDishRatio} onChange={(v) => set({ newDishRatio: v })} /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={t('gen.required')}><LookupSelect resource="dishes" multiple value={req.requiredDishIds} onChange={(v) => set({ requiredDishIds: v ?? [] })} /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={t('gen.excluded')}><LookupSelect resource="dishes" multiple value={req.excludedDishIds} onChange={(v) => set({ excludedDishIds: v ?? [] })} /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item label={t('gen.allergens')}><Select mode="tags" value={req.allergens} onChange={(v) => set({ allergens: v })} options={ALLERGENS.map((a) => ({ value: a, label: a }))} /></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item><Checkbox checked={req.preferExpiringStock} onChange={(e) => set({ preferExpiringStock: e.target.checked })}>{t('gen.expiring')}</Checkbox></Form.Item></Col>
            <Col xs={24} md={8}><Form.Item><Space><Switch checked={req.skipWeekends} onChange={(v) => set({ skipWeekends: v })} />{t('gen.skipSundays')}</Space></Form.Item></Col>
          </Row>
          <Space wrap>
            <Button type="primary" icon={<ThunderboltOutlined />} loading={busy} onClick={() => void run(false)} data-testid="gen-run">{t('gen.run')}</Button>
            {result && <Button loading={busy} onClick={() => void run(true)}>{t('gen.rerun')}</Button>}
            {result && <Button type="primary" icon={<SaveOutlined />} loading={busy || !!jobId} onClick={() => void save()} data-testid="gen-save">{t('gen.saveAll')}</Button>}
          </Space>
        </Form>
      </Card>
      {jobId && <Alert type="info" showIcon message={t('gen.queued')} description={job.data?.status} />}
      {result && (
        <Card title={t('auto')} extra={<Typography.Text type="secondary">{t('gen.elapsed', { ms: result.elapsedMs, n: result.candidateCount, seed: result.seed })} · {t('gen.scoreHint')}</Typography.Text>}>
          {result.problems.length > 0 && <Alert type="warning" showIcon message={t('gen.problems')} description={<ul style={{ margin: 0 }}>{result.problems.map((p) => <li key={p}>{p}</li>)}</ul>} style={{ marginBottom: 12 }} />}
          {result.days.length === 0 ? <Empty /> : (
            <Space direction="vertical" style={{ width: '100%' }}>
              {result.days.map((d) => (
                <Card key={d.date} size="small" type="inner" title={`${fmt.weekday(d.date)} ${fmt.date(d.date)}`} data-testid="gen-day">
                  <Radio.Group value={chosen[d.date] ?? 0} onChange={(e) => setChosen({ ...chosen, [d.date]: e.target.value })} style={{ width: '100%' }}>
                    <Space direction="vertical" style={{ width: '100%' }}>
                      {[d.best, ...d.alternatives].map((p, i) => (
                        <Radio key={i} value={i} style={{ alignItems: 'flex-start' }}>
                          <Typography.Text strong>{i === 0 ? t('gen.best') : t('gen.alternative', { n: i + 1 })}</Typography.Text>
                          <ProposalView p={p} />
                        </Radio>
                      ))}
                    </Space>
                  </Radio.Group>
                  <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0, fontSize: 12 }}>{t('explanation')}: {i18n.language === 'en' ? d.explanationEn : d.explanationVi}</Typography.Paragraph>
                </Card>
              ))}
            </Space>
          )}
        </Card>
      )}
    </Space>
  );
}
