import { useEffect, useState } from 'react';
import { App, Button, Checkbox, InputNumber, Modal, Space, Table, Tag, Tooltip } from 'antd';
import { DeleteOutlined, FundViewOutlined, PlusOutlined, UnorderedListOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { CatalogPage, std } from '@/shared/components/catalog/CatalogPage';
import { EnumSelect, LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { FileField } from '@/shared/components/FileField';
import { ENUMS } from '@/shared/utils/enums';
import { fmt } from '@/shared/utils/format';
import { DishQuickView } from '@/features/recipes/RecipeEditorPage';

type Any = { id: string; [k: string]: any };

// ------------------------------------------------------------------ dishes

export function DishesPage() {
  const { t } = useTranslation(['recipe', 'catalog', 'common']);
  const enumLabel = useEnumLabel();
  const navigate = useNavigate();
  const [quick, setQuick] = useState<string | null>(null);
  return (
    <>
      <CatalogPage<Any>
        config={{
          resource: 'dishes', perm: 'dish', title: 'nav.dishes',
          columns: std.columns([
            { key: 'dishType', label: 'catalog:f.dishType', width: 140, render: (v) => enumLabel('DishType', v) },
            { key: 'cookingMethod', label: 'catalog:f.cookingMethod', width: 140, render: (v) => enumLabel('CookingMethod', v) },
            { key: 'servingWeightG', label: 'catalog:f.servingWeight', width: 140, align: 'right', render: (v) => fmt.number(v, 0) },
            { key: 'currentRecipeVersion', label: 'recipe:version', width: 110, render: (v) => (v ? <Tag color="green">v{v}</Tag> : <Tag color="orange">{t('recipe:noRecipe')}</Tag>) },
            { key: 'allergens', label: 'catalog:f.allergens', width: 140 },
            { key: 'tags', label: 'catalog:f.tags', width: 160, defaultHidden: true },
          ]),
          fields: std.fields([
            { name: 'dishType', label: 'catalog:f.dishType', type: 'enum', enumName: 'DishType', enumValues: [...ENUMS.DishType], required: true, span: 8 },
            { name: 'cookingMethod', label: 'catalog:f.cookingMethod', type: 'enum', enumName: 'CookingMethod', enumValues: [...ENUMS.CookingMethod], required: true, span: 8 },
            { name: 'servingWeightG', label: 'catalog:f.servingWeight', type: 'number', min: 0, max: 5000, span: 8 },
            { name: 'tags', label: 'catalog:f.tags', type: 'text' },
            { name: 'allergens', label: 'catalog:f.allergens', type: 'text' },
            { name: 'imageFileId', label: 'catalog:f.image', type: 'custom', span: 24, render: ({ value, onChange }) => <FileField value={value as string} onChange={onChange} ownerType="Dish" accept=".png,.jpg,.jpeg,.webp" /> },
            { name: 'description', label: 'catalog:f.description', type: 'textarea' },
          ]),
          defaults: { dishType: 'MainProtein', cookingMethod: 'Boil' },
          filters: [
            { name: 'dishType', label: 'catalog:f.dishType', type: 'enum', enumName: 'DishType', enumValues: [...ENUMS.DishType] },
            { name: 'cookingMethod', label: 'catalog:f.cookingMethod', type: 'enum', enumName: 'CookingMethod', enumValues: [...ENUMS.CookingMethod] },
          ],
          rowActions: (r) => (
            <>
              <Tooltip title={t('recipe:quick')}><Button type="text" icon={<FundViewOutlined />} onClick={() => setQuick(r.id)} data-testid="dish-quick" /></Tooltip>
              <Tooltip title={t('recipe:openRecipe')}><Button type="text" icon={<UnorderedListOutlined />} onClick={() => navigate(r.currentRecipeId ? `/recipes/${r.currentRecipeId}` : `/recipes/new?dishId=${r.id}`)} /></Tooltip>
            </>
          ),
        }}
      />
      {quick && <DishQuickView dishId={quick} onClose={() => setQuick(null)} />}
    </>
  );
}

// ------------------------------------------------------------------ recommendations

interface Target { nutrientId: string; nutrientCode?: string; minPerMeal?: number | null; maxPerMeal?: number | null; level: string }

function TargetsEditor({ value, onChange }: { value?: Target[]; onChange?: (v: Target[]) => void }) {
  const { t } = useTranslation(['catalog', 'common']);
  const rows = value ?? [];
  const set = (i: number, patch: Partial<Target>) => onChange?.(rows.map((r, j) => (j === i ? { ...r, ...patch } : r)));
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Table<Target> size="small" rowKey={(r, i) => r.nutrientId || String(i)} dataSource={rows} pagination={false} scroll={{ x: 600 }}
        columns={[
          { title: t('f.nutrient'), key: 'n', width: 240, render: (_, r, i) => <LookupSelect resource="nutrients" value={r.nutrientId} onChange={(v) => set(i, { nutrientId: v })} /> },
          { title: t('f.minPerMeal'), key: 'min', render: (_, r, i) => <InputNumber min={0} value={r.minPerMeal ?? undefined} onChange={(v) => set(i, { minPerMeal: v === null ? null : Number(v) })} decimalSeparator="," /> },
          { title: t('f.maxPerMeal'), key: 'max', render: (_, r, i) => <InputNumber min={0} value={r.maxPerMeal ?? undefined} onChange={(v) => set(i, { maxPerMeal: v === null ? null : Number(v) })} decimalSeparator="," /> },
          { title: t('f.level'), key: 'l', width: 160, render: (_, r, i) => <EnumSelect enumName="WarningLevel" values={[...ENUMS.WarningLevel]} value={r.level} onChange={(v) => set(i, { level: v })} /> },
          { title: '', key: 'x', width: 44, render: (_, _r, i) => <Button type="text" danger icon={<DeleteOutlined />} onClick={() => onChange?.(rows.filter((_, j) => j !== i))} /> },
        ]} />
      <Button size="small" icon={<PlusOutlined />} onClick={() => onChange?.([...rows, { nutrientId: '', level: 'Warning' }])}>{t('common:actions.add')}</Button>
    </Space>
  );
}

export function RecommendationsPage() {
  const pct = (a: string, b: string) => (_: unknown, r: Any) => `${fmt.number(r[a], 0)}–${fmt.number(r[b], 0)}%`;
  return (
    <CatalogPage<Any>
      config={{
        resource: 'recommendations', perm: 'recommendation', title: 'nav.recommendations', formWidth: 960,
        columns: std.columns([
          { key: 'subjectGroupNameVi', label: 'catalog:f.subject', width: 220 },
          { key: 'energyPerDayKcal', label: 'catalog:f.energyPerDay', width: 160, align: 'right', render: (v) => fmt.number(v, 0) },
          { key: 'mealSharePercent', label: 'catalog:f.mealShare', width: 150, align: 'right', render: (v) => fmt.percent(v, 0) },
          { key: 'mealEnergyKcal', label: 'nutrition:target', width: 130, align: 'right', render: (v) => fmt.number(v, 0) },
          { key: 'p', label: 'catalog:f.proteinPct', width: 130, render: pct('proteinPctMin', 'proteinPctMax') },
          { key: 'l', label: 'catalog:f.lipidPct', width: 130, render: pct('lipidPctMin', 'lipidPctMax') },
          { key: 'g', label: 'catalog:f.glucidPct', width: 130, render: pct('glucidPctMin', 'glucidPctMax') },
          { key: 'budgetPerServing', label: 'catalog:f.budget', width: 150, align: 'right', render: (v) => fmt.money(v) },
          { key: 'reference', label: 'catalog:f.reference', width: 240, defaultHidden: true },
        ]),
        fields: std.fields([
          { name: 'subjectGroupId', label: 'catalog:f.subject', type: 'lookup', lookup: 'subject-groups' },
          { name: 'reference', label: 'catalog:f.reference', type: 'text', span: 8 },
          { name: 'referenceYear', label: 'catalog:f.referenceYear', type: 'number', min: 1990, max: 2100, span: 4 },
          { name: 'energyPerDayKcal', label: 'catalog:f.energyPerDay', type: 'number', required: true, min: 500, max: 6000, span: 8 },
          { name: 'mealSharePercent', label: 'catalog:f.mealShare', type: 'percent', required: true, min: 5, max: 100, span: 8 },
          { name: 'budgetPerServing', label: 'catalog:f.budget', type: 'money', min: 0, span: 8 },
          { name: 'proteinPctMin', label: 'catalog:f.proteinPct', type: 'percent', addonAfter: '% min', span: 6 },
          { name: 'proteinPctMax', label: 'catalog:f.proteinPct', type: 'percent', addonAfter: '% max', span: 6 },
          { name: 'lipidPctMin', label: 'catalog:f.lipidPct', type: 'percent', addonAfter: '% min', span: 6 },
          { name: 'lipidPctMax', label: 'catalog:f.lipidPct', type: 'percent', addonAfter: '% max', span: 6 },
          { name: 'glucidPctMin', label: 'catalog:f.glucidPct', type: 'percent', addonAfter: '% min', span: 6 },
          { name: 'glucidPctMax', label: 'catalog:f.glucidPct', type: 'percent', addonAfter: '% max', span: 6 },
          { name: 'animalProteinPctMin', label: 'catalog:f.animalProteinPct', type: 'percent', addonAfter: '% min', span: 6 },
          { name: 'animalProteinPctMax', label: 'catalog:f.animalProteinPct', type: 'percent', addonAfter: '% max', span: 6 },
          { name: 'nutrients', label: 'catalog:tabs.targets', type: 'custom', span: 24, render: ({ value, onChange }) => <TargetsEditor value={value as Target[]} onChange={onChange} /> },
          { name: 'note', label: 'catalog:f.note', type: 'textarea' },
        ]),
        defaults: { mealSharePercent: 35, proteinPctMin: 13, proteinPctMax: 20, lipidPctMin: 20, lipidPctMax: 25, glucidPctMin: 55, glucidPctMax: 65, animalProteinPctMin: 30, animalProteinPctMax: 50, nutrients: [] },
        filters: [{ name: 'subjectGroupId', label: 'catalog:f.subject', type: 'lookup', lookup: 'subject-groups' }],
        toBody: (v) => ({ ...v, nutrients: ((v.nutrients as Target[]) ?? []).filter((n) => n.nutrientId) }),
      }}
    />
  );
}

// ------------------------------------------------------------------ report groups

function AssignReports({ group, onDone }: { group: Any; onDone: () => void }) {
  const { t, i18n } = useTranslation(['recipe', 'common']);
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const [codes, setCodes] = useState<string[]>(group.reportCodes ?? []);
  const { data } = useQuery({ queryKey: ['report-defs'], enabled: open, queryFn: async () => (await http.get<Any[]>('/report-groups/definitions')).data });
  useEffect(() => setCodes(group.reportCodes ?? []), [group]);
  return (
    <>
      <Button type="text" size="small" onClick={() => setOpen(true)}>{t('recipe:assignReports')}</Button>
      <Modal open={open} title={t('recipe:assignReports')} onCancel={() => setOpen(false)}
        onOk={async () => { try { await http.put(`/report-groups/${group.id}/reports`, { reportCodes: codes }); setOpen(false); onDone(); } catch (e) { message.error(problemMessage(e)); } }}>
        <Checkbox.Group value={codes} onChange={(v) => setCodes(v as string[])} style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {data?.map((d) => <Checkbox key={d.code} value={d.code}>{d.code} — {i18n.language === 'en' ? d.nameEn : d.nameVi} {d.group && d.reportGroupId !== group.id && <Tag>{d.group}</Tag>}</Checkbox>)}
        </Checkbox.Group>
      </Modal>
    </>
  );
}

export function ReportGroupsPage() {
  const { has } = usePermission();
  return (
    <CatalogPage<Any>
      config={{
        resource: 'report-groups', perm: 'report_group', title: 'nav.reportGroups',
        columns: std.columns([
          { key: 'reportCodes', label: 'catalog:tabs.reports', width: 360, render: (v: string[]) => v?.map((c) => <Tag key={c}>{c}</Tag>) },
          { key: 'requiredPermission', label: 'catalog:f.requiredPermission', width: 180 },
        ]),
        fields: std.fields([
          { name: 'requiredPermission', label: 'catalog:f.requiredPermission', type: 'text' },
          { name: 'description', label: 'catalog:f.description', type: 'textarea' },
        ]),
        rowActions: (r, reload) => has('report_group.edit') && <AssignReports group={r} onDone={reload} />,
      }}
    />
  );
}


