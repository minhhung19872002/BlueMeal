import { useEffect, useState } from 'react';
import { App, Button, Form, Image, Input, Upload } from 'antd';
import { UploadOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { useBeforeUnload } from '@/shared/hooks/useTablePrefs';
import { zodRule, zs } from '@/shared/utils/validation';
import { PageHead, Section, XButton } from '@/app/layout/designKit';

/** Organization information + logo shown on header and reports (SPEC §7.1.1) — layout: Claude Design BMAdmin. */
export default function OrganizationPage() {
  const { t } = useTranslation('admin');
  const { message } = App.useApp();
  const { has } = usePermission();
  const canEdit = has('organization.edit');
  const [form] = Form.useForm();
  const [dirty, setDirty] = useState(false);
  const [logoVersion, setLogoVersion] = useState(0);
  const { data, refetch } = useQuery({ queryKey: ['organization'], queryFn: async () => (await http.get('/organization')).data });
  useBeforeUnload(dirty);
  useEffect(() => {
    if (data) form.setFieldsValue(data);
  }, [data, form]);

  const save = async () => {
    const v = await form.validateFields();
    try {
      await http.put('/organization', { ...v, version: data?.version });
      message.success(t('common:saved'));
      setDirty(false);
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <div className="bm-page">
      <PageHead title={t('org.title')} actions={canEdit && <XButton kind="primary" onClick={() => void save()} disabled={!dirty}>{t('common:actions.save')}</XButton>} />
      <Section title={t('org.general')} hint={t('org.generalHint')}>
        <Form form={form} layout="vertical" disabled={!canEdit} onValuesChange={() => setDirty(true)} className="bm-x-grid">
          <Form.Item name="name" label={t('org.name')} rules={[zodRule(zs.required(300))]}><Input /></Form.Item>
          <Form.Item name="nameEn" label={t('org.nameEn')}><Input /></Form.Item>
          <Form.Item name="taxCode" label={t('org.taxCode')} rules={[zodRule(z.string().regex(/^\d{10}(-\d{3})?$/, 'common:validation.number').optional().nullable())]}><Input /></Form.Item>
          <Form.Item name="phone" label={t('org.phone')}><Input /></Form.Item>
          <Form.Item name="email" label={t('org.email')} rules={[zodRule(zs.email())]}><Input /></Form.Item>
          <Form.Item name="website" label={t('org.website')}><Input /></Form.Item>
          <Form.Item name="address" label={t('org.address')} className="bm-x-wide"><Input /></Form.Item>
          <Form.Item name="representative" label={t('org.representative')}><Input /></Form.Item>
          <Form.Item name="representativeTitle" label={t('org.representativeTitle')}><Input /></Form.Item>
        </Form>
      </Section>
      <Section title={t('org.logo')} hint={t('org.logoHint')}
        foot={canEdit && (
          <Upload
            accept=".png,.jpg,.jpeg,.webp"
            showUploadList={false}
            customRequest={async ({ file, onSuccess, onError }) => {
              try {
                const fd = new FormData();
                fd.append('file', file as File);
                await http.post('/organization/logo', fd);
                setLogoVersion((x) => x + 1);
                void refetch();
                onSuccess?.({});
              } catch (e) {
                message.error(problemMessage(e));
                onError?.(e as Error);
              }
            }}
          >
            <Button icon={<UploadOutlined />}>{t('org.uploadLogo')}</Button>
          </Upload>
        )}>
        <div className="bm-x-logo">
          {data?.logoFileId
            ? <Image src={`/api/v1/organization/logo?v=${logoVersion}`} width={160} alt={t('org.logo')} preview={false} />
            : <div className="bm-x-field-v">{t('org.noLogo')}</div>}
        </div>
      </Section>
    </div>
  );
}
