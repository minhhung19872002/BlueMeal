import { useState } from 'react';
import { App, Button, Modal, Space, Typography } from 'antd';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { http, problemMessage } from '@/shared/api/http';
import { useAuth } from '@/shared/auth/authStore';
import { fmt } from '@/shared/utils/format';
import { logout } from './api';

export interface PrivacyNoticeDto {
  enabled: boolean;
  version: number;
  publishedAt?: string | null;
  contentVi: string;
  contentEn: string;
  language: string;
  accepted: boolean;
  acceptedVersion?: number | null;
  acceptedAt?: string | null;
}

export const PRIVACY_NOTICE_KEY = ['privacy-notice'];

export function usePrivacyNotice(enabled = true) {
  return useQuery({
    queryKey: PRIVACY_NOTICE_KEY,
    queryFn: async () => (await http.get<PrivacyNoticeDto>('/profile/privacy-notice')).data,
    enabled,
  });
}

export function noticeText(n: PrivacyNoticeDto | undefined, lang: string) {
  if (!n) return '';
  return lang === 'en' ? n.contentEn || n.contentVi : n.contentVi || n.contentEn;
}

export function NoticeBody({ text }: { text: string }) {
  return (
    <div style={{ whiteSpace: 'pre-wrap', maxHeight: '55vh', overflowY: 'auto', lineHeight: 1.6 }} data-testid="privacy-text">
      {text}
    </div>
  );
}

/**
 * Personal-data processing notice (Luật 91/2025/QH15): shown, non-dismissible, until the signed-in user accepts the
 * current version. The server stores the consent (version, time, IP, user agent) and writes an audit entry.
 */
export function PrivacyConsentGate() {
  const { t, i18n } = useTranslation('auth');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const user = useAuth((s) => s.user);
  const setUser = useAuth((s) => s.setUser);
  const required = !!user?.privacyConsentRequired;
  const notice = usePrivacyNotice(required);
  const [busy, setBusy] = useState(false);

  if (!required || !user) return null;

  const accept = async () => {
    if (!notice.data) return;
    setBusy(true);
    try {
      await http.post('/profile/privacy-consent', { version: notice.data.version, language: i18n.language === 'en' ? 'en' : 'vi' });
      setUser({ ...user, privacyConsentRequired: false });
      void qc.invalidateQueries({ queryKey: PRIVACY_NOTICE_KEY });
    } catch (e) {
      message.error(problemMessage(e));
      void notice.refetch(); // 409: the text changed while it was open
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      width={720}
      closable={false}
      maskClosable={false}
      keyboard={false}
      title={t('privacy.title')}
      footer={
        <Space>
          <Button onClick={() => void logout().then(() => navigate('/login?loggedOut=1'))}>{t('privacy.decline')}</Button>
          <Button type="primary" loading={busy} disabled={!notice.data} onClick={() => void accept()} data-testid="privacy-accept">
            {t('privacy.accept')}
          </Button>
        </Space>
      }
    >
      <Typography.Paragraph type="secondary">{t('privacy.intro')}</Typography.Paragraph>
      {notice.isLoading ? null : <NoticeBody text={noticeText(notice.data, i18n.language)} />}
    </Modal>
  );
}

/** Read-only view of the notice and the user's consent, for the profile page. */
export function PrivacyNoticeSummary() {
  const { t, i18n } = useTranslation('auth');
  const notice = usePrivacyNotice();
  const [open, setOpen] = useState(false);
  const n = notice.data;
  if (!n || !n.enabled) return null;
  return (
    <>
      <Space wrap>
        <span>
          {n.accepted && n.acceptedAt
            ? t('privacy.acceptedOn', { date: fmt.dateTime(n.acceptedAt), version: n.acceptedVersion })
            : t('privacy.notAccepted')}
        </span>
        <Button size="small" onClick={() => setOpen(true)}>{t('privacy.view')}</Button>
      </Space>
      <Modal open={open} width={720} title={t('privacy.title')} footer={null} onCancel={() => setOpen(false)}>
        <NoticeBody text={noticeText(n, i18n.language)} />
      </Modal>
    </>
  );
}
