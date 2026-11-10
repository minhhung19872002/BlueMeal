import { useEffect, useMemo, useState } from 'react';
import { Alert, App, Button, Col, Drawer, Dropdown, Form, Grid, Input, InputNumber, Row, Select, Space, Table, Tooltip, Typography } from 'antd';
import { CopyOutlined, DeleteOutlined, DiffOutlined, MoreOutlined, PlusOutlined, SaveOutlined, CheckOutlined, CalculatorOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useBlocker, useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel, type LookupItem } from '@/shared/components/catalog/inputs';
import { NutritionPanel, type CostView, type NutritionView, type WarningItem } from '@/shared/components/nutrition/NutritionPanel';
import { useBeforeUnload } from '@/shared/hooks/useTablePrefs';
import { fmt } from '@/shared/utils/format';
import { LineCell, LineRow, Page, Section } from '@/shared/components/PageChrome';
import { statusTone } from './RecipesPage';

const lineSchema = z.object({
  ingredientId: z.string({ message: 'common:validation.requiredShort' }).min(1, 'common:validation.requiredShort'),
  ingredientLabel: z.string().optional(),
  grossG: z.number({ message: 'common:validation.number' }).positive('common:validation.min|{"min":0}'),
  netGOverride: z.number().nonnegative().nullable().optional(),
  prepNote: z.string().max(500).nullable().optional(),
});
const schema = z.object({
  dishId: z.string({ message: 'common:validation.requiredShort' }).min(1, 'common:validation.requiredShort'),
  baseServings: z.number().positive().max(10000),
  cookingLossRate: z.number().min(0).max(90),
  prepTimeMinutes: z.number().int().min(0).max(1440).nullable().optional(),
  instructions: z.string().max(8000).nullable().optional(),
  note: z.string().max(1000).nullable().optional(),
  items: z.array(lineSchema).min(1, 'common:validation.requiredShort'),
});
type FormValues = z.infer<typeof schema>;

interface Computed { nutrition: NutritionView; cost?: CostView | null; warnings: WarningItem[]; scaled?: { ingredientId: string; code: string; name: string; grossG: number; netG: number; purchaseUnits: number }[] | null }
interface RecipeDetail {
  id: string; dishId: string; dish: { code: string; nameVi: string; nameEn?: string; dishType: string; cookingMethod: string }; versionNo: number; isCurrent: boolean; status: string;
  baseServings: number; instructions?: string; prepTimeMinutes?: number; cookingLossRate: number; note?: string; version: number;
  items: { id: string; ingredientId: string; ingredientCode?: string; ingredientName?: string; grossG: number; netGOverride?: number | null; netG: number; refuseRate?: number | null; hasFood: boolean; prepNote?: string }[];
  computed: Computed;
}

function useDebounced<T>(v: T, ms = 400) {
  const [d, setD] = useState(v);
  useEffect(() => { const h = setTimeout(() => setD(v), ms); return () => clearTimeout(h); }, [v, ms]);
  return d;
}

/** Recipe editor with realtime nutrition/cost (SPEC §7.3.7). */
export default function RecipeEditorPage() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const isNew = !id || id === 'new';
  const { t, i18n } = useTranslation('recipe');
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const screens = Grid.useBreakpoint();
  const [servings, setServings] = useState<number | null>(null);
  const [compareOpen, setCompareOpen] = useState(false);
  const [saving, setSaving] = useState(false);

  const { data: detail, refetch } = useQuery({ queryKey: ['recipe', id], enabled: !isNew, queryFn: async () => (await http.get<RecipeDetail>(`/recipes/${id}`)).data });
  const { control, handleSubmit, reset, formState, setValue, getValues } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { dishId: params.get('dishId') ?? '', baseServings: 1, cookingLossRate: 0, items: [] },
    mode: 'onChange',
  });
  const { fields, append, remove } = useFieldArray({ control, name: 'items' });
  const editable = isNew || detail?.status === 'Draft';
  const canEdit = editable && (isNew ? has('recipe.create') : has('recipe.edit'));

  useEffect(() => {
    if (!detail) return;
    reset({
      dishId: detail.dishId, baseServings: detail.baseServings, cookingLossRate: detail.cookingLossRate, prepTimeMinutes: detail.prepTimeMinutes ?? null,
      instructions: detail.instructions ?? '', note: detail.note ?? '',
      items: detail.items.map((i) => ({ ingredientId: i.ingredientId, ingredientLabel: `${i.ingredientCode} — ${i.ingredientName}`, grossG: i.grossG, netGOverride: i.netGOverride ?? null, prepNote: i.prepNote ?? '' })),
    });
  }, [detail, reset]);

  const dirty = formState.isDirty;
  useBeforeUnload(dirty);
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && !saving && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    if (blocker.state === 'blocked') modal.confirm({ title: t('common:confirmLeave'), onOk: () => blocker.proceed(), onCancel: () => blocker.reset() });
  }, [blocker, modal, t]);

  // realtime computation
  const watched = useWatch({ control });
  const debounced = useDebounced(watched);
  const dishQ = useQuery({ queryKey: ['dish-lite', debounced.dishId], enabled: !!debounced.dishId, queryFn: async () => (await http.get<any>(`/dishes/${debounced.dishId}`)).data });
  const computeBody = useMemo(() => ({
    dishId: debounced.dishId || null, cookingMethod: dishQ.data?.cookingMethod, dishType: dishQ.data?.dishType,
    baseServings: Number(debounced.baseServings) || 1, cookingLossRate: Number(debounced.cookingLossRate) || 0, servings,
    items: (debounced.items ?? []).filter((i) => i?.ingredientId && Number(i.grossG) > 0).map((i) => ({ ingredientId: i!.ingredientId, grossG: Number(i!.grossG), netGOverride: i!.netGOverride ?? null })),
  }), [debounced, dishQ.data, servings]);
  const computed = useQuery({
    queryKey: ['recipe-compute', computeBody],
    enabled: computeBody.items.length > 0,
    queryFn: async () => (await http.post<Computed>('/recipes/compute', computeBody)).data,
    placeholderData: (prev) => prev,
  });

  const onSave = handleSubmit(async (v) => {
    setSaving(true);
    try {
      const body = { ...v, items: v.items.map(({ ingredientLabel: _l, ...rest }) => rest), version: detail?.version };
      if (isNew) {
        const r = await http.post<{ id: string }>('/recipes', body);
        message.success(t('common:saved'));
        reset(v);
        navigate(`/recipes/${r.data.id}`, { replace: true });
      } else {
        await http.put(`/recipes/${id}`, body);
        message.success(t('common:saved'));
        reset(v);
        void refetch();
      }
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setSaving(false);
    }
  });

  const run = async (fn: () => Promise<unknown>, ok?: string) => {
    try { await fn(); if (ok) message.success(ok); } catch (e) { message.error(problemMessage(e)); }
  };

  const versions = useQuery({
    queryKey: ['recipe-versions', detail?.dishId],
    enabled: !!detail?.dishId,
    queryFn: async () => (await http.get<PagedResult<any>>('/recipes', { params: { dishId: detail!.dishId, pageSize: 50 } })).data.items,
  });

  const scaled = computed.data?.scaled;

  const tags = detail && (
    <>
      <span className="bm-tag t-neutral">v{detail.versionNo}</span>
      <span className={`bm-tag ${statusTone[detail.status] ?? 't-neutral'}`}>{enumLabel('RecipeStatus', detail.status)}</span>
      {detail.isCurrent && <span className="bm-tag t-info">{t('current')}</span>}
    </>
  );
  const dishName = detail ? (i18n.language === 'en' && detail.dish.nameEn ? detail.dish.nameEn : detail.dish.nameVi) : null;
  const lineOf = (idx: number) => (computed.data?.nutrition as any)?.dishes?.[0]?.lines?.[idx] as { netG: number; missingFood?: boolean } | undefined;

  return (
    <Page
      title={dishName ? `${t('editor')} · ${dishName}` : t('editor')}
      tag={tags}
      actions={(
        <>
          <Button onClick={() => navigate('/recipes')}>{t('common:actions.back')}</Button>
          {!isNew && (
            <Dropdown trigger={['click']} menu={{ items: [
              ...(has('recipe.create') ? [{ key: 'v', icon: <CopyOutlined />, label: t('newVersion'), onClick: () => void run(async () => { const r = await http.post<{ id: string }>(`/recipes/${id}/clone`, {}); navigate(`/recipes/${r.data.id}`); }) }] : []),
              ...(has('recipe.create') ? [{ key: 'c', icon: <CopyOutlined />, label: t('cloneTo'), onClick: () => {
                let target: string | undefined;
                modal.confirm({ title: t('cloneTo'), content: <LookupSelect resource="dishes" onChange={(v) => (target = v ?? undefined)} />, onOk: () => run(async () => { const r = await http.post<{ id: string }>(`/recipes/${id}/clone`, { targetDishId: target }); navigate(`/recipes/${r.data.id}`); }) });
              } }] : []),
              { key: 'cmp', icon: <DiffOutlined />, label: t('compare'), onClick: () => setCompareOpen(true) },
              ...(has('recipe.delete') && detail && !detail.isCurrent ? [{ key: 'd', icon: <DeleteOutlined />, danger: true, label: t('common:actions.delete'), onClick: () => modal.confirm({ title: t('common:confirmDelete'), onOk: () => run(async () => { await http.delete(`/recipes/${id}`); reset(getValues()); navigate('/recipes'); }) }) }] : []),
            ] }}>
              <Button icon={<MoreOutlined />} aria-label={t('common:actions.more')} />
            </Dropdown>
          )}
          {!isNew && detail && !detail.isCurrent && has('recipe.approve') && (
            <Button className="bm-btn-soft" icon={<CheckOutlined />} disabled={dirty} onClick={() => void run(async () => { await http.post(`/recipes/${id}/activate`); await refetch(); void versions.refetch(); }, t('activated'))} data-testid="recipe-activate">{t('activate')}</Button>
          )}
          {canEdit && <Button type="primary" icon={<SaveOutlined />} loading={saving} onClick={() => void onSave()} data-testid="recipe-save">{t('save')}</Button>}
        </>
      )}
    >
      {!editable && <div className="bm-note is-info">{t('readOnly')}</div>}
      <Form layout="vertical" disabled={!canEdit} className="bm-formcard is-tight">
        <Controller control={control} name="dishId" render={({ field, fieldState }) => (
          <Form.Item label={t('dish')} required className="bm-span-2" validateStatus={fieldState.error ? 'error' : undefined} help={fieldState.error && t(fieldState.error.message ?? '')}>
            <LookupSelect resource="dishes" value={field.value || undefined} onChange={(v) => field.onChange(v ?? '')} disabled={!isNew} data-testid="recipe-dish" />
          </Form.Item>
        )} />
        <Controller control={control} name="baseServings" render={({ field }) => (
          <Form.Item label={t('baseServings')} tooltip={t('perServing')}><InputNumber min={1} max={10000} value={field.value} onChange={(v) => field.onChange(Number(v ?? 1))} style={{ width: '100%' }} /></Form.Item>
        )} />
        <Controller control={control} name="cookingLossRate" render={({ field }) => (
          <Form.Item label={t('cookingLoss')}><InputNumber min={0} max={90} value={field.value} onChange={(v) => field.onChange(Number(v ?? 0))} style={{ width: '100%' }} decimalSeparator="," /></Form.Item>
        )} />
        <Controller control={control} name="prepTimeMinutes" render={({ field }) => (
          <Form.Item label={t('prepTime')}><InputNumber min={0} max={1440} value={field.value ?? undefined} onChange={(v) => field.onChange(v === null ? null : Number(v))} style={{ width: '100%' }} /></Form.Item>
        )} />
        {dishQ.data && (
          <div className="bm-span-all bm-dchips">
            <span className="bm-tag t-neutral">{enumLabel('DishType', dishQ.data.dishType)}</span>
            <span className="bm-tag t-neutral">{enumLabel('CookingMethod', dishQ.data.cookingMethod)}</span>
            {dishQ.data.allergens && <span className="bm-tag t-orange">{dishQ.data.allergens}</span>}
          </div>
        )}
      </Form>

      <div className="bm-2col">
        <div className="bm-2col-main">
          <Form layout="vertical" disabled={!canEdit} component={false}>
            <Section
              title={t('ingredient')} hint={t('linesHint')}
              extra={canEdit && <Button icon={<PlusOutlined />} onClick={() => append({ ingredientId: '', grossG: 0, netGOverride: null, prepNote: '' })} data-testid="recipe-add-line">{t('addLine')}</Button>}
            >
              {formState.errors.items?.message && <div className="bm-note is-red" style={{ margin: '12px 16px 0' }}>{t(formState.errors.items.message)}</div>}
              {fields.length === 0 ? <div className="bm-noRows">{t('common:noData')}</div> : (
                <div className="bm-lines">
                  {fields.map((f, idx) => {
                    const line = lineOf(idx);
                    return (
                      <LineRow key={f.id} actions={canEdit && <Button type="text" danger icon={<DeleteOutlined />} onClick={() => remove(idx)} aria-label={t('common:actions.delete')} />}>
                        <LineCell label={t('ingredient')} flex="2 1 160px">
                          <Controller control={control} name={`items.${idx}.ingredientId`} render={({ field, fieldState }) => (
                            <>
                              <LookupSelect resource="ingredients" value={field.value || undefined} onChange={(v, item) => { field.onChange(v ?? ''); setValue(`items.${idx}.ingredientLabel`, item && !Array.isArray(item) ? `${(item as LookupItem).code} — ${(item as LookupItem).nameVi}` : '', { shouldDirty: true }); }} data-testid={`line-ingredient-${idx}`} />
                              {fieldState.error && <span className="bm-lcell-err">{t(fieldState.error.message ?? '')}</span>}
                            </>
                          )} />
                        </LineCell>
                        <LineCell label={t('gross')} flex="0 0 88px">
                          <Controller control={control} name={`items.${idx}.grossG`} render={({ field }) => (
                            <InputNumber min={0} value={field.value} onChange={(v) => field.onChange(v === null ? 0 : Number(v))} decimalSeparator="," data-testid={`line-gross-${idx}`} />
                          )} />
                        </LineCell>
                        <LineCell label={t('net')} flex="0 0 64px" static color={line?.missingFood ? 'var(--danger-text)' : undefined}>
                          {line ? <Tooltip title={line.missingFood ? t('noFood') : undefined}><span>{fmt.number(line.netG, 1)}</span></Tooltip> : '—'}
                        </LineCell>
                        <LineCell label={t('netOverride')} flex="0 0 88px">
                          <Controller control={control} name={`items.${idx}.netGOverride`} render={({ field }) => (
                            <InputNumber min={0} value={field.value ?? undefined} onChange={(v) => field.onChange(v === null ? null : Number(v))} decimalSeparator="," />
                          )} />
                        </LineCell>
                        <LineCell label={t('prepNote')} flex="1 1 100px">
                          <Controller control={control} name={`items.${idx}.prepNote`} render={({ field }) => <Input value={field.value ?? ''} onChange={field.onChange} />} />
                        </LineCell>
                      </LineRow>
                    );
                  })}
                </div>
              )}
            </Section>

            <Section title={t('instructions')}>
              <div className="bm-pad">
                <Controller control={control} name="instructions" render={({ field }) => (
                  <Input.TextArea rows={4} value={field.value ?? ''} onChange={field.onChange} maxLength={8000} showCount aria-label={t('instructions')} />
                )} />
              </div>
            </Section>
          </Form>

          <Section
            title={<span className="bm-sec-ico"><CalculatorOutlined />{t('scale')}</span>}
            extra={(
              <span className="bm-scale-in">
                <span>{t('servings')}</span>
                <InputNumber min={1} max={100000} value={servings ?? undefined} onChange={(v) => setServings(v === null ? null : Number(v))} placeholder="350" data-testid="recipe-servings" />
              </span>
            )}
          >
            {scaled ? (
              <Table size="small" rowKey="ingredientId" dataSource={scaled} pagination={false} scroll={{ x: 520 }}
                columns={[{ title: t('ingredient'), key: 'n', render: (_, r) => `${r.code} — ${r.name}` }, { title: t('gross'), dataIndex: 'grossG', align: 'right', render: (v) => fmt.number(v, 0) },
                  { title: t('net'), dataIndex: 'netG', align: 'right', render: (v) => fmt.number(v, 0) }, { title: t('purchaseUnits'), dataIndex: 'purchaseUnits', align: 'right', render: (v) => <b style={{ fontWeight: 600 }}>{fmt.number(v, 2)}</b> }]} />
            ) : <div className="bm-noRows">{t('servings')}</div>}
          </Section>

          {versions.data && versions.data.length > 1 && (
            <Section title={t('versions')}>
              <div className="bm-pad bm-dchips">
                {versions.data.map((v) => (
                  <button key={v.id} type="button" className={v.id === id ? 'bm-chip is-on' : 'bm-chip'} onClick={() => navigate(`/recipes/${v.id}`)}>
                    v{v.versionNo} · {enumLabel('RecipeStatus', v.status)}
                  </button>
                ))}
              </div>
            </Section>
          )}
        </div>
        <div className="bm-2col-side">
          <div style={{ position: screens.xl ? 'sticky' : undefined, top: 72 }}>
            <NutritionPanel nutrition={computed.data?.nutrition} cost={computed.data?.cost} warnings={computed.data?.warnings} />
            {computed.isError && <div className="bm-note is-red" style={{ marginTop: 8 }}>{problemMessage(computed.error)}</div>}
          </div>
        </div>
      </div>
      {compareOpen && detail && <CompareDrawer recipeId={detail.id} options={versions.data ?? []} onClose={() => setCompareOpen(false)} lang={i18n.language} />}
    </Page>
  );
}

function CompareDrawer({ recipeId, options, onClose }: { recipeId: string; options: any[]; onClose: () => void; lang: string }) {
  const { t } = useTranslation('recipe');
  const [other, setOther] = useState<string | undefined>(options.find((o) => o.id !== recipeId)?.id);
  const { data } = useQuery({ queryKey: ['recipe-compare', recipeId, other], enabled: !!other, queryFn: async () => (await http.get<any>('/recipes/compare', { params: { a: recipeId, b: other } })).data });
  return (
    <Drawer open onClose={onClose} title={t('compare')} width={Math.min(900, window.innerWidth)} rootClassName="bm-drawer">
      <Space style={{ marginBottom: 12 }}>
        <Typography.Text>{t('b')}</Typography.Text>
        <Select style={{ width: 220 }} value={other} onChange={setOther} options={options.filter((o) => o.id !== recipeId).map((o) => ({ value: o.id, label: `v${o.versionNo} (${o.status})` }))} />
      </Space>
      {data && (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Table size="small" rowKey="ingredientId" dataSource={data.rows} pagination={false}
            columns={[{ title: t('ingredient'), key: 'n', render: (_, r: any) => `${r.code} — ${r.name}` },
              { title: `${t('a')} (v${data.a.versionNo})`, dataIndex: 'a', align: 'right', render: (v) => fmt.number(v, 1) },
              { title: `${t('b')} (v${data.b.versionNo})`, dataIndex: 'b', align: 'right', render: (v) => fmt.number(v, 1) },
              { title: t('diff'), dataIndex: 'diff', align: 'right', render: (v: number) => <span style={{ color: v > 0 ? 'var(--primary-text)' : v < 0 ? 'var(--danger-text)' : undefined, fontWeight: 600 }}>{v > 0 ? '+' : ''}{fmt.number(v, 1)}</span> }]} />
          <Row gutter={12}>
            <Col xs={24} md={12}><NutritionPanel compact title={`${t('a')} — v${data.a.versionNo}`} nutrition={data.a.computed.nutrition} cost={data.a.computed.cost} /></Col>
            <Col xs={24} md={12}><NutritionPanel compact title={`${t('b')} — v${data.b.versionNo}`} nutrition={data.b.computed.nutrition} cost={data.b.computed.cost} /></Col>
          </Row>
        </Space>
      )}
    </Drawer>
  );
}

/** Quick nutrition & cost drawer for a dish (dish catalog row action). */
export function DishQuickView({ dishId, onClose }: { dishId: string; onClose: () => void }) {
  const { t } = useTranslation('recipe');
  const navigate = useNavigate();
  const { data } = useQuery({ queryKey: ['dish-quick', dishId], queryFn: async () => (await http.get<{ recipeId?: string; nutrition: NutritionView; cost?: CostView }>(`/dishes/${dishId}/quick`)).data });
  return (
    <Drawer open onClose={onClose} title={t('quick')} width={Math.min(520, window.innerWidth)}
      extra={data?.recipeId ? <Button onClick={() => navigate(`/recipes/${data.recipeId}`)}>{t('openRecipe')}</Button> : <Button type="primary" onClick={() => navigate(`/recipes/new?dishId=${dishId}`)}>{t('new')}</Button>}>
      {data && !data.recipeId ? <Alert type="warning" showIcon message={t('noRecipe')} /> : <NutritionPanel compact nutrition={data?.nutrition} cost={data?.cost} />}
    </Drawer>
  );
}
