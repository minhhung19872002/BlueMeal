import { useMemo, useState } from 'react';
import { App, Button, Card, Checkbox, InputNumber, Segmented, Select, Space, Table, Tag, Tooltip, Tree, Typography } from 'antd';
import { CloudSyncOutlined, LockOutlined, PlusOutlined, DeleteOutlined, UnlockOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { CatalogPage, std } from '@/shared/components/catalog/CatalogPage';
import { EnumSelect, LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { SubTable } from '@/shared/components/SubTable';
import { ENUMS } from '@/shared/utils/enums';
import { fmt, parseVnNumber } from '@/shared/utils/format';

type Any = { id: string; [k: string]: any };
interface NutrientDef { id: string; code: string; nameVi: string; nameEn?: string; unit: string; group: string; isCore: boolean; decimals: number }

function useNutrients() {
  return useQuery({
    queryKey: ['nutrients-all'],
    queryFn: async () => (await http.get<PagedResult<NutrientDef>>('/nutrients', { params: { pageSize: 200, isActive: true } })).data.items,
    staleTime: 300_000,
  });
}

// ------------------------------------------------------------------ warehouses

export function WarehousesPage() {
  const enumLabel = useEnumLabel();
  return (
    <CatalogPage<Any>
      config={{
        resource: 'warehouses', perm: 'warehouse', title: 'nav.warehouses',
        columns: std.columns([
          { key: 'type', label: 'catalog:f.warehouseType', width: 140, render: (v) => enumLabel('WarehouseType', v) },
          { key: 'location', label: 'catalog:f.location', width: 180 },
          { key: 'keeperName', label: 'catalog:f.keeper', width: 180 },
          { key: 'temp', label: 'catalog:f.tempMin', width: 130, render: (_, r) => (r.tempMin !== null || r.tempMax !== null ? `${fmt.number(r.tempMin, 0)} … ${fmt.number(r.tempMax, 0)} °C` : '') },
        ]),
        fields: std.fields([
          { name: 'type', label: 'catalog:f.warehouseType', type: 'enum', enumName: 'WarehouseType', enumValues: [...ENUMS.WarehouseType], required: true },
          { name: 'keeperEmployeeId', label: 'catalog:f.keeper', type: 'lookup', lookup: 'employees' },
          { name: 'location', label: 'catalog:f.location', type: 'text', span: 24 },
          { name: 'tempMin', label: 'catalog:f.tempMin', type: 'number', min: -60, max: 60, span: 6 },
          { name: 'tempMax', label: 'catalog:f.tempMax', type: 'number', min: -60, max: 60, span: 6 },
          { name: 'note', label: 'catalog:f.note', type: 'textarea' },
        ]),
        defaults: { type: 'Dry' },
      }}
    />
  );
}

// ------------------------------------------------------------------ food groups (list + tree)

interface GroupNode { id: string; code: string; nameVi: string; nameEn?: string; isAnimalSource: boolean; foodCount: number; children: GroupNode[] }

function FoodGroupTree() {
  const { t, i18n } = useTranslation('catalog');
  const { data } = useQuery({ queryKey: ['food-group-tree'], queryFn: async () => (await http.get<GroupNode[]>('/food-groups/tree')).data });
  const toTree = (nodes: GroupNode[]): any[] => nodes.map((n) => ({
    key: n.id,
    title: <Space>{n.code} — {i18n.language === 'en' && n.nameEn ? n.nameEn : n.nameVi}{n.isAnimalSource && <Tag color="volcano">{t('f.isAnimalSource')}</Tag>}<Tag>{t('foods.foodCount')}: {n.foodCount}</Tag></Space>,
    children: toTree(n.children),
  }));
  return <Card title={t('foods.tree')}><Tree treeData={toTree(data ?? [])} defaultExpandAll blockNode showLine /></Card>;
}

export function FoodGroupsPage() {
  const { t } = useTranslation('catalog');
  const [mode, setMode] = useState<'list' | 'tree'>('list');
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Segmented value={mode} onChange={(v) => setMode(v as 'list' | 'tree')} options={[{ value: 'list', label: t('departments.list') }, { value: 'tree', label: t('foods.tree') }]} />
      {mode === 'tree' ? <FoodGroupTree /> : (
        <CatalogPage<Any>
          config={{
            resource: 'food-groups', perm: 'food_group', title: 'nav.foodGroups',
            columns: std.columns([
              { key: 'parentNameVi', label: 'catalog:f.parent', width: 200 },
              { key: 'isAnimalSource', label: 'catalog:f.isAnimalSource', width: 140, render: (v) => (v ? <Tag color="volcano">✓</Tag> : null) },
            ]),
            fields: std.fields([
              { name: 'parentId', label: 'catalog:f.parent', type: 'lookup', lookup: 'food-groups' },
              { name: 'isAnimalSource', label: 'catalog:f.isAnimalSource', type: 'switch', tooltip: 'catalog:f.animalProteinPct' },
            ]),
          }}
        />
      )}
    </Space>
  );
}

// ------------------------------------------------------------------ nutrients

export function NutrientsPage() {
  const enumLabel = useEnumLabel();
  return (
    <CatalogPage<Any>
      config={{
        resource: 'nutrients', perm: 'nutrient', title: 'nav.nutrients', defaultSort: 'sortOrder',
        columns: std.columns([
          { key: 'unit', label: 'catalog:f.unit', width: 80 },
          { key: 'group', label: 'catalog:f.nutrientGroup', width: 140, render: (v) => enumLabel('NutrientGroup', v) },
          { key: 'isCore', label: 'catalog:f.isCore', width: 120, render: (v) => (v ? <Tag color="green">✓</Tag> : null) },
          { key: 'decimals', label: 'catalog:f.decimals', width: 110, align: 'right' },
        ]),
        fields: std.fields([
          { name: 'unit', label: 'catalog:f.unit', type: 'text', required: true, span: 6 },
          { name: 'group', label: 'catalog:f.nutrientGroup', type: 'enum', enumName: 'NutrientGroup', enumValues: [...ENUMS.NutrientGroup], required: true, span: 6 },
          { name: 'decimals', label: 'catalog:f.decimals', type: 'number', min: 0, max: 4, span: 6 },
          { name: 'isCore', label: 'catalog:f.isCore', type: 'switch', span: 6 },
        ]),
        defaults: { unit: 'g', group: 'Macro', decimals: 1 },
        filters: [{ name: 'group', label: 'catalog:f.nutrientGroup', type: 'enum', enumName: 'NutrientGroup', enumValues: [...ENUMS.NutrientGroup] }],
      }}
    />
  );
}

// ------------------------------------------------------------------ foods: list + quick-edit grid

/** Per-nutrient editor used inside the food form (value per 100 g edible portion). */
function NutrientEditor({ value, onChange }: { value?: Record<string, number | null>; onChange?: (v: Record<string, number | null>) => void }) {
  const { i18n, t } = useTranslation('catalog');
  const { data } = useNutrients();
  const v = value ?? {};
  return (
    <Table<NutrientDef>
      size="small" rowKey="code" pagination={false} dataSource={data} scroll={{ y: 360 }}
      columns={[
        { title: t('f.nutrient'), key: 'n', render: (_, n) => <span>{i18n.language === 'en' && n.nameEn ? n.nameEn : n.nameVi} {n.isCore && <Tag color="green">★</Tag>}</span> },
        { title: t('f.unit'), dataIndex: 'unit', width: 70 },
        {
          title: t('f.value'), key: 'v', width: 160,
          render: (_, n) => (
            <InputNumber
              size="small" min={0} style={{ width: '100%' }} decimalSeparator="," precision={Math.max(n.decimals, 1)}
              value={v[n.code] ?? undefined}
              onChange={(x) => onChange?.({ ...v, [n.code]: x === null ? null : Number(x) })}
              data-testid={'nutrient-' + n.code}
            />
          ),
        },
      ]}
    />
  );
}

function FoodGrid() {
  const { t, i18n } = useTranslation(['catalog', 'common']);
  const { message } = App.useApp();
  const { has } = usePermission();
  const qc = useQueryClient();
  const nutrients = useNutrients();
  const [codes, setCodes] = useState<string[]>(['ENERGY', 'PROTEIN', 'LIPID', 'GLUCID', 'FIBER', 'CA', 'FE', 'NA', 'VIT_C']);
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const [groupId, setGroupId] = useState<string>();
  const { data, isFetching } = useQuery({
    queryKey: ['food-grid', page, q, codes.join(','), groupId],
    queryFn: async () => (await http.get<PagedResult<Any>>('/foods/grid', { params: { page, pageSize: 50, q: q || undefined, nutrients: codes.join(','), foodGroupId: groupId } })).data,
    placeholderData: keepPreviousData,
  });
  const canEdit = has('food.edit');
  const canUnlock = has('food.unlock');
  const save = async (foodId: string, code: string, raw: string) => {
    const value = raw.trim() === '' ? null : parseVnNumber(raw);
    try {
      await http.put(`/foods/${foodId}/nutrients`, { [code]: value });
      void qc.invalidateQueries({ queryKey: ['food-grid'] });
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const defs = useMemo(() => new Map((nutrients.data ?? []).map((n) => [n.code, n])), [nutrients.data]);
  return (
    <Card title={t('foods.grid')} extra={<Typography.Text type="secondary">{t('foods.editHint')}</Typography.Text>}>
      <div className="bm-toolbar">
        <Select mode="multiple" value={codes} onChange={setCodes} style={{ minWidth: 320, maxWidth: 700 }} maxTagCount="responsive"
          options={nutrients.data?.map((n) => ({ value: n.code, label: `${n.code} (${n.unit})` }))} placeholder={t('foods.nutrients')} />
        <LookupSelect resource="food-groups" placeholder={t('f.foodGroup')} value={groupId} onChange={(v) => { setGroupId(v ?? undefined); setPage(1); }} style={{ width: 240 }} />
        <input className="ant-input" placeholder={t('common:searchPlaceholder')} onKeyDown={(e) => { if (e.key === 'Enter') { setQ((e.target as HTMLInputElement).value); setPage(1); } }} style={{ maxWidth: 260 }} data-testid="grid-search" />
      </div>
      <Table<Any>
        rowKey="id" size="small" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content', y: 560 }} sticky
        pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('common:fields.code'), dataIndex: 'code', fixed: 'left', width: 110 },
          { title: t('common:fields.name'), key: 'name', fixed: 'left', width: 220, render: (_, r) => <Space>{i18n.language === 'en' && r.nameEn ? r.nameEn : r.nameVi}{r.isLocked && <Tooltip title={t('foods.locked')}><LockOutlined /></Tooltip>}</Space> },
          { title: t('f.refuseRate'), dataIndex: 'refuseRate', width: 90, align: 'right', render: (v) => fmt.number(v, 1) },
          ...codes.map((code) => ({
            title: `${code} (${defs.get(code)?.unit ?? ''})`, key: code, width: 110, align: 'right' as const,
            render: (_: unknown, r: Any) => {
              const editable = canEdit && (!r.isLocked || canUnlock);
              const val = r.values?.[code];
              return editable ? (
                <input
                  className="ant-input ant-input-sm bm-num" defaultValue={val === undefined ? '' : fmt.number(val, defs.get(code)?.decimals ?? 1)} style={{ width: 96 }}
                  onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
                  onBlur={(e) => { const nv = e.target.value; const old = val === undefined ? '' : fmt.number(val, defs.get(code)?.decimals ?? 1); if (nv !== old) void save(r.id, code, nv); }}
                  aria-label={code}
                />
              ) : <span className="bm-num">{fmt.number(val, defs.get(code)?.decimals ?? 1)}</span>;
            },
          })),
        ]}
      />
    </Card>
  );
}

function FoodList() {
  const { t } = useTranslation(['catalog', 'common']);
  const { message } = App.useApp();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  return (
    <CatalogPage<Any>
      config={{
        resource: 'foods', perm: 'food', title: 'nav.foods', formWidth: 900,
        columns: std.columns([
          { key: 'foodGroupNameVi', label: 'catalog:f.foodGroup', width: 200 },
          { key: 'refuseRate', label: 'catalog:f.refuseRate', width: 120, align: 'right', render: (v) => fmt.number(v, 1) },
          { key: 'energy', label: 'catalog:sub.energy', width: 120, align: 'right', render: (_, r) => fmt.number(r.nutrients?.ENERGY, 0) },
          { key: 'protein', label: 'catalog:f.proteinPct', width: 110, align: 'right', render: (_, r) => fmt.number(r.nutrients?.PROTEIN, 1), defaultHidden: true },
          { key: 'isAnimalSource', label: 'catalog:f.isAnimalSource', width: 110, render: (v) => (v ? <Tag color="volcano">✓</Tag> : null) },
          {
            key: 'source', label: 'catalog:f.source', width: 200,
            render: (v, r) => (
              <Space size={4} wrap>
                <Tag color={v === 'VddApi' ? 'blue' : undefined}>{enumLabel('DataSource', v)}</Tag>
                {r.sourceVersion === 'SAMPLE' && <Tag color="gold">{t('common:sampleData')}</Tag>}
                {r.isLocked && <Tooltip title={t('foods.locked')}><LockOutlined /></Tooltip>}
                {r.locallyModified && <Tag color="orange">{t('foods.modified')}</Tag>}
              </Space>
            ),
          },
          { key: 'lastSyncedAt', label: 'catalog:f.lastSyncedAt', width: 150, render: (v) => fmt.dateTime(v), defaultHidden: true },
        ]),
        fields: std.fields([
          { name: 'foodGroupId', label: 'catalog:f.foodGroup', type: 'lookup', lookup: 'food-groups', required: true },
          { name: 'refuseRate', label: 'catalog:f.refuseRate', type: 'percent', min: 0, max: 99.9, precision: 1, span: 6 },
          { name: 'isAnimalSourceOverride', label: 'catalog:f.animalOverride', type: 'custom', span: 6, render: ({ value, onChange }) => (
            <Select allowClear value={value as boolean | undefined} onChange={onChange} options={[{ value: true, label: t('common:actions.yes') }, { value: false, label: t('common:actions.no') }]} />
          ) },
          { name: 'note', label: 'catalog:f.note', type: 'textarea' },
          { name: 'nutrients', label: 'catalog:tabs.nutrients', type: 'custom', span: 24, render: ({ value, onChange }) => <NutrientEditor value={value as never} onChange={onChange} /> },
        ]),
        filters: [
          { name: 'foodGroupId', label: 'catalog:f.foodGroup', type: 'lookup', lookup: 'food-groups' },
          { name: 'source', label: 'catalog:f.source', type: 'enum', enumName: 'DataSource', enumValues: [...ENUMS.DataSource] },
        ],
        toolbar: () => has('integration.sync') && (
          <Button icon={<CloudSyncOutlined />} onClick={async () => { try { await http.post('/integration/sync'); message.success(t('common:saved')); } catch (e) { message.error(problemMessage(e)); } }}>
            {t('foods.sync')}
          </Button>
        ),
        rowActions: (r, reload) => has('food.unlock') && r.source === 'VddApi' && (
          <Tooltip title={r.isLocked ? t('foods.unlock') : t('foods.lock')}>
            <Button type="text" icon={r.isLocked ? <UnlockOutlined /> : <LockOutlined />} onClick={async () => { await http.post(`/foods/${r.id}/data-lock`, null, { params: { locked: !r.isLocked } }); reload(); }} />
          </Tooltip>
        ),
      }}
    />
  );
}

export function FoodsPage() {
  const { t } = useTranslation('catalog');
  const [mode, setMode] = useState<'list' | 'grid'>('list');
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Segmented value={mode} onChange={(v) => setMode(v as 'list' | 'grid')} options={[{ value: 'list', label: t('foods.list') }, { value: 'grid', label: t('foods.grid') }]} />
      {mode === 'grid' ? <FoodGrid /> : <FoodList />}
    </Space>
  );
}

// ------------------------------------------------------------------ ingredients (+ price history, stock)

function IngredientStock({ id }: { id: string }) {
  const { t } = useTranslation('catalog');
  const { data } = useQuery({ queryKey: ['ingredient-stock', id], queryFn: async () => (await http.get<{ balances: Any[]; lots: Any[] }>(`/ingredients/${id}/stock`)).data });
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Table size="small" rowKey="warehouseId" dataSource={data?.balances} pagination={false} scroll={{ x: 500 }}
        columns={[{ title: t('sub.warehouse'), dataIndex: 'warehouse' }, { title: t('sub.quantity'), dataIndex: 'quantity', align: 'right', render: (v) => fmt.number(v, 2) },
          { title: t('sub.avgCost'), dataIndex: 'averageCost', align: 'right', render: (v) => fmt.money(v) }, { title: t('sub.stockValue'), dataIndex: 'value', align: 'right', render: (v) => fmt.money(v) }]} />
      <Table size="small" rowKey="id" dataSource={data?.lots} pagination={false} scroll={{ x: 500 }}
        columns={[{ title: t('sub.lot'), dataIndex: 'lotNo' }, { title: t('sub.mfg'), dataIndex: 'mfgDate', render: (v) => fmt.date(v) }, { title: t('sub.exp'), dataIndex: 'expiryDate', render: (v) => fmt.date(v) },
          { title: t('sub.quantity'), dataIndex: 'quantity', align: 'right', render: (v) => fmt.number(v, 2) }, { title: t('sub.unitPrice'), dataIndex: 'unitCost', align: 'right', render: (v) => fmt.money(v) }]} />
    </Space>
  );
}

export function IngredientsPage() {
  const { t } = useTranslation('catalog');
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  return (
    <CatalogPage<Any>
      config={{
        resource: 'ingredients', perm: 'ingredient', title: 'nav.ingredients', formWidth: 900,
        columns: std.columns([
          { key: 'foodNameVi', label: 'catalog:f.food', width: 200, render: (v, r) => (v ? `${r.foodCode} — ${v}` : <Tag color="orange">∅</Tag>) },
          { key: 'purchaseUnitSymbol', label: 'catalog:f.purchaseUnit', width: 100 },
          { key: 'gramsPerUnit', label: 'catalog:f.gramsPerUnit', width: 150, align: 'right', render: (v) => fmt.number(v, 2) },
          { key: 'referencePrice', label: 'catalog:f.referencePrice', width: 160, align: 'right', render: (v) => fmt.money(v) },
          { key: 'stockOnHand', label: 'catalog:tabs.stock', width: 110, align: 'right', render: (v, r) => <span style={{ color: v < r.minStock ? '#c8463d' : undefined }}>{fmt.number(v, 2)}</span> },
          { key: 'minStock', label: 'catalog:f.minStock', width: 110, align: 'right', render: (v) => fmt.number(v, 2), defaultHidden: true },
          { key: 'preferredPartnerNameVi', label: 'catalog:f.preferredPartner', width: 200, defaultHidden: true },
          { key: 'allergens', label: 'catalog:f.allergens', width: 120, defaultHidden: true },
        ]),
        fields: std.fields([
          { name: 'foodId', label: 'catalog:f.food', type: 'lookup', lookup: 'foods', span: 24 },
          { name: 'purchaseUnitId', label: 'catalog:f.purchaseUnit', type: 'lookup', lookup: 'units', span: 8 },
          { name: 'gramsPerUnit', label: 'catalog:f.gramsPerUnit', type: 'number', required: true, min: 0.001, precision: 3, span: 8 },
          { name: 'referencePrice', label: 'catalog:f.referencePrice', type: 'money', min: 0, span: 8 },
          { name: 'defaultWarehouseId', label: 'catalog:f.defaultWarehouse', type: 'lookup', lookup: 'warehouses', span: 8 },
          { name: 'minStock', label: 'catalog:f.minStock', type: 'number', min: 0, precision: 2, span: 8 },
          { name: 'shelfLifeDays', label: 'catalog:f.shelfLifeDays', type: 'number', min: 0, span: 8 },
          { name: 'preferredPartnerId', label: 'catalog:f.preferredPartner', type: 'lookup', lookup: 'partners' },
          { name: 'storageCondition', label: 'catalog:f.storageCondition', type: 'text' },
          { name: 'allergens', label: 'catalog:f.allergens', type: 'text' },
          { name: 'barcode', label: 'catalog:f.barcode', type: 'text' },
          { name: 'note', label: 'catalog:f.note', type: 'textarea' },
        ]),
        defaults: { gramsPerUnit: 1000 },
        filters: [
          { name: 'foodId', label: 'catalog:f.food', type: 'lookup', lookup: 'foods' },
          { name: 'unmapped', label: 'catalog:f.food', type: 'select', options: [{ value: 'true', label: '∅' }] },
        ],
        detailTabs: (r) => [
          {
            key: 'prices', label: t('tabs.priceHistory'),
            children: (
              <SubTable<Any>
                url={`/ingredients/${r.id}/prices`} canEdit={has('ingredient.price')} allowUpdate={false} allowDelete={false} title={t('sub.addPrice')}
                columns={[
                  { title: t('sub.effectiveDate'), dataIndex: 'effectiveDate', render: (v) => fmt.date(v) },
                  { title: t('sub.unitPrice'), dataIndex: 'unitPrice', align: 'right', render: (v) => fmt.money(v) },
                  { title: t('sub.source'), dataIndex: 'source', render: (v) => enumLabel('PriceSource', v) }, { title: t('sub.partner'), dataIndex: 'partner' },
                ]}
                fields={[
                  { name: 'effectiveDate', label: 'catalog:sub.effectiveDate', type: 'date', required: true }, { name: 'unitPrice', label: 'catalog:sub.unitPrice', type: 'money', required: true, min: 0 },
                  { name: 'partnerId', label: 'catalog:sub.partner', type: 'lookup', lookup: 'partners', span: 24 },
                ]}
              />
            ),
          },
          { key: 'stock', label: t('tabs.stock'), children: <IngredientStock id={r.id} /> },
        ],
      }}
    />
  );
}

// ------------------------------------------------------------------ food safety specs

export function FoodSafetySpecsPage() {
  return (
    <CatalogPage<Any>
      config={{
        resource: 'food-safety-specs', perm: 'food_safety', title: 'nav.foodSafety', hasActive: false, hasCode: false, formWidth: 860,
        columns: [
          { key: 'foodNameVi', label: 'catalog:f.food', width: 200, fixed: 'left', mobile: true },
          { key: 'ingredientNameVi', label: 'catalog:sub.ingredient', width: 200 },
          { key: 'sensoryColor', label: 'inspection:criteria.color', width: 160 },
          { key: 'sensorySmell', label: 'inspection:criteria.smell', width: 160 },
          { key: 'storage', label: 'catalog:f.storageCondition', width: 140, render: (_, r) => (r.storageTempMin !== null || r.storageTempMax !== null ? `${fmt.number(r.storageTempMin, 0)} … ${fmt.number(r.storageTempMax, 0)} °C` : '') },
          { key: 'maxShelfDaysAfterReceipt', label: 'catalog:f.shelfLifeDays', width: 120, align: 'right' },
          { key: 'allergens', label: 'catalog:f.allergens', width: 140 },
        ],
        fields: [
          { name: 'foodId', label: 'catalog:f.food', type: 'lookup', lookup: 'foods' },
          { name: 'ingredientId', label: 'catalog:sub.ingredient', type: 'lookup', lookup: 'ingredients' },
          { name: 'sensoryColor', label: 'inspection:criteria.color', type: 'text', span: 8 },
          { name: 'sensorySmell', label: 'inspection:criteria.smell', type: 'text', span: 8 },
          { name: 'sensoryState', label: 'inspection:criteria.state', type: 'text', span: 8 },
          { name: 'storageTempMin', label: 'catalog:f.tempMin', type: 'number', min: -60, max: 60, span: 8 },
          { name: 'storageTempMax', label: 'catalog:f.tempMax', type: 'number', min: -60, max: 60, span: 8 },
          { name: 'maxShelfDaysAfterReceipt', label: 'catalog:f.shelfLifeDays', type: 'number', min: 0, span: 8 },
          { name: 'requiredDocuments', label: 'inspection:criteria.documents', type: 'text', span: 24 },
          { name: 'allergens', label: 'catalog:f.allergens', type: 'text', span: 24 },
          { name: 'note', label: 'catalog:f.note', type: 'textarea' },
        ],
      }}
    />
  );
}

// ------------------------------------------------------------------ meal shifts / meal structures

export function MealShiftsPage() {
  return (
    <CatalogPage<Any>
      config={{
        resource: 'meal-shifts', perm: 'meal_shift', title: 'nav.mealShifts',
        columns: std.columns([{ key: 'serve', label: 'catalog:f.serveFrom', width: 160, render: (_, r) => `${fmt.time(r.serveFrom)} – ${fmt.time(r.serveTo)}` }]),
        fields: std.fields([{ name: 'serveFrom', label: 'catalog:f.serveFrom', type: 'time' }, { name: 'serveTo', label: 'catalog:f.serveTo', type: 'time' }]),
      }}
    />
  );
}

interface Slot { dishType: string; quantity: number; isRequired: boolean; sortOrder?: number }

function SlotsEditor({ value, onChange }: { value?: Slot[]; onChange?: (v: Slot[]) => void }) {
  const { t } = useTranslation('catalog');
  const slots = value ?? [];
  const set = (i: number, patch: Partial<Slot>) => onChange?.(slots.map((s, j) => (j === i ? { ...s, ...patch } : s)));
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      {slots.map((s, i) => (
        <Space key={i} wrap>
          <EnumSelect enumName="DishType" values={[...ENUMS.DishType]} value={s.dishType} onChange={(v) => set(i, { dishType: v })} style={{ width: 200 }} />
          <InputNumber min={1} max={5} value={s.quantity} onChange={(v) => set(i, { quantity: Number(v ?? 1) })} addonBefore={t('sub.slotQty')} style={{ width: 150 }} />
          <Checkbox checked={s.isRequired} onChange={(e) => set(i, { isRequired: e.target.checked })}>{t('sub.slotRequired')}</Checkbox>
          <Button type="text" danger icon={<DeleteOutlined />} onClick={() => onChange?.(slots.filter((_, j) => j !== i))} />
        </Space>
      ))}
      <Button icon={<PlusOutlined />} onClick={() => onChange?.([...slots, { dishType: 'MainProtein', quantity: 1, isRequired: true }])}>{t('sub.addSlot')}</Button>
    </Space>
  );
}

export function MealStructuresPage() {
  const enumLabel = useEnumLabel();
  return (
    <CatalogPage<Any>
      config={{
        resource: 'meal-structures', perm: 'meal_structure', title: 'nav.mealStructures',
        columns: std.columns([
          { key: 'slots', label: 'catalog:tabs.slots', width: 360, render: (v: Slot[]) => v?.map((s, i) => <Tag key={i} color={s.isRequired ? 'green' : undefined}>{s.quantity} × {enumLabel('DishType', s.dishType)}</Tag>) },
          { key: 'description', label: 'catalog:f.description', width: 240, defaultHidden: true },
        ]),
        fields: std.fields([
          { name: 'description', label: 'catalog:f.description', type: 'textarea' },
          { name: 'slots', label: 'catalog:tabs.slots', type: 'custom', span: 24, render: ({ value, onChange }) => <SlotsEditor value={value as Slot[]} onChange={onChange} /> },
        ]),
        defaults: { slots: [] },
      }}
    />
  );
}
