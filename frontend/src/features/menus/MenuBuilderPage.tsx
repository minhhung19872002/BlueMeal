import { useEffect, useMemo, useState } from 'react';
import { Alert, App, Button, Card, Col, DatePicker, Drawer, Empty, Form, Input, InputNumber, List, Row, Select, Space, Table, Typography } from 'antd';
import { DeleteOutlined, PlusOutlined, SaveOutlined, SwapOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useBlocker, useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel, type LookupItem } from '@/shared/components/catalog/inputs';
import { NutritionPanel } from '@/shared/components/nutrition/NutritionPanel';
import { useBeforeUnload } from '@/shared/hooks/useTablePrefs';
import { ENUMS } from '@/shared/utils/enums';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';
import { EDITABLE, MenuStatusTag, type MenuDetail, type MenuSnapshot } from './shared';

interface Line { key: string; itemId?: string; dishId?: string; label?: string; slot: string; servingFactor: number }
interface Ctx {
  menuDate: dayjs.Dayjs | null; mealShiftId?: string; subjectGroupId?: string; customerId?: string; recommendationId?: string; structureTemplateId?: string;
  servings: number; budgetPerServing?: number | null; note?: string; planId?: string;
}
interface Suggestion { dishId: string; code: string; name: string; nameEn?: string; energy?: number; cost?: number | null; score: number; improvement: number }

const DEFAULT_SLOTS = ['Staple', 'MainProtein', 'StirFry', 'Soup', 'Vegetable', 'Dessert'];
let seq = 0;
const newKey = () => `l${++seq}`;

function useDebounced<T>(v: T, ms = 400) {
  const [d, setD] = useState(v);
  useEffect(() => { const h = setTimeout(() => setD(v), ms); return () => clearTimeout(h); }, [v, ms]);
  return d;
}

/** Manual menu builder with realtime nutrition/cost and swap suggestions (SPEC §7.4.4). */
export default function MenuBuilderPage() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const isNew = !id;
  const { t, i18n } = useTranslation('menu');
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const qc = useQueryClient();
  const [ctx, setCtx] = useState<Ctx>({
    menuDate: params.get('date') ? dayjs(params.get('date')) : dayjs().add(1, 'day'), mealShiftId: params.get('shiftId') ?? undefined,
    planId: params.get('planId') ?? undefined, subjectGroupId: params.get('subjectGroupId') ?? undefined, customerId: params.get('customerId') ?? undefined, servings: 100,
  });
  const [lines, setLines] = useState<Line[]>(DEFAULT_SLOTS.map((s) => ({ key: newKey(), slot: s, servingFactor: 1 })));
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [swapFor, setSwapFor] = useState<Line | null>(null);
  const [errors, setErrors] = useState<Record<string, boolean>>({});

  const { data: detail } = useQuery({ queryKey: ['menu', id], enabled: !isNew, queryFn: async () => (await http.get<MenuDetail>(`/menus/${id}`)).data });
  useEffect(() => {
    if (!detail) return;
    setCtx({
      menuDate: dayjs(detail.menuDate), mealShiftId: detail.mealShiftId, subjectGroupId: detail.subjectGroupId, customerId: detail.customerId, recommendationId: detail.recommendationId,
      structureTemplateId: detail.structureTemplateId, servings: detail.servings, budgetPerServing: detail.budgetPerServing, note: detail.note, planId: detail.planId,
    });
    setLines(detail.items.map((i) => ({
      key: newKey(), itemId: i.id, dishId: i.dishId, label: `${i.dishCode} — ${i18n.language === 'en' && i.dishNameEn ? i.dishNameEn : i.dishNameVi}`,
      slot: i.slot && i.slot !== 'null' ? i.slot : i.dishType ?? 'Other', servingFactor: i.servingFactor,
    })));
    setDirty(false);
  }, [detail, i18n.language]);

  const editable = isNew ? has('menu.create') : !!detail && EDITABLE.includes(detail.status) && has('menu.edit');
  useBeforeUnload(dirty);
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && !saving && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    if (blocker.state === 'blocked') modal.confirm({ title: t('common:confirmLeave'), onOk: () => blocker.proceed(), onCancel: () => blocker.reset() });
  }, [blocker, modal, t]);

  const patch = (p: Partial<Ctx>) => { setCtx((c) => ({ ...c, ...p })); setDirty(true); };
  const patchLine = (key: string, p: Partial<Line>) => { setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...p } : l))); setDirty(true); };

  const body = useMemo(() => ({
    menuDate: isoDate(ctx.menuDate), mealShiftId: ctx.mealShiftId, subjectGroupId: ctx.subjectGroupId ?? null, customerId: ctx.customerId ?? null,
    recommendationId: ctx.recommendationId ?? null, structureTemplateId: ctx.structureTemplateId ?? null, servings: ctx.servings || 1, budgetPerServing: ctx.budgetPerServing ?? null,
    note: ctx.note || null, planId: ctx.planId ?? null,
    items: lines.filter((l) => l.dishId).map((l) => ({ dishId: l.dishId, servingFactor: l.servingFactor || 1, slot: l.slot })),
  }), [ctx, lines]);
  const debounced = useDebounced(body);
  const computed = useQuery({
    queryKey: ['menu-compute', debounced],
    enabled: debounced.items.length > 0 && !!debounced.menuDate,
    queryFn: async () => (await http.post<MenuSnapshot>('/menus/compute', { ...debounced, mealShiftId: debounced.mealShiftId ?? '00000000-0000-0000-0000-000000000000' }, { params: { menuId: id } })).data,
    placeholderData: (p) => p,
  });

  const save = async () => {
    const errs = { menuDate: !ctx.menuDate, mealShiftId: !ctx.mealShiftId, items: body.items.length === 0 };
    setErrors(errs);
    if (Object.values(errs).some(Boolean)) { message.error(t('fixErrors')); return; }
    setSaving(true);
    try {
      void qc.invalidateQueries({ queryKey: ['menus'] });
      if (isNew) {
        const r = await http.post<{ id: string }>('/menus', body);
        setDirty(false);
        message.success(t('common:saved'));
        setTimeout(() => navigate(`/menus/${r.data.id}`, { replace: true }));
      } else {
        await http.put(`/menus/${id}`, { ...body, version: detail?.version });
        await qc.invalidateQueries({ queryKey: ['menu', id] });
        setDirty(false);
        message.success(t('common:saved'));
        setTimeout(() => navigate(`/menus/${id}`));
      }
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setSaving(false);
    }
  };

  const suggestions = useQuery({
    queryKey: ['menu-suggest', id, swapFor?.itemId],
    enabled: !!swapFor?.itemId && !!id,
    queryFn: async () => (await http.get<Suggestion[]>(`/menus/${id}/suggestions`, { params: { itemId: swapFor!.itemId } })).data,
  });

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Card>
        <div className="bm-page-header">
          <Space>
            <Typography.Title level={4} style={{ margin: 0 }}>{isNew ? t('new') : `${t('edit')} ${detail?.code ?? ''}`}</Typography.Title>
            {detail && <MenuStatusTag status={detail.status} />}
          </Space>
          <Space>
            <Button onClick={() => navigate(isNew ? '/menus' : `/menus/${id}`)}>{t('common:actions.back')}</Button>
            {editable && <Button type="primary" icon={<SaveOutlined />} loading={saving} onClick={() => void save()} data-testid="menu-save">{t('save')}</Button>}
          </Space>
        </div>
        {!editable && detail && <Alert type="info" showIcon message={t('readOnly')} style={{ marginBottom: 12 }} />}
        <Form layout="vertical" disabled={!editable}>
          <Row gutter={12}>
            <Col xs={24} sm={12} lg={6}>
              <Form.Item label={t('date')} required validateStatus={errors.menuDate ? 'error' : undefined}>
                <DatePicker value={ctx.menuDate} onChange={(v) => patch({ menuDate: v })} format={DATE_FORMAT} style={{ width: '100%' }} data-testid="menu-date" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} lg={6}>
              <Form.Item label={t('shift')} required validateStatus={errors.mealShiftId ? 'error' : undefined}>
                <LookupSelect resource="meal-shifts" value={ctx.mealShiftId} onChange={(v) => patch({ mealShiftId: v })} data-testid="menu-shift" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} lg={6}>
              <Form.Item label={t('subject')}><LookupSelect resource="subject-groups" value={ctx.subjectGroupId} onChange={(v) => patch({ subjectGroupId: v })} data-testid="menu-subject" /></Form.Item>
            </Col>
            <Col xs={24} sm={12} lg={6}>
              <Form.Item label={t('customer')}><LookupSelect resource="customers" value={ctx.customerId} onChange={(v) => patch({ customerId: v })} /></Form.Item>
            </Col>
            <Col xs={24} sm={12} lg={6}>
              <Form.Item label={t('recommendation')}><LookupSelect resource="recommendations" value={ctx.recommendationId} onChange={(v) => patch({ recommendationId: v })} /></Form.Item>
            </Col>
            <Col xs={24} sm={12} lg={6}>
              <Form.Item label={t('structure')}><LookupSelect resource="meal-structures" value={ctx.structureTemplateId} onChange={(v) => patch({ structureTemplateId: v })} /></Form.Item>
            </Col>
            <Col xs={12} lg={3}>
              <Form.Item label={t('servings')}><InputNumber min={1} max={100000} value={ctx.servings} onChange={(v) => patch({ servings: Number(v) || 1 })} style={{ width: '100%' }} /></Form.Item>
            </Col>
            <Col xs={12} lg={3}>
              <Form.Item label={t('budget')}><InputNumber min={0} step={1000} value={ctx.budgetPerServing ?? undefined} onChange={(v) => patch({ budgetPerServing: v ?? null })} style={{ width: '100%' }} /></Form.Item>
            </Col>
            <Col xs={24}>
              <Form.Item label={t('note')}><Input.TextArea rows={1} maxLength={1000} value={ctx.note} onChange={(e) => patch({ note: e.target.value })} /></Form.Item>
            </Col>
          </Row>
        </Form>
      </Card>
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={13}>
          <Card title={t('dishes')} size="small" extra={editable && <Button size="small" icon={<PlusOutlined />} onClick={() => { setLines([...lines, { key: newKey(), slot: 'Other', servingFactor: 1 }]); setDirty(true); }}>{t('addDish')}</Button>}>
            {errors.items && <Alert type="error" showIcon message={t('common:validation.requiredShort')} style={{ marginBottom: 8 }} />}
            <Table<Line>
              rowKey="key" dataSource={lines} pagination={false} size="small" scroll={{ x: 'max-content' }}
              columns={[
                { title: t('slot'), key: 'slot', width: 150, render: (_, l) => (
                  <Select size="small" value={l.slot} disabled={!editable} style={{ width: 140 }} onChange={(v) => patchLine(l.key, { slot: v, dishId: undefined, label: undefined, itemId: undefined })}
                    options={ENUMS.DishType.map((d) => ({ value: d, label: enumLabel('DishType', d) }))} />
                ) },
                { title: t('dishes'), key: 'dish', render: (_, l) => (
                  <LookupSelect resource="dishes" value={l.dishId} disabled={!editable} extraParams={{ dishType: l.slot }} style={{ minWidth: 240, width: '100%' }} data-testid={`menu-dish-${l.slot}`}
                    onChange={(v, item) => patchLine(l.key, { dishId: v ?? undefined, label: item ? `${(item as LookupItem).code} — ${(item as LookupItem).nameVi}` : undefined, itemId: undefined })} />
                ) },
                { title: t('factor'), key: 'f', width: 110, render: (_, l) => (
                  <InputNumber size="small" min={0.1} max={5} step={0.1} value={l.servingFactor} disabled={!editable} onChange={(v) => patchLine(l.key, { servingFactor: Number(v) || 1 })} />
                ) },
                { key: 'a', width: 80, render: (_, l) => editable && (
                  <Space size={0}>
                    {l.itemId && !dirty && <Button size="small" type="text" icon={<SwapOutlined />} title={t('swap')} onClick={() => setSwapFor(l)} />}
                    <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => { setLines(lines.filter((x) => x.key !== l.key)); setDirty(true); }} />
                  </Space>
                ) },
              ]}
            />
          </Card>
        </Col>
        <Col xs={24} lg={11}>
          {body.items.length === 0 ? <Card size="small" title={t('realtime')}><Empty /></Card> : (
            <NutritionPanel title={t('realtime')} nutrition={computed.data?.nutrition} cost={computed.data?.cost} warnings={computed.data?.warnings} />
          )}
          {computed.isError && <Alert type="error" showIcon message={problemMessage(computed.error)} style={{ marginTop: 8 }} />}
        </Col>
      </Row>
      <Drawer open={!!swapFor} onClose={() => setSwapFor(null)} title={`${t('suggestions')} — ${swapFor?.label ?? ''}`} width={460}>
        <List
          loading={suggestions.isFetching}
          dataSource={suggestions.data ?? []}
          locale={{ emptyText: t('noSuggestions') }}
          renderItem={(s) => (
            <List.Item actions={[<Button key="u" size="small" type="primary" onClick={() => { patchLine(swapFor!.key, { dishId: s.dishId, label: `${s.code} — ${s.name}`, itemId: undefined }); setSwapFor(null); }}>{t('swap')}</Button>]}>
              <List.Item.Meta
                title={`${s.code} — ${i18n.language === 'en' && s.nameEn ? s.nameEn : s.name}`}
                description={`${fmt.number(s.energy, 0)} kcal${s.cost != null ? ' · ' + fmt.money(s.cost) : ''} · ${t('improve')}: ${fmt.number(s.improvement, 1)}`}
              />
            </List.Item>
          )}
        />
      </Drawer>
    </Space>
  );
}
