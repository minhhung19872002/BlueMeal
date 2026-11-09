import { Avatar, Badge, Button, Dropdown, Empty, List, Popover, Segmented, Space, Tooltip, Typography } from 'antd';
import { BellOutlined, LogoutOutlined, MoonOutlined, SunOutlined, UserOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http } from '@/shared/api/http';
import { useAuth } from '@/shared/auth/authStore';
import { changeLanguage, type Lang } from '@/shared/i18n';
import { useUi } from '@/shared/hooks/useUiStore';
import { fmt } from '@/shared/utils/format';
import { logout } from '@/features/auth/api';

interface NotificationItem { id: string; type: string; titleVi: string; titleEn?: string; link?: string; createdAt: string; readAt?: string | null }

export function LanguageSwitch() {
  const { i18n } = useTranslation();
  const user = useAuth((s) => s.user);
  const onChange = (v: Lang) => {
    void changeLanguage(v);
    if (user) void http.put('/profile/preferences', { language: v }).catch(() => undefined);
  };
  return (
    <Segmented
      size="small"
      value={i18n.language === 'en' ? 'en' : 'vi'}
      onChange={(v) => onChange(v as Lang)}
      options={[{ label: 'VI', value: 'vi' }, { label: 'EN', value: 'en' }]}
    />
  );
}

export function ThemeSwitch() {
  const { t } = useTranslation();
  const mode = useUi((s) => s.theme);
  const setTheme = useUi((s) => s.setTheme);
  const user = useAuth((s) => s.user);
  const next = mode === 'dark' ? 'light' : 'dark';
  return (
    <Tooltip title={t(mode === 'dark' ? 'theme.light' : 'theme.dark')}>
      <Button
        type="text"
        aria-label={t(mode === 'dark' ? 'theme.light' : 'theme.dark')}
        icon={mode === 'dark' ? <SunOutlined /> : <MoonOutlined />}
        onClick={() => {
          setTheme(next);
          if (user) void http.put('/profile/preferences', { theme: next }).catch(() => undefined);
        }}
      />
    </Tooltip>
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
      <Badge count={data?.unread ?? 0} size="small" overflowCount={99}>
        <Button type="text" aria-label={t('header.notifications')} icon={<BellOutlined style={{ fontSize: 18 }} />} />
      </Badge>
    </Popover>
  );
}

export function AppHeaderRight() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const user = useAuth((s) => s.user);
  return (
    <Space size={8} wrap={false}>
      <LanguageSwitch />
      <ThemeSwitch />
      <NotificationBell />
      <Dropdown
        menu={{
          items: [
            { key: 'profile', icon: <UserOutlined />, label: t('actions.profile'), onClick: () => navigate('/profile') },
            { type: 'divider' },
            { key: 'logout', icon: <LogoutOutlined />, danger: true, label: t('actions.logout'), onClick: () => void logout().then(() => navigate('/login?loggedOut=1')) },
          ],
        }}
      >
        <Space style={{ cursor: 'pointer' }}>
          <Avatar size="small" style={{ backgroundColor: '#3f8f5a' }}>{(user?.fullName ?? '?').slice(0, 1).toUpperCase()}</Avatar>
          <span className="bm-hide-mobile">{user?.fullName}</span>
        </Space>
      </Dropdown>
    </Space>
  );
}
