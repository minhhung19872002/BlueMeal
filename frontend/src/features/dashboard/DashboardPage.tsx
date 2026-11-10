import type { ReactNode } from 'react';
import { Skeleton } from 'antd';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { http } from '@/shared/api/http';
import { useAuth, usePermission } from '@/shared/auth/authStore';
import { fmt } from '@/shared/utils/format';
import { Svg, tileIcon } from '@/app/layout/navIcons';

interface Dashboard {
  pendingApproval?: number; drafts?: number; criticalAlerts?: number; latestAlerts?: { id: string; code: string; messageVi: string; messageEn: string; link?: string; createdAt: string }[];
  servingsToday?: number; servingsByShift?: { shift: string; servings: number; menus: number }[]; energyPct7d?: number | null; energyTrend?: { date: string; pct: number }[];
  costPerServingWeek?: number | null; budgetPerServingWeek?: number | null; expiringLots?: number;
  expiringList?: { id: string; ingredient: string; ingredientEn?: string; lotNo?: string; expiryDate: string; quantity: number; unit?: string }[]; samplesDue?: number;
}

/** Up to 3 decimals, without trailing zeros (48 → "48", 36.5 → "36,5"). */
function qty(v: number): string {
  const s = String(Math.round(v * 1000) / 1000);
  return fmt.number(v, s.includes('.') ? s.length - s.indexOf('.') - 1 : 0);
}

interface Tile { key: keyof Dashboard; title: string; value: string | number; icon: string; to: string; danger?: boolean; extra?: string }

function Panel({ title, link, children }: { title: string; link?: { label: string; onClick: () => void }; children: ReactNode }) {
  return (
    <section className="bm-panel">
      <div className="bm-panel-head">
        <span>{title}</span>
        {link && <a href="#" onClick={(e) => { e.preventDefault(); link.onClick(); }}>{link.label}</a>}
      </div>
      {children}
    </section>
  );
}

/** Role-based home dashboard (SPEC §7.9), layout from "BlueMeal Layout.dc.html": only widgets returned by the API are shown. */
export default function DashboardPage() {
  const { t, i18n } = useTranslation('analysis');
  const navigate = useNavigate();
  const user = useAuth((s) => s.user);
  const { has } = usePermission();
  const en = i18n.language === 'en';
  const { data, isLoading } = useQuery({ queryKey: ['dashboard'], queryFn: async () => (await http.get<Dashboard>('/dashboard')).data, refetchInterval: 60_000 });
  const sample = useQuery({
    queryKey: ['sample-data-status'],
    queryFn: async () => (await http.get<{ loaded: boolean; purged: boolean }>('/system/sample-data')).data,
    enabled: has('system.setting'),
    staleTime: 300_000,
  });

  const tiles: Tile[] = [];
  if (data?.pendingApproval !== undefined) tiles.push({ key: 'pendingApproval', title: t('dash.pendingApproval'), value: data.pendingApproval, icon: tileIcon.audit, to: '/menus/approval' });
  if (data?.drafts !== undefined) tiles.push({ key: 'drafts', title: t('dash.drafts'), value: data.drafts, icon: tileIcon.reports, to: '/menus?status=Draft' });
  if (data?.criticalAlerts !== undefined) tiles.push({ key: 'criticalAlerts', title: t('dash.criticalAlerts'), value: data.criticalAlerts, icon: tileIcon.alert, to: '/alerts', danger: (data.criticalAlerts ?? 0) > 0 });
  if (data?.servingsToday !== undefined) tiles.push({ key: 'servingsToday', title: t('dash.servingsToday'), value: fmt.number(data.servingsToday), icon: tileIcon.partners, to: '/menus/published' });
  if (data && 'costPerServingWeek' in data) {
    const over = data.costPerServingWeek != null && data.budgetPerServingWeek != null && data.costPerServingWeek > data.budgetPerServingWeek;
    tiles.push({ key: 'costPerServingWeek', title: t('dash.costWeek'), value: data.costPerServingWeek == null ? '—' : fmt.money(data.costPerServingWeek), icon: tileIcon.wallet, to: '/cost-sheet', danger: over,
      extra: data.budgetPerServingWeek == null ? undefined : `${t('dash.budget')}: ${fmt.money(data.budgetPerServingWeek)} · ${over ? t('dash.overBudget') : t('dash.withinBudget')}` });
  }
  if (data && 'energyPct7d' in data) tiles.push({ key: 'energyPct7d', title: t('dash.energy7d'), value: data.energyPct7d == null ? '—' : fmt.percent(data.energyPct7d), icon: tileIcon.fire, to: '/analysis/nutrition' });
  if (data?.expiringLots !== undefined) tiles.push({ key: 'expiringLots', title: t('dash.expiringLots'), value: data.expiringLots, icon: tileIcon.clock, to: '/stock/balance', danger: (data.expiringLots ?? 0) > 0 });
  if (data?.samplesDue !== undefined) tiles.push({ key: 'samplesDue', title: t('dash.samplesDue'), value: data.samplesDue, icon: tileIcon.food, to: '/safety/samples?state=due', danger: (data.samplesDue ?? 0) > 0 });

  // Energy bars: the design scales to 120%; taller values widen the scale so no bar overflows.
  const trend = data?.energyTrend ?? [];
  const scale = Math.max(120, ...trend.map((p) => p.pct));
  const maxShift = Math.max(1, ...(data?.servingsByShift ?? []).map((s) => s.servings));
  const today = dayjs();
  const weekday = today.locale(en ? 'en' : 'vi').format('dddd').replace(/(^|\s)\p{L}/gu, (m) => m.toUpperCase());
  const dateLabel = `${weekday}, ${fmt.date(today.format('YYYY-MM-DD'))}`;
  const daysLeft = (d: string) => dayjs(d).startOf('day').diff(today.startOf('day'), 'day');
  const empty = <div className="bm-empty">{t('dash.noData')}</div>;

  return (
    <div className="bm-dash">
      <div className="bm-dash-head">
        <div>
          <div className="bm-dash-date">{dateLabel}</div>
          <h1 className="bm-dash-h1">{t('dashboard:welcome', { name: user?.fullName })}</h1>
        </div>
        {sample.data?.loaded && !sample.data.purged && <span className="bm-pill-orange">{t('dashboard:sampleBadge')}</span>}
      </div>

      {isLoading ? <Skeleton active /> : tiles.length === 0 ? (
        <section className="bm-panel"><div className="bm-empty">{t('dashboard:intro')}</div></section>
      ) : (
        <div className="bm-tiles">
          {tiles.map((x) => (
            <button key={x.key} type="button" className={x.danger ? 'bm-tile is-danger' : 'bm-tile'} onClick={() => navigate(x.to)} data-testid={`dash-${x.key}`}>
              <span className="bm-tile-top">
                <span className="bm-tile-title">{x.title}</span>
                <span className="bm-tile-icon"><Svg d={x.icon} size={16} stroke={1.8} /></span>
              </span>
              <span className="bm-tile-value">{x.value}</span>
              <span className="bm-tile-extra">{x.extra ?? ''}</span>
            </button>
          ))}
        </div>
      )}

      <div className="bm-panels">
        {data?.energyTrend && (
          <Panel title={t('dash.energyTrend')}>
            {trend.length === 0 ? empty : (
              <div style={{ padding: 18 }}>
                <div className="bm-bars">
                  <div className="bm-bars-ref" style={{ bottom: `${(100 / scale) * 100}%` }} />
                  <span className="bm-bars-ref-label" style={{ bottom: `calc(${(100 / scale) * 100}% - 8px)` }}>100%</span>
                  {trend.map((p) => (
                    <div key={p.date} className="bm-bar">
                      <span className="bm-bar-pct">{fmt.percent(p.pct)}</span>
                      <div className="bm-bar-fill" style={{ height: `${(p.pct / scale) * 100}%`, background: p.pct < 90 ? 'var(--warn)' : 'var(--primary)' }} />
                    </div>
                  ))}
                </div>
                <div className="bm-bars-x">
                  {trend.map((p) => <span key={p.date}>{fmt.date(p.date).slice(0, 5)}</span>)}
                </div>
              </div>
            )}
          </Panel>
        )}

        {data?.servingsByShift && (
          <Panel title={t('dash.byShift')}>
            {data.servingsByShift.length === 0 ? empty : (
              <div style={{ padding: '6px 18px 12px', display: 'flex', flexDirection: 'column' }}>
                {data.servingsByShift.map((s) => (
                  <div key={s.shift} className="bm-row bm-shift">
                    <div className="bm-shift-top">
                      <span className="bm-shift-name">{s.shift} <small>· {t('dashboard:menusCount', { count: s.menus })}</small></span>
                      <span className="bm-num-strong">{fmt.number(s.servings)}</span>
                    </div>
                    <div className="bm-track"><div style={{ width: `${(s.servings / maxShift) * 100}%` }} /></div>
                  </div>
                ))}
              </div>
            )}
          </Panel>
        )}

        {data?.latestAlerts && (
          <Panel title={t('dash.latestAlerts')} link={{ label: t('alerts.title'), onClick: () => navigate('/alerts') }}>
            {data.latestAlerts.length === 0 ? empty : (
              <div style={{ padding: '0 18px 6px', display: 'flex', flexDirection: 'column' }}>
                {data.latestAlerts.map((a) => (
                  <div key={a.id} className={`bm-row bm-alert${a.link ? ' is-link' : ''}`} onClick={() => a.link && navigate(a.link)}>
                    <div className="bm-alert-top">
                      <span className="bm-code-red">{a.code}</span>
                      <span className="bm-muted-num">{fmt.dateTime(a.createdAt)}</span>
                    </div>
                    <span className="bm-alert-msg">{en ? a.messageEn : a.messageVi}</span>
                  </div>
                ))}
              </div>
            )}
          </Panel>
        )}

        {data?.expiringList && (
          <Panel title={t('dash.expiringList')} link={{ label: t('dashboard:stockLink'), onClick: () => navigate('/stock/balance') }}>
            {data.expiringList.length === 0 ? empty : (
              <div style={{ padding: '0 18px 6px', display: 'flex', flexDirection: 'column' }}>
                {data.expiringList.map((l) => {
                  const days = daysLeft(l.expiryDate);
                  return (
                    <div key={l.id} className="bm-row bm-lot">
                      <div style={{ minWidth: 0 }}>
                        <div className="bm-lot-name">{en && l.ingredientEn ? l.ingredientEn : l.ingredient}</div>
                        <div className="bm-muted-num">{t('dash.lot')} {l.lotNo ?? '—'} · {t('dash.expiry')} {fmt.date(l.expiryDate)}</div>
                      </div>
                      <div className="bm-lot-right">
                        <div className="bm-num-strong">{qty(l.quantity)} {l.unit ?? ''}</div>
                        <div className="bm-lot-days">{days < 0 ? t('dashboard:expired') : t(days === 1 ? 'dashboard:dayLeft' : 'dashboard:daysLeft', { count: days })}</div>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </Panel>
        )}
      </div>
    </div>
  );
}
