import { Suspense, useMemo } from 'react';
import { Link, Outlet, useLocation, useNavigate } from 'react-router';
import { Breadcrumb, Grid, Layout, Menu, Spin, theme, type MenuProps } from 'antd';
import { MenuFoldOutlined, MenuUnfoldOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { filterNav, findTrail, NAV, type NavItem } from '@/app/navigation';
import { usePermission } from '@/shared/auth/authStore';
import { useUi } from '@/shared/hooks/useUiStore';
import { AppHeaderRight } from './HeaderRight';
import { BrandLogo } from './BrandLogo';

const { Sider, Header, Content } = Layout;

function toMenuItems(items: NavItem[], t: (k: string) => string): MenuProps['items'] {
  return items.map((i) =>
    i.children
      ? { key: i.key, icon: i.icon, label: t(i.label), children: toMenuItems(i.children, t) }
      : { key: i.key, icon: i.icon, label: i.path ? <Link to={i.path}>{t(i.label)}</Link> : t(i.label) },
  );
}

export function MainLayout() {
  const { t } = useTranslation();
  const { has } = usePermission();
  const loc = useLocation();
  const navigate = useNavigate();
  const screens = Grid.useBreakpoint();
  const isMobile = !screens.lg;
  const collapsed = useUi((s) => s.collapsed);
  const toggle = useUi((s) => s.toggleCollapsed);
  const setCollapsed = useUi((s) => s.setCollapsed);
  const { token } = theme.useToken();

  const nav = useMemo(() => filterNav(NAV, has), [has]);
  const trail = useMemo(() => findTrail(nav, loc.pathname) ?? [], [nav, loc.pathname]);
  const selected = trail.length ? [trail[trail.length - 1].key] : [];
  const openKeys = trail.slice(0, -1).map((i) => i.key);
  const siderCollapsed = isMobile ? true : collapsed;

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider
        collapsible
        collapsed={siderCollapsed}
        trigger={null}
        width={264}
        collapsedWidth={isMobile ? 0 : 64}
        breakpoint="lg"
        onBreakpoint={(broken) => setCollapsed(broken)}
        style={{ borderRight: `1px solid ${token.colorBorderSecondary}`, position: isMobile ? 'fixed' : 'sticky', top: 0, height: '100vh', zIndex: 20, overflow: 'auto' }}
        className={isMobile && !collapsed ? 'bm-sider-open' : undefined}
      >
        <div style={{ height: 56, display: 'flex', alignItems: 'center', paddingInline: siderCollapsed ? 16 : 20, cursor: 'pointer' }} onClick={() => navigate('/')}>
          <BrandLogo compact={siderCollapsed} />
        </div>
        <Menu
          mode="inline"
          items={toMenuItems(nav, t)}
          selectedKeys={selected}
          defaultOpenKeys={openKeys}
          key={openKeys.join('|')}
          onClick={() => isMobile && setCollapsed(true)}
          style={{ borderInlineEnd: 0 }}
        />
      </Sider>
      {isMobile && !collapsed && <div className="bm-backdrop" onClick={() => setCollapsed(true)} />}
      <Layout>
        <Header style={{ position: 'sticky', top: 0, zIndex: 10, display: 'flex', alignItems: 'center', gap: 12, paddingInline: 16, borderBottom: `1px solid ${token.colorBorderSecondary}` }}>
          <span role="button" aria-label={t('actions.more')} onClick={() => (isMobile ? setCollapsed(!collapsed) : toggle())} style={{ fontSize: 18, cursor: 'pointer' }}>
            {siderCollapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
          </span>
          {!isMobile && (
            <Breadcrumb
              items={[{ title: <Link to="/">{t('nav.dashboard')}</Link> }, ...trail.filter((i) => i.key !== 'dashboard').map((i) => ({ title: i.path ? <Link to={i.path}>{t(i.label)}</Link> : t(i.label) }))]}
            />
          )}
          <div style={{ flex: 1 }} />
          <AppHeaderRight />
        </Header>
        <Content style={{ padding: isMobile ? 12 : 20, minWidth: 0 }}>
          <Suspense fallback={<div style={{ padding: 48, textAlign: 'center' }}><Spin /></div>}>
            <Outlet />
          </Suspense>
        </Content>
      </Layout>
    </Layout>
  );
}
