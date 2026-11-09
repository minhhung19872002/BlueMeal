import { useEffect, useState } from 'react';
import { App, Button, Card, Col, Divider, Form, Input, InputNumber, Row, Select, Space, Switch, Table, Tabs, Typography } from 'antd';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { EnumSelect, LookupSelect } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';

type F = { name: string; type: 'number' | 'bool' | 'text' | 'enum' | 'roles' | 'warehouse' | 'textarea'; enumName?: keyof typeof ENUMS; min?: number; max?: number; step?: number };
interface Section { key: string; group: string; title?: string; fields: F[] }

const SECTIONS: Record<string, Section[]> = {
  security: [
    { key: 'security.password', group: 'password', title: 'settings.password.title', fields: [
      { name: 'minLength', type: 'number', min: 8, max: 128 }, { name: 'requireUpper', type: 'bool' }, { name: 'requireLower', type: 'bool' }, { name: 'requireDigit', type: 'bool' },
      { name: 'requireSpecial', type: 'bool' }, { name: 'historyCount', type: 'number', min: 0, max: 24 }, { name: 'expiryDays', type: 'number', min: 0, max: 3650 },
      { name: 'maxFailedAttempts', type: 'number', min: 3, max: 20 }, { name: 'lockoutMinutes', type: 'number', min: 1, max: 1440 }, { name: 'captchaAfterFailures', type: 'number', min: 1, max: 20 },
      { name: 'captchaIpMultiplier', type: 'number', min: 1, max: 100 },
    ] },
    { key: 'security.session', group: 'session', title: 'settings.session.title', fields: [
      { name: 'idleTimeoutMinutes', type: 'number', min: 5, max: 480 }, { name: 'maxConcurrentSessions', type: 'number', min: 1, max: 20 },
      { name: 'refreshTokenDays', type: 'number', min: 1, max: 30 }, { name: 'accessTokenMinutes', type: 'number', min: 5, max: 60 },
    ] },
    { key: 'security.mfa', group: 'mfa', title: 'settings.mfa.title', fields: [{ name: 'requiredRoles', type: 'roles' }, { name: 'allowEmailOtp', type: 'bool' }] },
  ],
  warning: [
    { key: 'nutrition.warning', group: 'warning', fields: [
      { name: 'energyWarnLowPct', type: 'number', min: 0, max: 100 }, { name: 'energyWarnHighPct', type: 'number', min: 100, max: 300 },
      { name: 'energyCriticalLowPct', type: 'number', min: 0, max: 100 }, { name: 'energyCriticalHighPct', type: 'number', min: 100, max: 300 },
      { name: 'costCriticalOverPct', type: 'number', min: 0, max: 100 }, { name: 'repeatDishDays', type: 'number', min: 0, max: 60 },
      { name: 'certExpiryWarnDays', type: 'number', min: 1, max: 365 }, { name: 'stockExpiryWarnDays', type: 'number', min: 1, max: 60 },
      { name: 'plgEnabled', type: 'bool' }, { name: 'animalProteinEnabled', type: 'bool' },
      { name: 'plgLevel', type: 'enum', enumName: 'WarningLevel' }, { name: 'animalProteinLevel', type: 'enum', enumName: 'WarningLevel' },
      { name: 'missingDataLevel', type: 'enum', enumName: 'WarningLevel' }, { name: 'allergenLevel', type: 'enum', enumName: 'WarningLevel' },
    ] },
  ],
  inventory: [{ key: 'inventory', group: 'inventory', fields: [{ name: 'costMethod', type: 'enum', enumName: 'InventoryCostMethod' }, { name: 'allowNegativeStock', type: 'bool' }, { name: 'defaultWarehouseId', type: 'warehouse' }] }],
  smtp: [{ key: 'smtp', group: 'smtp', fields: [
    { name: 'enabled', type: 'bool' }, { name: 'host', type: 'text' }, { name: 'port', type: 'number', min: 1, max: 65535 }, { name: 'useSsl', type: 'bool' },
    { name: 'userName', type: 'text' }, { name: 'password', type: 'text' }, { name: 'fromAddress', type: 'text' }, { name: 'fromName', type: 'text' },
  ] }],
  backup: [{ key: 'backup', group: 'backup', fields: [
    { name: 'cron', type: 'text' }, { name: 'keepDaily', type: 'number', min: 1, max: 90 }, { name: 'keepWeekly', type: 'number', min: 0, max: 52 },
    { name: 'keepMonthly', type: 'number', min: 0, max: 60 }, { name: 'mirrorPath', type: 'text' },
  ] }],
  sample: [{ key: 'food_safety.sample', group: 'sample', fields: [{ name: 'retentionHours', type: 'number', min: 24, max: 720 }, { name: 'minWeightG', type: 'number', min: 50, max: 1000 }] }],
  generator: [{ key: 'menu.generator', group: 'generator', fields: [
    { name: 'w1Energy', type: 'number', min: 0, max: 100, step: 0.5 }, { name: 'w2Plg', type: 'number', min: 0, max: 100, step: 0.5 }, { name: 'w3AnimalProtein', type: 'number', min: 0, max: 100, step: 0.5 },
    { name: 'w4Micro', type: 'number', min: 0, max: 100, step: 0.5 }, { name: 'w5Cost', type: 'number', min: 0, max: 100, step: 0.5 }, { name: 'w6Repeat', type: 'number', min: 0, max: 100, step: 0.5 },
    { name: 'w7CookingMethod', type: 'number', min: 0, max: 100, step: 0.5 }, { name: 'candidates', type: 'number', min: 20, max: 2000 },
    { name: 'topForLocalSearch', type: 'number', min: 1, max: 50 }, { name: 'maxLocalSearchIterations', type: 'number', min: 0, max: 5000 }, { name: 'maxFriedPerMeal', type: 'number', min: 0, max: 5 },
  ] }],
  approval: [{ key: 'menu.approval', group: 'approval', fields: [{ name: 'levels', type: 'number', min: 1, max: 2 }, { name: 'notifyByEmail', type: 'bool' }] }],
  audit: [{ key: 'audit', group: 'audit', fields: [{ name: 'retentionMonths', type: 'number', min: 12, max: 120 }, { name: 'logReads', type: 'bool' }] }],
  system: [
    { key: 'system.maintenance', group: 'system', fields: [{ name: 'enabled', type: 'bool' }, { name: 'messageVi', type: 'textarea' }, { name: 'messageEn', type: 'textarea' }] },
    { key: 'system.swagger', group: 'system', fields: [{ name: 'enabledInProduction', type: 'bool' }] },
  ],
};

const LABEL_ALIASES: Record<string, string> = { 'system.enabled': 'maintenance', 'system.messageVi': 'maintenanceVi', 'system.messageEn': 'maintenanceEn', 'system.enabledInProduction': 'swagger' };

function SettingForm({ section, value, canEdit, onSaved }: { section: Section; value: Record<string, unknown>; canEdit: boolean; onSaved: () => void }) {
  const { t, i18n } = useTranslation('admin');
  const { message } = App.useApp();
  const [form] = Form.useForm();
  const [dirty, setDirty] = useState(false);
  const roles = useQuery({ queryKey: ['roles'], enabled: section.fields.some((f) => f.type === 'roles'), queryFn: async () => (await http.get<{ code: string; nameVi: string; nameEn?: string }[]>('/roles')).data });
  useEffect(() => {
    form.setFieldsValue(value);
    setDirty(false);
  }, [value, form]);
  const label = (f: F) => t(`settings.${section.group}.${LABEL_ALIASES[`${section.group}.${f.name}`] ?? f.name}`);
  const save = async () => {
    const v = await form.validateFields();
    try {
      await http.put(`/settings/${section.key}`, { ...value, ...v });
      message.success(t('settings.saved'));
      setDirty(false);
      onSaved();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  return (
    <Card size="small" title={section.title ? t(section.title) : undefined} style={{ marginBottom: 16 }}>
      <Form form={form} layout="vertical" disabled={!canEdit} onValuesChange={() => setDirty(true)}>
        <Row gutter={16}>
          {section.fields.map((f) => (
            <Col xs={24} md={f.type === 'textarea' || f.type === 'roles' ? 24 : 12} lg={f.type === 'textarea' || f.type === 'roles' ? 24 : 8} key={f.name}>
              <Form.Item name={f.name} label={label(f)} valuePropName={f.type === 'bool' ? 'checked' : 'value'}>
                {f.type === 'bool' ? <Switch /> :
                  f.type === 'number' ? <InputNumber min={f.min} max={f.max} step={f.step} style={{ width: '100%' }} decimalSeparator="," /> :
                  f.type === 'enum' ? <EnumSelect enumName={f.enumName!} values={[...ENUMS[f.enumName!]]} /> :
                  f.type === 'roles' ? <Select mode="multiple" options={roles.data?.map((r) => ({ value: r.code, label: i18n.language === 'en' && r.nameEn ? r.nameEn : r.nameVi }))} /> :
                  f.type === 'warehouse' ? <LookupSelect resource="warehouses" /> :
                  f.type === 'textarea' ? <Input.TextArea rows={2} /> :
                  <Input type={f.name === 'password' ? 'password' : 'text'} autoComplete="off" />}
              </Form.Item>
            </Col>
          ))}
        </Row>
      </Form>
      {section.key === 'backup' && <Typography.Paragraph type="secondary">{t('settings.backup.cronHint')}</Typography.Paragraph>}
      {canEdit && <Button type="primary" disabled={!dirty} onClick={() => void save()}>{t('common:actions.save')}</Button>}
    </Card>
  );
}

/** Overhead items editor for meal cost (SPEC §6.3, §7.7). */
function CostSettings({ value, canEdit, onSaved }: { value: any; canEdit: boolean; onSaved: () => void }) {
  const { t } = useTranslation('admin');
  const { message } = App.useApp();
  const [priceMode, setPriceMode] = useState<string>(value?.priceMode);
  const [items, setItems] = useState<any[]>(value?.overheads ?? []);
  useEffect(() => {
    setPriceMode(value?.priceMode);
    setItems(value?.overheads ?? []);
  }, [value]);
  const update = (i: number, k: string, v: unknown) => setItems(items.map((x, j) => (j === i ? { ...x, [k]: v } : x)));
  return (
    <Card size="small">
      <Space direction="vertical" style={{ width: '100%' }}>
        <Typography.Text>{t('settings.cost.priceMode')}</Typography.Text>
        <EnumSelect enumName="CostPriceMode" values={[...ENUMS.CostPriceMode]} value={priceMode} onChange={setPriceMode} disabled={!canEdit} style={{ maxWidth: 360 }} />
        <Divider orientation="left">{t('settings.cost.overheads')}</Divider>
        <Table
          size="small"
          rowKey={(_, i) => String(i)}
          pagination={false}
          dataSource={items}
          scroll={{ x: 700 }}
          columns={[
            { title: t('settings.cost.code'), dataIndex: 'code', render: (v, _r, i) => <Input value={v} disabled={!canEdit} onChange={(e) => update(i, 'code', e.target.value)} /> },
            { title: t('settings.cost.nameVi'), dataIndex: 'nameVi', render: (v, _r, i) => <Input value={v} disabled={!canEdit} onChange={(e) => update(i, 'nameVi', e.target.value)} /> },
            { title: t('settings.cost.nameEn'), dataIndex: 'nameEn', render: (v, _r, i) => <Input value={v} disabled={!canEdit} onChange={(e) => update(i, 'nameEn', e.target.value)} /> },
            { title: t('settings.cost.mode'), dataIndex: 'mode', width: 220, render: (v, _r, i) => <EnumSelect enumName="OverheadMode" values={[...ENUMS.OverheadMode]} value={v} disabled={!canEdit} onChange={(x) => update(i, 'mode', x)} /> },
            { title: t('settings.cost.value'), dataIndex: 'value', width: 140, render: (v, _r, i) => <InputNumber value={v} min={0} disabled={!canEdit} onChange={(x) => update(i, 'value', x ?? 0)} style={{ width: '100%' }} decimalSeparator="," /> },
            { title: '', key: 'd', width: 50, render: (_, _r, i) => canEdit && <Button type="text" danger icon={<DeleteOutlined />} onClick={() => setItems(items.filter((_, j) => j !== i))} /> },
          ]}
        />
        {canEdit && (
          <Space>
            <Button icon={<PlusOutlined />} onClick={() => setItems([...items, { code: 'NEW', nameVi: '', nameEn: '', mode: 'FixedPerServing', value: 0 }])}>{t('settings.cost.add')}</Button>
            <Button type="primary" onClick={async () => {
              try { await http.put('/settings/cost', { priceMode, overheads: items }); message.success(t('settings.saved')); onSaved(); } catch (e) { message.error(problemMessage(e)); }
            }}>{t('common:actions.save')}</Button>
          </Space>
        )}
      </Space>
    </Card>
  );
}

function SmtpTest() {
  const { t } = useTranslation('admin');
  const { message } = App.useApp();
  const [to, setTo] = useState('');
  return (
    <Space.Compact style={{ maxWidth: 420, width: '100%' }}>
      <Input placeholder={t('settings.smtp.testTo')} value={to} onChange={(e) => setTo(e.target.value)} />
      <Button onClick={async () => { try { await http.post('/settings/smtp/test', { to }); message.success(t('settings.smtp.sent')); } catch (e) { message.error(problemMessage(e)); } }}>{t('settings.smtp.test')}</Button>
    </Space.Compact>
  );
}

/** System configuration screen (SPEC §7.1.13). */
export default function SettingsPage() {
  const { t } = useTranslation('admin');
  const { has } = usePermission();
  const canEdit = has('system.setting_edit');
  const { data, refetch } = useQuery({ queryKey: ['settings'], queryFn: async () => (await http.get<Record<string, any>>('/settings')).data });
  if (!data) return null;
  const tab = (key: string) =>
    SECTIONS[key].map((s) => <SettingForm key={s.key} section={s} value={data[s.key] ?? {}} canEdit={canEdit} onSaved={() => void refetch()} />);
  return (
    <Card title={t('settings.title')}>
      <Tabs
        tabPosition="top"
        items={[
          { key: 'security', label: t('settings.tabs.security'), children: tab('security') },
          { key: 'warning', label: t('settings.tabs.warning'), children: tab('warning') },
          { key: 'inventory', label: t('settings.tabs.inventory'), children: tab('inventory') },
          { key: 'cost', label: t('settings.tabs.cost'), children: <CostSettings value={data['cost']} canEdit={canEdit} onSaved={() => void refetch()} /> },
          { key: 'smtp', label: t('settings.tabs.smtp'), children: <>{tab('smtp')}{canEdit && <SmtpTest />}</> },
          { key: 'backup', label: t('settings.tabs.backup'), children: tab('backup') },
          { key: 'sample', label: t('settings.tabs.sample'), children: tab('sample') },
          { key: 'generator', label: t('settings.tabs.generator'), children: tab('generator') },
          { key: 'approval', label: t('settings.tabs.approval'), children: tab('approval') },
          { key: 'audit', label: t('settings.tabs.audit'), children: tab('audit') },
          { key: 'system', label: t('settings.tabs.system'), children: tab('system') },
        ]}
      />
    </Card>
  );
}
