import { useEffect, useState } from 'react';
import { App, Button, Checkbox, DatePicker, Empty, Form, InputNumber, Select, Slider, Space, Switch } from 'antd';
import { SaveOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { DATE_FORMAT, fmt, isoDate, mondayOf } from '@/shared/utils/format';
import { Page } from '@/shared/components/PageChrome';

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
  const metaLine = (p: Proposal) => [
    `${fmt.number(p.energyKcal, 0)} kcal`, p.costPerServing != null ? fmt.money(p.costPerServing) : null,
    `P/L/G ${fmt.number(p.proteinPct, 0)}/${fmt.number(p.lipidPct, 0)}/${fmt.number(p.glucidPct, 0)}%`,
  ].filter(Boolean).join(' · ');
  const optClass = (on: boolean) => (on ? 'bm-opt is-on' : 'bm-opt');

  return (
    <Page
      title={t('auto')}
      actions={(
        <>
          <Button type={result ? 'default' : 'primary'} icon={<ThunderboltOutlined />} loading={busy} onClick={() => void run(false)} data-testid="gen-run">{t('gen.run')}</Button>
          {result && <Button loading={busy} onClick={() => void run(true)}>{t('gen.rerun')}</Button>}
          {result && <Button type="primary" icon={<SaveOutlined />} loading={busy || !!jobId} onClick={() => void save()} data-testid="gen-save">{t('gen.saveAll')}</Button>}
        </>
      )}
    >
      <Form layout="vertical" className="bm-formcard">
        <Form.Item label={t('gen.period')} required className="bm-span-2"><DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} style={{ width: '100%' }} /></Form.Item>
        <Form.Item label={t('gen.shifts')} required className="bm-span-2"><LookupSelect resource="meal-shifts" multiple value={req.mealShiftIds} onChange={(v) => set({ mealShiftIds: v ?? [] })} data-testid="gen-shifts" /></Form.Item>
        <Form.Item label={t('subject')}><LookupSelect resource="subject-groups" value={req.subjectGroupId} onChange={(v) => set({ subjectGroupId: v })} data-testid="gen-subject" /></Form.Item>
        <Form.Item label={t('customer')}><LookupSelect resource="customers" value={req.customerId} onChange={(v) => set({ customerId: v })} /></Form.Item>
        <Form.Item label={t('recommendation')}><LookupSelect resource="recommendations" value={req.recommendationId} onChange={(v) => set({ recommendationId: v })} data-testid="gen-rec" /></Form.Item>
        <Form.Item label={t('structure')}><LookupSelect resource="meal-structures" value={req.structureTemplateId} onChange={(v) => set({ structureTemplateId: v })} /></Form.Item>
        <Form.Item label={t('servings')}><InputNumber min={1} value={req.servings} onChange={(v) => set({ servings: Number(v) || 1 })} style={{ width: '100%' }} /></Form.Item>
        <Form.Item label={t('budget')}><InputNumber min={0} step={1000} value={req.budgetPerServing ?? undefined} onChange={(v) => set({ budgetPerServing: v ?? null })} style={{ width: '100%' }} /></Form.Item>
        <Form.Item label={t('gen.noRepeat')}><InputNumber min={0} max={60} value={req.noRepeatDays} onChange={(v) => set({ noRepeatDays: Number(v) || 0 })} style={{ width: '100%' }} /></Form.Item>
        <Form.Item label={t('gen.seed')}><InputNumber min={0} value={req.seed ?? undefined} onChange={(v) => set({ seed: v ?? null })} style={{ width: '100%' }} /></Form.Item>
        <Form.Item label={`${t('gen.newRatio')}: ${Math.round(req.newDishRatio * 100)}%`} className="bm-span-2"><Slider min={0} max={1} step={0.05} value={req.newDishRatio} onChange={(v) => set({ newDishRatio: v })} /></Form.Item>
        <Form.Item label={t('gen.required')} className="bm-span-2"><LookupSelect resource="dishes" multiple value={req.requiredDishIds} onChange={(v) => set({ requiredDishIds: v ?? [] })} /></Form.Item>
        <Form.Item label={t('gen.excluded')} className="bm-span-2"><LookupSelect resource="dishes" multiple value={req.excludedDishIds} onChange={(v) => set({ excludedDishIds: v ?? [] })} /></Form.Item>
        <Form.Item label={t('gen.allergens')} className="bm-span-2"><Select mode="tags" value={req.allergens} onChange={(v) => set({ allergens: v })} options={ALLERGENS.map((a) => ({ value: a, label: a }))} /></Form.Item>
        <Form.Item className="bm-span-2"><Checkbox checked={req.preferExpiringStock} onChange={(e) => set({ preferExpiringStock: e.target.checked })}>{t('gen.expiring')}</Checkbox></Form.Item>
        <Form.Item className="bm-span-2"><Space><Switch checked={req.skipWeekends} onChange={(v) => set({ skipWeekends: v })} />{t('gen.skipSundays')}</Space></Form.Item>
      </Form>
      {jobId && <div className="bm-note is-info">{t('gen.queued')}{job.data?.status ? ` · ${job.data.status}` : ''}</div>}
      {result && (
        <>
          <div className="bm-sub" style={{ marginTop: 0 }}>{t('gen.elapsed', { ms: result.elapsedMs, n: result.candidateCount, seed: result.seed })} · {t('gen.scoreHint')}</div>
          {result.problems.length > 0 && (
            <div className="bm-note is-orange"><b>{t('gen.problems')}</b><ul style={{ margin: '4px 0 0', paddingLeft: 18 }}>{result.problems.map((p) => <li key={p}>{p}</li>)}</ul></div>
          )}
          {result.days.length === 0 ? <div className="bm-card bm-pad"><Empty /></div> : (
            <div className="bm-res">
              {result.days.map((d) => (
                <div key={d.date} className="bm-res-day" data-testid="gen-day">
                  <div className="bm-res-label">{fmt.weekday(d.date)} {fmt.date(d.date)}</div>
                  <div className="bm-res-opts" role="radiogroup" aria-label={`${fmt.weekday(d.date)} ${fmt.date(d.date)}`}>
                    {[d.best, ...d.alternatives].map((p, i) => {
                      const on = (chosen[d.date] ?? 0) === i;
                      return (
                        <button key={i} type="button" role="radio" aria-checked={on} className={optClass(on)} onClick={() => setChosen({ ...chosen, [d.date]: i })}>
                          <span className="bm-opt-top">
                            <span className="bm-opt-name">{i === 0 ? t('gen.best') : t('gen.alternative', { n: i + 1 })}</span>
                            <span className="bm-opt-score">{t('score')} {fmt.number(p.score, 1)}</span>
                          </span>
                          <span className="bm-opt-dishes" title={p.dishes.map((x) => enumLabel('DishType', x.type)).join(' · ')}>{p.dishes.map(dishName).join(' · ')}</span>
                          <span className="bm-opt-meta">{metaLine(p)}</span>
                        </button>
                      );
                    })}
                  </div>
                  <div className="bm-opt-why">{t('explanation')}: {i18n.language === 'en' ? d.explanationEn : d.explanationVi}</div>
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </Page>
  );
}
