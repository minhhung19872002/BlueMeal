import { Alert, Empty, Progress, Space, Tag, Tooltip, Typography } from 'antd';
import { CheckCircleFilled, CloseCircleFilled, ExclamationCircleFilled, InfoCircleFilled, WarningFilled } from '@ant-design/icons';
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip as RTooltip } from 'recharts';
import { useTranslation } from 'react-i18next';
import { fmt } from '@/shared/utils/format';
import { CHART, useChartTheme } from './charts';

export interface ComplianceItem { code: string; actual: number; min?: number | null; max?: number | null; target?: number | null; pct?: number | null; belowMin: boolean; aboveMax: boolean }
export interface NutritionView {
  energyKcal: number; energyTableKcal: number; energyFromMacrosKcal: number;
  proteinG: number; lipidG: number; glucidG: number; proteinPct: number; lipidPct: number; glucidPct: number;
  animalProteinG: number; animalProteinPct: number; totals: Record<string, number>;
  dishes: { dishId: string; code: string; name: string; type: string; servingFactor: number; netWeightG: number; energyKcal: number; nutrients: Record<string, number> }[];
  missingFoodIngredients: string[]; compliance?: ComplianceItem[] | null; score?: number | null; targetEnergyKcal?: number | null;
}
export interface CostView {
  foodCostPerServing: number; overheadPerServing: number; costPerServing: number; servings: number; totalCost: number;
  overheads: { code: string; nameVi: string; nameEn: string; amount: number }[]; byFoodGroup: Record<string, number>;
  missingPriceIngredients: string[]; budget?: number | null; budgetVariance?: number | null; agreedPrice?: number | null; expectedMarginPerServing?: number | null;
  dishes?: { dishId: string; code: string; name: string; cost: number }[];
}
export interface WarningItem { code: string; level: 'Info' | 'Warning' | 'Critical'; messageVi: string; messageEn: string; subject?: string }

const DECIMALS: Record<string, number> = { ENERGY: 0, PROTEIN: 1, LIPID: 1, GLUCID: 1, FIBER: 1, WATER: 1 };
export const nd = (code: string) => DECIMALS[code] ?? 2;

/** P:L:G energy structure donut (3 parts of a whole) with direct labels + legend. */
export function PlgDonut({ p, l, g, size = 160 }: { p: number; l: number; g: number; size?: number }) {
  const { t } = useTranslation('nutrition');
  const theme = useChartTheme();
  const data = [
    { key: 'P', name: t('protein'), value: p },
    { key: 'L', name: t('lipid'), value: l },
    { key: 'G', name: t('glucid'), value: g },
  ];
  if (p + l + g <= 0) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} />;
  return (
    <Space align="center" wrap>
      <div style={{ width: size, height: size }} role="img" aria-label={t('plgTitle')}>
        <ResponsiveContainer>
          <PieChart>
            <Pie data={data} dataKey="value" nameKey="name" innerRadius={size * 0.3} outerRadius={size * 0.48} paddingAngle={2} stroke={theme.surface} strokeWidth={2} isAnimationActive={false}>
              {data.map((d, i) => <Cell key={d.key} fill={theme.series[i]} />)}
            </Pie>
            <RTooltip formatter={(v: number, n: string) => [fmt.percent(v), n]} />
          </PieChart>
        </ResponsiveContainer>
      </div>
      <Space direction="vertical" size={2}>
        {data.map((d, i) => (
          <Space key={d.key} size={6}>
            <span style={{ display: 'inline-block', width: 10, height: 10, borderRadius: 3, background: theme.series[i] }} />
            <Typography.Text>{d.name}</Typography.Text>
            <Typography.Text strong>{fmt.percent(d.value)}</Typography.Text>
          </Space>
        ))}
      </Space>
    </Space>
  );
}

function statusOf(c: ComplianceItem): { color: string; icon: React.ReactNode; key: string } {
  if (c.belowMin || c.aboveMax) return { color: CHART.status.serious, icon: <CloseCircleFilled style={{ color: CHART.status.serious }} />, key: c.belowMin ? 'belowMin' : 'aboveMax' };
  if (c.code === 'ENERGY' && c.pct != null && (c.pct < 90 || c.pct > 110)) return { color: CHART.status.warning, icon: <ExclamationCircleFilled style={{ color: CHART.status.warning }} />, key: 'outOfRange' };
  return { color: CHART.status.good, icon: <CheckCircleFilled style={{ color: CHART.status.good }} />, key: 'ok' };
}

/** % achievement bars with status icon + label (never color alone). */
export function ComplianceBars({ items, names }: { items: ComplianceItem[]; names?: Record<string, string> }) {
  const { t } = useTranslation('nutrition');
  return (
    <Space direction="vertical" style={{ width: '100%' }} size={6}>
      {items.map((c) => {
        const s = statusOf(c);
        return (
          <div key={c.code}>
            <Space style={{ width: '100%', justifyContent: 'space-between' }}>
              <Space size={6}>{s.icon}<Typography.Text>{names?.[c.code] ?? t('n.' + c.code, c.code)}</Typography.Text></Space>
              <Typography.Text type="secondary" className="bm-num">
                {fmt.number(c.actual, nd(c.code))}{c.target != null && ` / ${fmt.number(c.target, nd(c.code))}`} · {t('status.' + s.key)}
              </Typography.Text>
            </Space>
            <Progress percent={Math.min(150, Math.round(c.pct ?? 0))} showInfo format={(p) => `${p}%`} strokeColor={s.color} size="small" />
          </div>
        );
      })}
    </Space>
  );
}

export function WarningList({ warnings }: { warnings: WarningItem[] }) {
  const { t, i18n } = useTranslation('nutrition');
  if (!warnings.length) return <Alert type="success" showIcon message={t('noWarnings')} />;
  const order = { Critical: 0, Warning: 1, Info: 2 } as const;
  return (
    <Space direction="vertical" style={{ width: '100%' }} size={4}>
      {[...warnings].sort((a, b) => order[a.level] - order[b.level]).map((w, i) => (
        <Alert
          key={i}
          showIcon
          icon={w.level === 'Critical' ? <CloseCircleFilled /> : w.level === 'Warning' ? <WarningFilled /> : <InfoCircleFilled />}
          type={w.level === 'Critical' ? 'error' : w.level === 'Warning' ? 'warning' : 'info'}
          message={<Space wrap size={4}><Tag>{t('level.' + w.level)}</Tag><Typography.Text code>{w.code}</Typography.Text>{i18n.language === 'en' ? w.messageEn : w.messageVi}</Space>}
          style={{ padding: '4px 10px' }}
        />
      ))}
    </Space>
  );
}

/** Adequacy status for one recommendation item: text label + colour (never colour alone). */
function adequacy(c: ComplianceItem): { key: 'ok' | 'low' | 'high'; color: string } {
  const pct = c.pct ?? 0;
  if (c.belowMin || (c.code === 'ENERGY' && c.pct != null && pct < 90)) return { key: 'low', color: c.code === 'ENERGY' && pct < 85 ? 'var(--danger-text)' : 'var(--tag-orange-fg)' };
  if (c.aboveMax || (c.code === 'ENERGY' && c.pct != null && pct > 110)) return { key: 'high', color: 'var(--tag-orange-fg)' };
  return { key: 'ok', color: 'var(--primary-text)' };
}

const costRowClass = (r: { strong?: boolean; bad?: boolean }) => (r.strong ? 'bm-costrow is-strong' : r.bad ? 'bm-costrow is-bad' : 'bm-costrow');
const DONUT_EMPTY = 'var(--surface-2)';

/**
 * Realtime nutrition/cost summary used by the recipe editor and menu builders (SPEC §7.3.7, §7.4.4).
 * Layout from Claude Design BMMenus (DESIGN_HANDOFF §6): 4 headline numbers, P:L:G donut 112px (P = primary, L = warning,
 * G = info), adequacy bars 6px with a text status, cost rows and tinted warnings.
 */
export function NutritionPanel({ nutrition, cost, warnings, title }: { nutrition?: NutritionView | null; cost?: CostView | null; warnings?: WarningItem[]; title?: string; compact?: boolean }) {
  const { t, i18n } = useTranslation('nutrition');
  const head = <div className="bm-sec-head"><div className="bm-sec-title">{title ?? t('panelTitle')}</div></div>;
  if (!nutrition) return <section className="bm-card">{head}<div className="bm-np-body"><Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /></div></section>;
  const n = nutrition;
  const total = n.proteinPct + n.lipidPct + n.glucidPct;
  const pp = total > 0 ? (n.proteinPct / total) * 100 : 0;
  const lp = total > 0 ? (n.lipidPct / total) * 100 : 0;
  const donut = total > 0 ? `conic-gradient(var(--primary) 0 ${pp}%, var(--warn) ${pp}% ${pp + lp}%, var(--info) ${pp + lp}% 100%)` : DONUT_EMPTY;
  const legend = [
    { key: 'P', name: t('protein'), color: 'var(--primary)', pct: n.proteinPct, g: n.proteinG },
    { key: 'L', name: t('lipid'), color: 'var(--warn)', pct: n.lipidPct, g: n.lipidG },
    { key: 'G', name: t('glucid'), color: 'var(--info)', pct: n.glucidPct, g: n.glucidG },
  ];
  const stats: { key: string; label: string; value: string; unit?: string }[] = [
    { key: 'e', label: t('energy'), value: fmt.number(n.energyKcal, 0), unit: 'kcal' },
    ...(n.targetEnergyKcal != null ? [{ key: 't', label: t('target'), value: fmt.number(n.targetEnergyKcal, 0), unit: 'kcal' }] : []),
    ...(n.score != null ? [{ key: 's', label: t('score'), value: fmt.number(n.score, 0), unit: '/100' }] : []),
    ...(cost ? [{ key: 'c', label: t('costPerServing'), value: fmt.money(cost.costPerServing) }] : []),
  ];
  const over = cost && cost.budget != null ? cost.costPerServing - cost.budget : null;
  const costRows: { k: string; label: string; v: string; strong?: boolean; bad?: boolean }[] = cost ? [
    { k: 'food', label: t('foodCost'), v: fmt.money(cost.foodCostPerServing) },
    ...cost.overheads.map((o) => ({ k: o.code, label: i18n.language === 'en' && o.nameEn ? o.nameEn : o.nameVi, v: fmt.money(o.amount) })),
    { k: 'total', label: t('costPerServing'), v: fmt.money(cost.costPerServing), strong: true },
    ...(cost.budget != null ? [{ k: 'budget', label: t('budget'), v: fmt.money(cost.budget) }] : []),
    ...(over != null ? [{ k: 'var', label: t('variance'), v: (over > 0 ? '+' : '−') + fmt.money(Math.abs(over)), bad: over > 0 }] : []),
    ...(cost.expectedMarginPerServing != null ? [{ k: 'margin', label: t('margin'), v: fmt.money(cost.expectedMarginPerServing), bad: cost.expectedMarginPerServing < 0 }] : []),
  ] : [];
  const order = { Critical: 0, Warning: 1, Info: 2 } as const;
  const warns = [...(warnings ?? [])].sort((a, b) => order[a.level] - order[b.level]);
  return (
    <section className="bm-card">
      {head}
      <div className="bm-np-body">
        <div className="bm-np-stats">
          {stats.map((x) => (
            <div key={x.key}>
              <div className="bm-np-sl">{x.label}</div>
              <div className="bm-np-sv">{x.value}{x.unit && <small>{x.unit}</small>}</div>
            </div>
          ))}
        </div>
        <div>
          <div className="bm-np-h" style={{ marginBottom: 8 }}>{t('plgTitle')}</div>
          <div className="bm-np-plg">
            <div className="bm-donut" role="img" aria-label={t('plgTitle')} style={{ background: donut }}><div>P:L:G</div></div>
            <div className="bm-legend">
              {legend.map((g) => (
                <div key={g.key} className="bm-legend-i">
                  <i style={{ background: g.color }} />
                  <span>{g.name}</span>
                  <b>{fmt.percent(g.pct)}</b>
                  <em>{fmt.number(g.g, 1)} g</em>
                </div>
              ))}
            </div>
          </div>
        </div>
        <div className="bm-np-sub">
          <div><Tooltip title={t('animalHint')}><span>{t('animalProtein')}</span></Tooltip> <b>{fmt.percent(n.animalProteinPct)}</b></div>
          <div><span>{t('energyMacros')}</span> <b>{fmt.number(n.energyFromMacrosKcal, 0)} kcal</b></div>
          <div><span>{t('energyTable')}</span> <b>{fmt.number(n.energyTableKcal, 0)} kcal</b></div>
        </div>
        {n.compliance && n.compliance.length > 0 && (
          <div className="bm-adq">
            <div className="bm-np-h">{t('compliance')}</div>
            {n.compliance.map((c) => {
              const a = adequacy(c);
              return (
                <div key={c.code} className="bm-adq-i">
                  <div className="bm-adq-top">
                    <span>{t('n.' + c.code, c.code)}</span>
                    <span>
                      {fmt.number(c.actual, nd(c.code))}{c.target != null && ` / ${fmt.number(c.target, nd(c.code))}`}{c.pct != null && ` · ${fmt.number(c.pct, 0)}%`} · <b style={{ color: a.color }}>{t('adq.' + a.key)}</b>
                    </span>
                  </div>
                  <div className="bm-track"><div style={{ width: `${Math.min(100, (c.pct ?? 0) / 1.3)}%`, background: a.color }} /></div>
                </div>
              );
            })}
          </div>
        )}
        {cost && (
          <div className="bm-costrows">
            {costRows.map((r) => <div key={r.k} className={costRowClass(r)}><span>{r.label}</span><span>{r.v}</span></div>)}
          </div>
        )}
        {warnings && (
          <div className="bm-warns">
            {warns.length === 0 ? <div className="bm-warn is-ok">{t('noWarnings')}</div> : warns.map((w, i) => (
              <div key={i} className={`bm-warn is-${w.level}`}>
                <b>{t('level.' + w.level)}</b><small>{w.code}</small><span>{i18n.language === 'en' ? w.messageEn : w.messageVi}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
