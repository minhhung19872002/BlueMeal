import { useState } from 'react';
import { Alert, App, Button, Dropdown, Form, Input, Modal, Select, Tag, Tooltip, Typography } from 'antd';
import { EyeInvisibleOutlined, UserAddOutlined, MoreOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { downloadFile, http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { CatalogPage } from '@/shared/components/catalog/CatalogPage';
import { useEnumLabel } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { fmt } from '@/shared/utils/format';
import { zodRule, zs } from '@/shared/utils/validation';

interface Employee { id: string; code: string; fullName: string; userId?: string | null; piiMasked?: boolean; [k: string]: any }
interface Role { id: string; code: string; nameVi: string; nameEn?: string }

function CreateAccountButton({ emp, onDone }: { emp: Employee; onDone: () => void }) {
  const { t, i18n } = useTranslation(['catalog', 'common']);
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const [temp, setTemp] = useState<string | null>(null);
  const [form] = Form.useForm();
  const { data: roles } = useQuery({ queryKey: ['roles'], enabled: open, queryFn: async () => (await http.get<Role[]>('/roles')).data });
  return (
    <>
      <Tooltip title={t('employees.createAccount')}>
        <Button type="text" icon={<UserAddOutlined />} onClick={() => { setOpen(true); setTemp(null); form.setFieldsValue({ userName: emp.code.toLowerCase(), roleIds: [] }); }} />
      </Tooltip>
      <Modal
        open={open}
        title={t('employees.createAccount') + ' — ' + emp.fullName}
        onCancel={() => setOpen(false)}
        okButtonProps={{ hidden: !!temp }}
        cancelText={temp ? t('common:actions.close') : t('common:actions.cancel')}
        onOk={async () => {
          const v = await form.validateFields();
          try {
            const r = await http.post<{ temporaryPassword: string }>(`/employees/${emp.id}/create-account`, v);
            setTemp(r.data.temporaryPassword);
            onDone();
          } catch (e) {
            message.error(problemMessage(e));
          }
        }}
      >
        {temp ? (
          <Alert type="success" showIcon message={t('employees.tempPassword')} description={<Typography.Text code copyable>{temp}</Typography.Text>} />
        ) : (
          <Form form={form} layout="vertical">
            <Form.Item name="userName" label={t('employees.userName')} rules={[zodRule(zs.required(64))]}>
              <Input />
            </Form.Item>
            <Form.Item name="roleIds" label={t('employees.roles')}>
              <Select mode="multiple" options={roles?.map((r) => ({ value: r.id, label: i18n.language === 'en' && r.nameEn ? r.nameEn : r.nameVi }))} />
            </Form.Item>
          </Form>
        )}
      </Modal>
    </>
  );
}

export default function EmployeesPage() {
  const { t } = useTranslation(['catalog', 'common']);
  const { message, modal } = App.useApp();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  return (
    <CatalogPage<Employee>
      config={{
        resource: 'employees',
        perm: 'employee',
        title: 'nav.employees',
        hasActive: true,
        defaultSort: 'code',
        columns: [
          { key: 'code', label: 'common:fields.code', width: 120, fixed: 'left', sortKey: 'code' },
          { key: 'fullName', label: 'catalog:f.fullName', width: 220, sortKey: 'fullName', mobile: true, render: (v: string, r) => <span>{v} {r.piiMasked && <Tooltip title={t('employees.masked')}><EyeInvisibleOutlined /></Tooltip>}</span> },
          { key: 'departmentNameVi', label: 'catalog:f.department', width: 180 },
          { key: 'position', label: 'catalog:f.position', width: 150 },
          { key: 'gender', label: 'catalog:f.gender', width: 90, render: (v) => enumLabel('Gender', v), defaultHidden: true },
          { key: 'dateOfBirth', label: 'catalog:f.dateOfBirth', width: 120, render: (v) => fmt.date(v), defaultHidden: true },
          { key: 'phone', label: 'catalog:f.phone', width: 130 },
          { key: 'email', label: 'catalog:f.email', width: 200 },
          { key: 'nationalId', label: 'catalog:f.nationalId', width: 140, defaultHidden: true },
          { key: 'userId', label: 'catalog:f.account', width: 100, render: (v) => (v ? <Tag color="green">✓</Tag> : null) },
        ],
        fields: [
          { name: 'code', label: 'common:fields.code', type: 'text', required: true, span: 8 },
          { name: 'fullName', label: 'catalog:f.fullName', type: 'text', required: true, span: 16 },
          { name: 'departmentId', label: 'catalog:f.department', type: 'lookup', lookup: 'departments' },
          { name: 'position', label: 'catalog:f.position', type: 'text' },
          { name: 'gender', label: 'catalog:f.gender', type: 'enum', enumName: 'Gender', enumValues: [...ENUMS.Gender], span: 8 },
          { name: 'dateOfBirth', label: 'catalog:f.dateOfBirth', type: 'date', span: 8 },
          { name: 'nationalId', label: 'catalog:f.nationalId', type: 'text', span: 8, schema: z.string().regex(/^\d{9}(\d{3})?$/, 'common:validation.number').optional().nullable() },
          { name: 'phone', label: 'catalog:f.phone', type: 'text' },
          { name: 'email', label: 'catalog:f.email', type: 'email', schema: zs.email() },
          { name: 'isActive', label: 'common:fields.isActive', type: 'switch', span: 6 },
        ],
        defaults: { gender: 'Common' },
        filters: [
          { name: 'departmentId', label: 'catalog:f.department', type: 'lookup', lookup: 'departments' },
          { name: 'hasAccount', label: 'catalog:f.hasAccount', type: 'select', options: [{ value: 'true', label: 'common:actions.yes' }, { value: 'false', label: 'common:actions.no' }] },
        ],
        rowActions: (r, reload) => (
          <>
            {!r.userId && has('user.create') && <CreateAccountButton emp={r} onDone={reload} />}
            {has('pii.manage') && (
              <Dropdown
                trigger={['click']}
                menu={{
                  items: [
                    { key: 'export', label: t('employees.exportPii'), onClick: () => void downloadFile(`/employees/${r.id}/personal-data`, undefined, `personal-data-${r.code}.json`) },
                    {
                      key: 'anon', danger: true, label: t('employees.anonymize'),
                      onClick: () => modal.confirm({
                        title: t('employees.anonymize'), content: t('employees.anonymizeConfirm'), okButtonProps: { danger: true },
                        onOk: async () => { try { await http.post(`/employees/${r.id}/anonymize`); reload(); } catch (e) { message.error(problemMessage(e)); } },
                      }),
                    },
                  ],
                }}
              >
                <Button type="text" icon={<MoreOutlined rotate={90} />} />
              </Dropdown>
            )}
          </>
        ),
      }}
    />
  );
}
