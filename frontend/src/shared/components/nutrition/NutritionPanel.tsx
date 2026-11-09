import { Alert, Card, Descriptions, Empty, Progress, Space, Statistic, Table, Tag, Tooltip, Typography } from 'antd';
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

/** Realtime nutrition/cost summary used by the recipe editor and menu builders (SPEC §7.3.7, §7.4.4). */
export function NutritionPanel({ nutrition, cost, warnings, title, compact }: { nutrition?: NutritionView | null; cost?: CostView | null; warnings?: WarningItem[]; title?: string; compact?: boolean }) {
  const { t } = useTranslation('nutrition');
  if (!nutrition) return <Card size="small" title={title}><Empty /></Card>;
  const n = nutrition;
  return (
    <Card size="small" title={title ?? t('panelTitle')}>
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        <Space wrap size="large">
          <Statistic title={t('energy')} value={fmt.number(n.energyKcal, 0)} suffix="kcal" />
          {n.targetEnergyKcal != null && <Statistic title={t('target')} value={fmt.number(n.targetEnergyKcal, 0)} suffix="kcal" />}
          {n.score != null && <Statistic title={t('score')} value={fmt.number(n.score, 0)} suffix="/100" />}
          {cost && <Statistic title={t('costPerServing')} value={fmt.money(cost.costPerServing)} />}
        </Space>
        <Descriptions size="small" column={compact ? 1 : 3} bordered>
          <Descriptions.Item label={t('protein')}>{fmt.number(n.proteinG, 1)} g</Descriptions.Item>
          <Descriptions.Item label={t('lipid')}>{fmt.number(n.lipidG, 1)} g</Descriptions.Item>
          <Descriptions.Item label={t('glucid')}>{fmt.number(n.glucidG, 1)} g</Descriptions.Item>
          <Descriptions.Item label={<Tooltip title={t('animalHint')}>{t('animalProtein')}</Tooltip>}>{fmt.percent(n.animalProteinPct)}</Descriptions.Item>
          <Descriptions.Item label={t('energyMacros')}>{fmt.number(n.energyFromMacrosKcal, 0)} kcal</Descriptions.Item>
          <Descriptions.Item label={t('energyTable')}>{fmt.number(n.energyTableKcal, 0)} kcal</Descriptions.Item>
        </Descriptions>
        <div>
          <Typography.Text strong>{t('plgTitle')}</Typography.Text>
          <PlgDonut p={n.proteinPct} l={n.lipidPct} g={n.glucidPct} size={compact ? 130 : 160} />
        </div>
        {n.compliance && n.compliance.length > 0 && (
          <div>
            <Typography.Text strong>{t('compliance')}</Typography.Text>
            <ComplianceBars items={n.compliance} />
          </div>
        )}
        {cost && (
          <Table
            size="small"
            pagination={false}
            rowKey="k"
            dataSource={[
              { k: 'food', label: t('foodCost'), v: cost.foodCostPerServing },
              ...cost.overheads.map((o) => ({ k: o.code, label: o.nameVi, v: o.amount })),
              { k: 'total', label: t('costPerServing'), v: cost.costPerServing },
              ...(cost.budget != null ? [{ k: 'budget', label: t('budget'), v: cost.budget }] : []),
              ...(cost.expectedMarginPerServing != null ? [{ k: 'margin', label: t('margin'), v: cost.expectedMarginPerServing }] : []),
            ]}
            columns={[{ dataIndex: 'label', key: 'l' }, { dataIndex: 'v', key: 'v', align: 'right', render: (v: number, r) => (r.k === 'total' ? <b>{fmt.money(v)}</b> : fmt.money(v)) }]}
            showHeader={false}
          />
        )}
        {warnings && <WarningList warnings={warnings} />}
      </Space>
    </Card>
  );
}
