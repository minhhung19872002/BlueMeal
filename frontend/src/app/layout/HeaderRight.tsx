import { useEffect, useRef, useState } from 'react';
import { Button, Empty, List, Popover, Space, Typography } from 'antd';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http } from '@/shared/api/http';
import { useAuth } from '@/shared/auth/authStore';
import { changeLanguage, type Lang } from '@/shared/i18n';
import { useUi } from '@/shared/hooks/useUiStore';
import { fmt } from '@/shared/utils/format';
import { logout } from '@/features/auth/api';
import { Svg } from './navIcons';

interface NotificationItem { id: string; type: string; titleVi: string; titleEn?: string; link?: string; createdAt: string; readAt?: string | null }

const SUN = 'M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10zM12 1v2M12 21v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M1 12h2M21 12h2M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4';
const MOON = 'M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z';
const LANGS: readonly Lang[] = ['vi', 'en'];
const BELL = 'M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 0 1-3.4 0';

/** VI/EN segmented switch (design: header right). */
export function LanguageSwitch() {
  const { t, i18n } = useTranslation();
  const user = useAuth((s) => s.user);
  const current: Lang = i18n.language === 'en' ? 'en' : 'vi';
  const onChange = (v: Lang) => {
    void changeLanguage(v);
    if (user) void http.put('/profile/preferences', { language: v }).catch(() => undefined);
  };
  return (
    <div className="bm-seg" role="group" aria-label={t('language.label')}>
      {LANGS.map((l) => (
        <button key={l} type="button" className={current === l ? 'is-on' : undefined} aria-pressed={current === l} onClick={() => onChange(l)}>
          {l === 'vi' ? 'VI' : 'EN'}
        </button>
      ))}
    </div>
  );
}

export function ThemeSwitch() {
  const { t } = useTranslation();
  const mode = useUi((s) => s.theme);
  const setTheme = useUi((s) => s.setTheme);
  const user = useAuth((s) => s.user);
  const next = mode === 'dark' ? 'light' : 'dark';
  const label = t(mode === 'dark' ? 'theme.light' : 'theme.dark');
  return (
    <button
      type="button"
      className="bm-icon-btn"
      aria-label={label}
      title={label}
      onClick={() => {
        setTheme(next);
        if (user) void http.put('/profile/preferences', { theme: next }).catch(() => undefined);
      }}
    >
      <Svg d={mode === 'dark' ? SUN : MOON} stroke={1.8} />
    </button>
  );
}

function NotificationBell() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { data } = useQuery({
    queryKey: ['notifications'],
    queryFn: async () => (await http.get<{ items: NotificationItem[]; unread: number }>('/system/notifications', { params: { take: 15 } })).data,
    refetchInterval: 60_000,
  });
  const open = async (n: NotificationItem) => {
    if (!n.readAt) await http.post(`/system/notifications/${n.id}/read`);
    void qc.invalidateQueries({ queryKey: ['notifications'] });
    if (n.link) navigate(n.link);
  };
  const unread = data?.unread ?? 0;
  const content = (
    <div style={{ width: 340, maxWidth: '86vw' }}>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }}>
        <Typography.Text strong>{t('header.notifications')}</Typography.Text>
        <Button size="small" type="link" onClick={async () => { await http.post('/system/notifications/read-all'); void qc.invalidateQueries({ queryKey: ['notifications'] }); }}>
          {t('header.markAllRead')}
        </Button>
      </Space>
      {data?.items?.length ? (
        <List
          size="small"
          dataSource={data.items}
          style={{ maxHeight: 360, overflow: 'auto' }}
          renderItem={(n) => (
            <List.Item onClick={() => void open(n)} style={{ cursor: 'pointer', opacity: n.readAt ? 0.6 : 1 }}>
              <List.Item.Meta title={i18n.language === 'en' && n.titleEn ? n.titleEn : n.titleVi} description={fmt.dateTime(n.createdAt)} />
            </List.Item>
          )}
        />
      ) : (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('header.noNotifications')} />
      )}
    </div>
  );
  return (
    <Popover content={content} trigger="click" placement="bottomRight">
      <button type="button" className="bm-icon-btn" aria-label={t('header.notifications')}>
        <Svg d={BELL} stroke={1.8} />
        {unread > 0 && <span className="bm-badge">{unread > 99 ? '99+' : unread}</span>}
      </button>
    </Popover>
  );
}

function UserMenu({ isMobile }: { isMobile: boolean }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const user = useAuth((s) => s.user);
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent | KeyboardEvent) => {
      if (e instanceof KeyboardEvent ? e.key === 'Escape' : !ref.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', close);
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', close); };
  }, [open]);
  return (
    <div className="bm-user" ref={ref}>
      <button type="button" className="bm-user-btn" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}>
        <span className="bm-avatar">{(user?.fullName ?? '?').slice(0, 1).toUpperCase()}</span>
        {!isMobile && <span className="bm-user-name">{user?.fullName}</span>}
      </button>
      {open && (
        <div className="bm-user-menu" role="menu">
          <button type="button" role="menuitem" onClick={() => { setOpen(false); navigate('/profile'); }}>{t('actions.profile')}</button>
          <button type="button" role="menuitem" className="is-danger" onClick={() => void logout().then(() => navigate('/login?loggedOut=1'))}>{t('actions.logout')}</button>
        </div>
      )}
    </div>
  );
}

export function AppHeaderRight({ isMobile = false }: { isMobile?: boolean }) {
  return (
    <>
      <LanguageSwitch />
      <ThemeSwitch />
      <NotificationBell />
      <UserMenu isMobile={isMobile} />
    </>
  );
}
