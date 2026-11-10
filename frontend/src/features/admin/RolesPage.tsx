import { useEffect, useMemo, useState } from 'react';
import { App, Form, Input, Modal, Popconfirm, Switch, Spin } from 'antd';
import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { zodRule, zs } from '@/shared/utils/validation';
import { PageHead, PillTabs, Section, Toggle, XButton } from '@/app/layout/designKit';

interface Role { id: string; code: string; nameVi: string; nameEn?: string; description?: string; isSystem: boolean; requiresMfa: boolean; isActive: boolean; userCount: number; permissionCount: number }
interface Perm { id: string; code: string; module: string; action: string; nameVi: string; nameEn: string; moduleNameVi: string; moduleNameEn: string; actionNameVi: string; actionNameEn: string }

const MATRIX_ACTIONS = ['view', 'create', 'edit', 'delete', 'approve', 'export', 'import'];

function Check() {
  return <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 12l5 5L20 6" /></svg>;
}

/** Roles + module × action permission matrix (SPEC §7.1.5, §7.1.9) — layout: Claude Design BMAdmin (role pills, 44px touch cells). */
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
  // keep the selected role object fresh after list refetches (counts, MFA flag)
  useEffect(() => {
    if (selected && roles.data) {
      const fresh = roles.data.find((r) => r.id === selected.id);
      if (fresh && fresh !== selected) setSelected(fresh);
    }
  }, [roles.data, selected]);

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
  const toggleRow = (m: { perms: Perm[] }) => {
    const on = !m.perms.every((p) => granted.has(p.code));
    const next = new Set(granted);
    m.perms.forEach((p) => (on ? next.add(p.code) : next.delete(p.code)));
    setGranted(next);
    setDirty(true);
  };

  const pick = (r: Role) => {
    if (selected?.id === r.id) return;
    if (dirty && !window.confirm(t('common:confirmLeave'))) return;
    setSelected(r);
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

  /** "Bắt buộc MFA" switch in the matrix header: same PUT /roles/{id} as the edit dialog. */
  const setMfa = async (requiresMfa: boolean) => {
    if (!selected) return;
    try {
      await http.put(`/roles/${selected.id}`, { code: selected.code, nameVi: selected.nameVi, nameEn: selected.nameEn, description: selected.description, requiresMfa, isActive: selected.isActive });
      void roles.refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const roleName = (r: Role) => (en && r.nameEn ? r.nameEn : r.nameVi);
  const activeCount = locked ? perms.data?.length ?? 0 : granted.size;

  return (
    <div className="bm-page">
      <PageHead title={t('roles.pageTitle')} actions={
        <>
          {has('role.create') && <XButton onClick={() => { setEditing('new'); form.resetFields(); form.setFieldsValue({ isActive: true }); }}><PlusOutlined />{t('roles.add')}</XButton>}
          {selected && canEdit && <XButton kind="primary" disabled={!dirty} onClick={() => void saveMatrix()} data-testid="save-matrix">{t('roles.saveMatrix')}</XButton>}
        </>
      } />
      {roles.isLoading ? <Spin /> : (
        <PillTabs label={t('roles.title')} value={selected?.id ?? ''} onChange={(id) => { const r = roles.data?.find((x) => x.id === id); if (r) pick(r); }}
          items={(roles.data ?? []).map((r) => ({ key: r.id, label: <>{roleName(r)}<span className="bm-x-tab-n">{r.userCount}</span></> }))} />
      )}
      {!selected ? (
        <div className="bm-x-empty">{t('roles.selectRole')}</div>
      ) : (
        <Section
          title={`${t('roles.matrix')} — ${roleName(selected)}`}
          hint={<>{selected.code} · {t('roles.activeCount', { n: activeCount })}{locked ? ` · ${t('roles.systemLocked')}` : ''}</>}
          extra={
            <div className="bm-ph-actions">
              <Toggle checked={selected.requiresMfa} disabled={!has('role.edit')} onChange={(v) => void setMfa(v)} onText={t('roles.requiresMfa')} offText={t('roles.requiresMfa')} label={t('roles.requiresMfa')} />
              {has('role.edit') && <XButton onClick={() => { setEditing(selected); form.setFieldsValue(selected); }} aria-label={t('roles.editRole')}><EditOutlined />{t('common:actions.edit')}</XButton>}
              {has('role.delete') && !selected.isSystem && (
                <Popconfirm title={t('common:confirmDelete')} onConfirm={async () => { try { await http.delete(`/roles/${selected.id}`); setSelected(null); void roles.refetch(); } catch (e) { message.error(problemMessage(e)); } }}>
                  <XButton kind="danger" aria-label={t('roles.deleteRole')}><DeleteOutlined />{t('common:actions.delete')}</XButton>
                </Popconfirm>
              )}
            </div>
          }
          bodyClass="bm-x-sec-table"
        >
          <div className="bm-x-pad"><Input.Search placeholder={t('roles.search')} allowClear onChange={(e) => setFilter(e.target.value)} style={{ maxWidth: 320 }} /></div>
          {detail.isFetching && !detail.data ? <div className="bm-x-pad"><Spin /></div> : (
            <div style={{ overflowX: 'auto' }}>
              <table className="bm-x-matrix">
                <thead>
                  <tr>
                    <th>{t('roles.module')}</th>
                    {MATRIX_ACTIONS.map((a) => <th key={a}>{t(`roles.action.${a}`, a)}</th>)}
                    <th>{t('roles.other')}</th>
                  </tr>
                </thead>
                <tbody>
                  {modules.map((m) => (
                    <tr key={m.module}>
                      <th className={canEdit ? 'is-click' : undefined} onClick={canEdit ? () => toggleRow(m) : undefined} title={canEdit ? t('roles.toggleRow') : undefined}>{m.name}</th>
                      {MATRIX_ACTIONS.map((a) => {
                        const p = m.perms.find((x) => x.action === a);
                        if (!p) return <td key={a} />;
                        const on = locked || granted.has(p.code);
                        return (
                          <td key={a}>
                            <button type="button" className={on ? 'bm-x-cell is-on' : 'bm-x-cell'} disabled={!canEdit} aria-pressed={on}
                              aria-label={`${m.name} – ${t(`roles.action.${a}`, a)}`} onClick={() => toggle(p.code, !on)}>
                              <span className="bm-x-box">{on && <Check />}</span>
                            </button>
                          </td>
                        );
                      })}
                      <td className="is-other">
                        {m.perms.filter((p) => !MATRIX_ACTIONS.includes(p.action)).map((p) => {
                          const on = locked || granted.has(p.code);
                          return (
                            <button key={p.code} type="button" className={on ? 'bm-x-chip-check is-on' : 'bm-x-chip-check'} disabled={!canEdit} aria-pressed={on} onClick={() => toggle(p.code, !on)}>
                              {en ? p.actionNameEn : p.actionNameVi}
                            </button>
                          );
                        })}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Section>
      )}
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
    </div>
  );
}
