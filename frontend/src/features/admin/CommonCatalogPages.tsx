import { useState } from 'react';
import { App, Button, Card, InputNumber, Modal, Segmented, Space, Tree, Typography } from 'antd';
import { ApartmentOutlined, SwapOutlined, UnorderedListOutlined } from '@ant-design/icons';
import type { TreeProps } from 'antd';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { CatalogPage, std } from '@/shared/components/catalog/CatalogPage';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import type { CatalogConfig } from '@/shared/components/catalog/types';
import { ENUMS } from '@/shared/utils/enums';
import { fmt } from '@/shared/utils/format';

type Any = { id: string; [k: string]: any };

// ------------------------------------------------------------------ departments (list + drag & drop tree)

const departmentConfig: CatalogConfig<Any> = {
  resource: 'departments',
  perm: 'department',
  title: 'nav.departments',
  columns: std.columns([{ key: 'parentNameVi', label: 'catalog:f.parent', width: 220 }, { key: 'description', label: 'catalog:f.description', width: 260, defaultHidden: true }]),
  fields: std.fields([
    { name: 'parentId', label: 'catalog:f.parent', type: 'lookup', lookup: 'departments', span: 24 },
    { name: 'description', label: 'catalog:f.description', type: 'textarea' },
  ]),
};

interface TreeNode { id: string; code: string; nameVi: string; nameEn?: string; isActive: boolean; children: TreeNode[] }

function DepartmentTree() {
  const { t, i18n } = useTranslation(['catalog', 'common']);
  const { message } = App.useApp();
  const { has } = usePermission();
  const { data, refetch } = useQuery({ queryKey: ['department-tree'], queryFn: async () => (await http.get<TreeNode[]>('/departments/tree')).data });
  const toData = (nodes: TreeNode[]): TreeProps['treeData'] =>
    nodes.map((n) => ({ key: n.id, title: `${n.code} — ${i18n.language === 'en' && n.nameEn ? n.nameEn : n.nameVi}${n.isActive ? '' : ' (' + t('common:status.inactive') + ')'}`, children: toData(n.children) }));

  const onDrop: TreeProps['onDrop'] = async (info) => {
    const dragKey = String(info.dragNode.key);
    const dropKey = String(info.node.key);
    const pos = info.node.pos.split('-');
    const dropPosition = info.dropPosition - Number(pos[pos.length - 1]);
    // find parent of drop node
    const findParent = (nodes: TreeNode[], id: string, parent: string | null = null): string | null | undefined => {
      for (const n of nodes) {
        if (n.id === id) return parent;
        const r = findParent(n.children, id, n.id);
        if (r !== undefined) return r;
      }
      return undefined;
    };
    const siblingsOf = (nodes: TreeNode[], parent: string | null): TreeNode[] => {
      if (parent === null) return nodes;
      for (const n of nodes) {
        if (n.id === parent) return n.children;
        const r = siblingsOf(n.children, parent);
        if (r.length || n.children.some((c) => c.id === parent)) return r;
      }
      return [];
    };
    let parentId: string | null;
    let index: number;
    if (!info.dropToGap) {
      parentId = dropKey;
      index = 0;
    } else {
      parentId = findParent(data ?? [], dropKey) ?? null;
      const sib = siblingsOf(data ?? [], parentId).filter((s) => s.id !== dragKey);
      const i = sib.findIndex((s) => s.id === dropKey);
      index = dropPosition === -1 ? i : i + 1;
    }
    try {
      await http.put(`/departments/${dragKey}/move`, { parentId, index });
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <Card>
      <Typography.Title level={4}>{t('departments.tree')}</Typography.Title>
      {has('department.edit') && <Typography.Paragraph type="secondary">{t('departments.dragHint')}</Typography.Paragraph>}
      <Tree treeData={toData(data ?? [])} draggable={has('department.edit')} blockNode defaultExpandAll onDrop={onDrop} showLine key={data?.length} />
    </Card>
  );
}

export function DepartmentsPage() {
  const { t } = useTranslation('catalog');
  const [mode, setMode] = useState<'list' | 'tree'>('tree');
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Segmented
        value={mode}
        onChange={(v) => setMode(v as 'list' | 'tree')}
        options={[{ value: 'tree', icon: <ApartmentOutlined />, label: t('departments.tree') }, { value: 'list', icon: <UnorderedListOutlined />, label: t('departments.list') }]}
      />
      {mode === 'tree' ? <DepartmentTree /> : <CatalogPage config={departmentConfig} />}
    </Space>
  );
}

// ------------------------------------------------------------------ countries / provinces / wards

export function CountriesPage() {
  return (
    <CatalogPage
      config={{
        resource: 'countries', perm: 'common_catalog', title: 'nav.countries',
        columns: std.columns([{ key: 'iso2', label: 'catalog:f.iso2', width: 90 }]),
        fields: std.fields([{ name: 'iso2', label: 'catalog:f.iso2', type: 'text', span: 8, schema: z.string().max(2).optional().nullable() }]),
      }}
    />
  );
}

export function ProvincesPage() {
  return (
    <CatalogPage
      config={{
        resource: 'provinces', perm: 'common_catalog', title: 'nav.provinces', defaultSort: 'sortOrder',
        columns: std.columns([{ key: 'kind', label: 'catalog:f.kind', width: 120 }, { key: 'countryNameVi', label: 'catalog:f.country', width: 140, defaultHidden: true }]),
        fields: std.fields([
          { name: 'countryId', label: 'catalog:f.country', type: 'lookup', lookup: 'countries', required: true },
          { name: 'kind', label: 'catalog:f.kind', type: 'text' },
        ]),
        filters: [{ name: 'countryId', label: 'catalog:f.country', type: 'lookup', lookup: 'countries' }],
      }}
    />
  );
}

export function WardsPage() {
  return (
    <CatalogPage
      config={{
        resource: 'wards', perm: 'common_catalog', title: 'nav.wards',
        columns: std.columns([{ key: 'kind', label: 'catalog:f.kind', width: 110 }, { key: 'provinceNameVi', label: 'catalog:f.province', width: 200 }]),
        fields: std.fields([
          { name: 'provinceId', label: 'catalog:f.province', type: 'lookup', lookup: 'provinces', required: true },
          { name: 'kind', label: 'catalog:f.kind', type: 'text' },
        ]),
        filters: [{ name: 'provinceId', label: 'catalog:f.province', type: 'lookup', lookup: 'provinces' }],
      }}
    />
  );
}

// ------------------------------------------------------------------ units of measure + converter

function UnitConverter() {
  const { t } = useTranslation('catalog');
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const [from, setFrom] = useState<string>();
  const [to, setTo] = useState<string>();
  const [value, setValue] = useState<number>(1);
  const [result, setResult] = useState<number | null>(null);
  const convert = async () => {
    if (!from || !to) return;
    try {
      const r = await http.get<{ result: number }>('/units/convert', { params: { value, from, to } });
      setResult(r.data.result);
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  return (
    <>
      <Button icon={<SwapOutlined />} onClick={() => setOpen(true)}>{t('units.converter')}</Button>
      <Modal open={open} onCancel={() => setOpen(false)} title={t('units.converter')} footer={null}>
        <Space direction="vertical" style={{ width: '100%' }}>
          <Typography.Text>{t('units.value')}</Typography.Text>
          <InputNumber value={value} onChange={(v) => setValue(Number(v ?? 0))} style={{ width: '100%' }} decimalSeparator="," />
          <Typography.Text>{t('units.from')}</Typography.Text>
          <LookupSelect resource="units" value={from} onChange={setFrom} />
          <Typography.Text>{t('units.to')}</Typography.Text>
          <LookupSelect resource="units" value={to} onChange={setTo} />
          <Button type="primary" onClick={() => void convert()} block>{t('units.convert')}</Button>
          {result !== null && <Typography.Title level={4}>{t('units.result')}: {fmt.number(result, 6).replace(/,?0+$/, '')}</Typography.Title>}
        </Space>
      </Modal>
    </>
  );
}

export function UnitsPage() {
  const enumLabel = useEnumLabel();
  return (
    <CatalogPage
      config={{
        resource: 'units', perm: 'common_catalog', title: 'nav.units',
        columns: std.columns([
          { key: 'symbol', label: 'catalog:f.symbol', width: 90 },
          { key: 'type', label: 'catalog:f.uomType', width: 140, render: (v) => enumLabel('UomType', v) },
          { key: 'factorToBase', label: 'catalog:f.factorToBase', width: 160, align: 'right', render: (v: number) => fmt.number(v, 6).replace(/,?0+$/, '') },
        ]),
        fields: std.fields([
          { name: 'type', label: 'catalog:f.uomType', type: 'enum', enumName: 'UomType', enumValues: [...ENUMS.UomType], required: true, span: 8 },
          { name: 'factorToBase', label: 'catalog:f.factorToBase', type: 'number', required: true, min: 0.000001, precision: 6, span: 8 },
          { name: 'symbol', label: 'catalog:f.symbol', type: 'text', span: 8 },
        ]),
        defaults: { type: 'Mass', factorToBase: 1 },
        filters: [{ name: 'type', label: 'catalog:f.uomType', type: 'enum', enumName: 'UomType', enumValues: [...ENUMS.UomType] }],
        toolbar: () => <UnitConverter />,
      }}
    />
  );
}
