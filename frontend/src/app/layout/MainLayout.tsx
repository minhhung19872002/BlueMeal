import { Suspense, useEffect, useMemo, useState } from 'react';
import { Link, Outlet, useLocation, useNavigate } from 'react-router';
import { Grid, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { filterNav, findTrail, NAV, type NavItem } from '@/app/navigation';
import { usePermission } from '@/shared/auth/authStore';
import { useUi } from '@/shared/hooks/useUiStore';
import { AppHeaderRight } from './HeaderRight';
import { PrivacyConsentGate } from '@/features/auth/PrivacyNotice';
import { childIcon, groupIcon, LogoMark, Svg } from './navIcons';

const itemClass = (solid: boolean, inGroup: boolean) => (solid ? 'bm-sb-item is-solid' : inGroup ? 'bm-sb-item is-in' : 'bm-sb-item');
const subClass = (active: boolean) => (active ? 'bm-sb-subitem is-active' : 'bm-sb-subitem');

/** The design has two levels; nested groups (System › Common catalogs) are flattened into their parent. */
function flatChildren(items: NavItem[]): NavItem[] {
  return items.flatMap((c) => (c.children ? flatChildren(c.children) : [c]));
}

/** App shell from "BlueMeal Layout.dc.html": sidebar 264/72 (drawer < 992px), 60px header, content max 1480px. */
export function MainLayout() {
  const { t } = useTranslation();
  const { has } = usePermission();
  const loc = useLocation();
  const navigate = useNavigate();
  const screens = Grid.useBreakpoint();
  const isMobile = !screens.lg;
  const collapsed = useUi((s) => s.collapsed);
  const toggleCollapsed = useUi((s) => s.toggleCollapsed);
  const [drawer, setDrawer] = useState(false);

  const nav = useMemo(() => filterNav(NAV, has).map((g) => (g.children ? { ...g, children: flatChildren(g.children) } : g)), [has]);
  const trail = useMemo(() => findTrail(nav, loc.pathname) ?? [], [nav, loc.pathname]);
  const activeKey = trail.length ? trail[trail.length - 1].key : undefined;
  const activeGroup = trail.length > 1 ? trail[0].key : undefined;
  const [openKey, setOpenKey] = useState<string | undefined>(activeGroup);
  useEffect(() => setOpenKey(activeGroup), [activeGroup]);
  useEffect(() => setDrawer(false), [loc.pathname]);

  const showLabels = isMobile || !collapsed;
  const padX = isMobile ? '12px' : '24px';
  const padY = isMobile ? '16px' : '24px';
  const crumbs = [{ label: t('nav.dashboard'), path: '/' }, ...trail.filter((i) => i.key !== 'dashboard').map((i) => ({ label: t(i.label), path: i.path }))];

  return (
    <div className="bm-shell">
      {isMobile && drawer && <div className="bm-backdrop" onClick={() => setDrawer(false)} />}

      <aside
        className="bm-sb"
        style={{
          width: isMobile ? 'min(284px, 86vw)' : collapsed ? '72px' : '264px',
          position: isMobile ? 'fixed' : 'sticky',
          transform: isMobile && !drawer ? 'translateX(-100%)' : 'none',
        }}
      >
        <div className="bm-sb-brand" onClick={() => navigate('/')}>
          <LogoMark />
          {showLabels && <span>{t('appName')}</span>}
        </div>
        <nav className="bm-sb-nav">
          {nav.map((g) => {
            const kids = g.children;
            const inGroup = activeGroup === g.key;
            const open = openKey === g.key;
            const solid = (!kids && activeKey === g.key) || (inGroup && !!kids && !(open && showLabels));
            const cls = itemClass(solid, inGroup);
            const inner = (
              <>
                <Svg d={groupIcon(g.key)} />
                {showLabels && <span className="bm-sb-label">{t(g.label)}</span>}
                {showLabels && kids && <Svg d="M6 9l6 6 6-6" size={14} stroke={2} style={{ opacity: 0.7, transform: open ? 'rotate(180deg)' : 'none', transition: 'transform .15s' }} />}
              </>
            );
            return (
              <div className="bm-sb-group" key={g.key}>
                {kids ? (
                  <button type="button" className={cls} title={t(g.label)} aria-expanded={open && showLabels} onClick={() => { setOpenKey(open ? '' : g.key); if (collapsed) toggleCollapsed(); }}>
                    {inner}
                  </button>
                ) : (
                  <Link className={cls} title={t(g.label)} to={g.path ?? '/'}>{inner}</Link>
                )}
                {kids && open && showLabels && (
                  <div className="bm-sb-sub">
                    {kids.map((c) => (
                      <Link key={c.key} to={c.path ?? '/'} className={subClass(activeKey === c.key)} aria-current={activeKey === c.key ? 'page' : undefined}>
                        <Svg d={childIcon(c.key)} size={16} />
                        <span>{t(c.label)}</span>
                      </Link>
                    ))}
                  </div>
                )}
              </div>
            );
          })}
        </nav>
        {showLabels && <div className="bm-sb-foot">v{import.meta.env.VITE_APP_VERSION ?? '1.0.0'} · {t('orgShort')}</div>}
      </aside>

      <div className="bm-main">
        <header className="bm-header" style={{ padding: `0 ${padX}` }}>
          <button type="button" className="bm-icon-btn" aria-label={t('header.toggleMenu')} onClick={() => (isMobile ? setDrawer(!drawer) : toggleCollapsed())}>
            <Svg d="M4 6h16M4 12h10M4 18h16" size={20} stroke={1.8} />
          </button>
          {!isMobile && (
            <nav className="bm-crumbs" aria-label={t('header.breadcrumb')}>
              {crumbs.map((c, i) => {
                const last = i === crumbs.length - 1;
                return (
                  <span key={i} style={{ display: 'contents' }}>
                    {last || !c.path ? <span className={last ? 'is-last' : undefined}>{c.label}</span> : <Link to={c.path}>{c.label}</Link>}
                    {!last && <span className="sep">/</span>}
                  </span>
                );
              })}
            </nav>
          )}
          <div style={{ flex: 1 }} />
          <AppHeaderRight isMobile={isMobile} />
        </header>

        <main className="bm-content" style={{ padding: `${padY} ${padX} 40px` }}>
          <Suspense fallback={<div style={{ padding: 48, textAlign: 'center' }}><Spin /></div>}>
            <Outlet />
          </Suspense>
        </main>
        <PrivacyConsentGate />
      </div>
    </div>
  );
}
