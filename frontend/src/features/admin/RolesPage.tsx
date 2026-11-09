import { useEffect, useMemo, useState } from 'react';
import { App, Button, Card, Checkbox, Col, Empty, Form, Input, List, Modal, Popconfirm, Row, Space, Switch, Table, Tag, Tooltip, Typography } from 'antd';
import { DeleteOutlined, EditOutlined, PlusOutlined, SafetyOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { zodRule, zs } from '@/shared/utils/validation';

interface Role { id: string; code: string; nameVi: string; nameEn?: string; description?: string; isSystem: boolean; requiresMfa: boolean; isActive: boolean; userCount: number; permissionCount: number }
interface Perm { id: string; code: string; module: string; action: string; nameVi: string; nameEn: string; moduleNameVi: string; moduleNameEn: string; actionNameVi: string; actionNameEn: string }

const MATRIX_ACTIONS = ['view', 'create', 'edit', 'delete', 'approve', 'export', 'import'];

/** Roles + module × action permission matrix (SPEC §7.1.5, §7.1.9). */
export default function RolesPage() {
  const { t, i18n } = useTranslation('admin');
  const { message } = App.useApp();
  const qc = useQueryClient();
  const { has } = usePermission();
  const en = i18n.language === 'en';
  const [selected, setSelected] = useState<Role | null>(null);
  const [granted, setGranted] = useState<Set<string>>(new Set());
  const [dirty, setDirty] = useState(false);
  const [filter, setFilter] = useState('');
  const [editing, setEditing] = useState<Role | 'new' | null>(null);
  const [form] = Form.useForm();

  const roles = useQuery({ queryKey: ['roles'], queryFn: async () => (await http.get<Role[]>('/roles')).data });
  const perms = useQuery({ queryKey: ['permissions'], queryFn: async () => (await http.get<Perm[]>('/permissions')).data });
  const detail = useQuery({
    queryKey: ['role', selected?.id],
    enabled: !!selected,
    queryFn: async () => (await http.get<{ permissions: string[] }>(`/roles/${selected!.id}`)).data,
  });
  useEffect(() => {
    if (detail.data) {
      setGranted(new Set(detail.data.permissions));
      setDirty(false);
    }
  }, [detail.data]);

  const modules = useMemo(() => {
    const map = new Map<string, { module: string; name: string; perms: Perm[] }>();
    for (const p of perms.data ?? []) {
      const m = map.get(p.module) ?? { module: p.module, name: en ? p.moduleNameEn : p.moduleNameVi, perms: [] };
      m.perms.push(p);
      map.set(p.module, m);
    }
    const f = filter.trim().toLowerCase();
    return [...map.values()].filter((m) => !f || m.name.toLowerCase().includes(f) || m.module.includes(f));
  }, [perms.data, en, filter]);

  const locked = selected?.code === 'SYS_ADMIN';
  const canEdit = has('role.edit') && !locked;
  const toggle = (code: string, on: boolean) => {
    const next = new Set(granted);
    if (on) next.add(code);
    else next.delete(code);
    setGranted(next);
    setDirty(true);
  };
  const toggleRow = (m: { perms: Perm[] }, on: boolean) => {
    const next = new Set(granted);
    m.perms.forEach((p) => (on ? next.add(p.code) : next.delete(p.code)));
    setGranted(next);
    setDirty(true);
  };

  const saveMatrix = async () => {
    if (!selected) return;
    try {
      await http.put(`/roles/${selected.id}/permissions`, { permissions: [...granted] });
      message.success(t('common:saved'));
      setDirty(false);
      void qc.invalidateQueries({ queryKey: ['roles'] });
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const saveRole = async () => {
    const v = await form.validateFields();
    try {
      if (editing === 'new') await http.post('/roles', v);
      else if (editing) await http.put(`/roles/${editing.id}`, v);
      setEditing(null);
      void roles.refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const columns = [
    { key: 'module', title: t('roles.module'), fixed: 'left' as const, width: 240, render: (_: unknown, m: (typeof modules)[number]) => (
      <Space>
        {canEdit && <Checkbox checked={m.perms.every((p) => granted.has(p.code))} indeterminate={m.perms.some((p) => granted.has(p.code)) && !m.perms.every((p) => granted.has(p.code))} onChange={(e) => toggleRow(m, e.target.checked)} />}
        <span>{m.name}</span>
      </Space>
    ) },
    ...MATRIX_ACTIONS.map((a) => ({
      key: a,
      title: t(`roles.action.${a}`, a),
      width: 80,
      align: 'center' as const,
      render: (_: unknown, m: (typeof modules)[number]) => {
        const p = m.perms.find((x) => x.action === a);
        return p ? <Checkbox checked={locked || granted.has(p.code)} disabled={!canEdit} onChange={(e) => toggle(p.code, e.target.checked)} /> : null;
      },
    })),
    { key: 'other', title: t('roles.other'), render: (_: unknown, m: (typeof modules)[number]) => (
      <Space wrap size={4}>
        {m.perms.filter((p) => !MATRIX_ACTIONS.includes(p.action)).map((p) => (
          <Checkbox key={p.code} checked={locked || granted.has(p.code)} disabled={!canEdit} onChange={(e) => toggle(p.code, e.target.checked)}>
            {en ? p.actionNameEn : p.actionNameVi}
          </Checkbox>
        ))}
      </Space>
    ) },
  ];

  return (
    <Row gutter={16}>
      <Col xs={24} lg={7}>
        <Card title={t('roles.title')} extra={has('role.create') && <Button size="small" icon={<PlusOutlined />} onClick={() => { setEditing('new'); form.resetFields(); form.setFieldsValue({ isActive: true }); }}>{t('common:actions.add')}</Button>}>
          <List
            loading={roles.isLoading}
            dataSource={roles.data}
            renderItem={(r) => (
              <List.Item
                onClick={() => {
                  if (dirty && !window.confirm(t('common:confirmLeave'))) return;
                  setSelected(r);
                }}
                style={{ cursor: 'pointer', background: selected?.id === r.id ? 'rgba(63,143,90,0.08)' : undefined, paddingInline: 8, borderRadius: 6 }}
                actions={[
                  has('role.edit') && <Tooltip key="e" title={t('common:actions.edit')}><Button size="small" type="text" icon={<EditOutlined />} onClick={(e) => { e.stopPropagation(); setEditing(r); form.setFieldsValue(r); }} /></Tooltip>,
                  has('role.delete') && !r.isSystem && (
                    <Popconfirm key="d" title={t('common:confirmDelete')} onConfirm={async () => { try { await http.delete(`/roles/${r.id}`); void roles.refetch(); } catch (e) { message.error(problemMessage(e)); } }}>
                      <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={(e) => e.stopPropagation()} />
                    </Popconfirm>
                  ),
                ].filter(Boolean)}
              >
                <List.Item.Meta
                  title={<Space>{en && r.nameEn ? r.nameEn : r.nameVi}{r.isSystem && <Tag>{t('roles.system')}</Tag>}{r.requiresMfa && <Tooltip title={t('roles.requiresMfa')}><SafetyOutlined /></Tooltip>}</Space>}
                  description={`${r.code} · ${t('roles.users')}: ${r.userCount} · ${t('roles.permissions')}: ${r.permissionCount}`}
                />
              </List.Item>
            )}
          />
        </Card>
      </Col>
      <Col xs={24} lg={17}>
        <Card
          title={selected ? `${t('roles.matrix')} — ${en && selected.nameEn ? selected.nameEn : selected.nameVi}` : t('roles.matrix')}
          extra={selected && canEdit && <Button type="primary" disabled={!dirty} onClick={() => void saveMatrix()} data-testid="save-matrix">{t('roles.saveMatrix')}</Button>}
        >
          {!selected ? (
            <Empty description={t('roles.selectRole')} />
          ) : (
            <>
              {locked && <Typography.Paragraph type="secondary">{t('roles.systemLocked')}</Typography.Paragraph>}
              <Input.Search placeholder={t('roles.search')} allowClear onChange={(e) => setFilter(e.target.value)} style={{ maxWidth: 320, marginBottom: 12 }} />
              <Table rowKey="module" size="small" columns={columns} dataSource={modules} pagination={false} scroll={{ x: 1000, y: 560 }} loading={detail.isFetching} />
            </>
          )}
        </Card>
      </Col>
      <Modal open={!!editing} title={editing === 'new' ? t('common:actions.add') : t('common:actions.edit')} onCancel={() => setEditing(null)} onOk={() => void saveRole()} destroyOnHidden>
        <Form form={form} layout="vertical">
          <Form.Item name="code" label={t('roles.code')} rules={[zodRule(z.string().regex(/^[A-Z0-9_]+$/, 'catalog:validation.code'))]}>
            <Input disabled={editing !== 'new'} />
          </Form.Item>
          <Form.Item name="nameVi" label={t('roles.nameVi')} rules={[zodRule(zs.required(200))]}><Input /></Form.Item>
          <Form.Item name="nameEn" label={t('roles.nameEn')}><Input /></Form.Item>
          <Form.Item name="description" label={t('common:fields.description')}><Input.TextArea rows={2} /></Form.Item>
          <Form.Item name="requiresMfa" label={t('roles.requiresMfa')} valuePropName="checked"><Switch /></Form.Item>
          <Form.Item name="isActive" label={t('common:fields.isActive')} valuePropName="checked"><Switch /></Form.Item>
        </Form>
      </Modal>
    </Row>
  );
}
