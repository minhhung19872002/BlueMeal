import { useState } from 'react';
import { App, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { Page } from '@/shared/components/PageChrome';
import { CalendarOutlined, CopyOutlined, DeleteOutlined, EditOutlined, PlusOutlined, TeamOutlined, ThunderboltOutlined, WarningFilled } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { DATE_FORMAT, fmt, isoDate, mondayOf } from '@/shared/utils/format';
import { zodRule, zs } from '@/shared/utils/validation';
import { EDITABLE, MENU_STATUS_COLOR } from './shared';

interface PlanRow {
  id: string; name: string; period: string; fromDate: string; toDate: string; customerId?: string; customer?: string; subjectGroupId?: string; subject?: string; status: string;
  budgetPerServing?: number; expectedServingsPerDay?: number; noRepeatDays: number; menuCount: number; version: number;
}
interface PlanDetail extends PlanRow { recommendationId?: string; structureTemplateId?: string; mealShiftIds: string[]; note?: string }

/** Menu plans list (SPEC §7.4.1). */
export function MenuPlansPage() {
  const { t } = useTranslation('menu');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<PlanDetail | 'new' | null>(null);
  const [form] = Form.useForm();
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['menu-plans', page],
    queryFn: async () => (await http.get<PagedResult<PlanRow>>('/menu-plans', { params: { page, pageSize: 20 } })).data,
  });
  const open = async (row?: PlanRow) => {
    if (!row) {
      const from = mondayOf(dayjs().add(1, 'week'));
      form.setFieldsValue({ name: '', period: 'Week', range: [from, from.add(6, 'day')], noRepeatDays: 7, status: 'Draft', mealShiftIds: [] });
      setEditing('new');
      return;
    }
    const d = (await http.get<PlanDetail>(`/menu-plans/${row.id}`)).data;
    form.setFieldsValue({ ...d, range: [dayjs(d.fromDate), dayjs(d.toDate)] });
    setEditing(d);
  };
  const save = async () => {
    const v = await form.validateFields();
    const body = { ...v, fromDate: isoDate(v.range[0]), toDate: isoDate(v.range[1]), range: undefined, version: editing !== 'new' ? editing?.version : undefined };
    try {
      if (editing === 'new') {
        const r = await http.post<{ id: string }>('/menu-plans', body);
        navigate(`/menus/plans/${r.data.id}`);
      } else {
        await http.put(`/menu-plans/${(editing as PlanDetail).id}`, body);
      }
      message.success(t('common:saved'));
      setEditing(null);
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const remove = async (id: string) => {
    try { await http.delete(`/menu-plans/${id}`); message.success(t('common:deleted')); void refetch(); } catch (e) { message.error(problemMessage(e)); }
  };
  return (
    <Page title={t('plans.title')} actions={has('menu_plan.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => void open()} data-testid="plan-add">{t('common:actions.add')}</Button>}>
      <div className="bm-card">
      <Table<PlanRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }}
        pagination={{ current: page, pageSize: 20, total: data?.totalItems, onChange: setPage, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('plans.name'), dataIndex: 'name', render: (v, r) => <a onClick={() => navigate(`/menus/plans/${r.id}`)} style={{ fontWeight: 500 }}>{v}</a> },
          { title: t('plans.period'), dataIndex: 'period', width: 100, render: (v) => enumLabel('PlanPeriod', v) },
          { title: t('plans.from'), dataIndex: 'fromDate', width: 110, render: fmt.date },
          { title: t('plans.to'), dataIndex: 'toDate', width: 110, render: fmt.date },
          { title: t('customer'), dataIndex: 'customer', responsive: ['lg'] },
          { title: t('subject'), dataIndex: 'subject', responsive: ['lg'] },
          { title: t('plans.menuCount'), dataIndex: 'menuCount', align: 'right', width: 110 },
          { title: t('status'), dataIndex: 'status', width: 130, render: (v) => <Tag>{enumLabel('PlanStatus', v)}</Tag> },
          { key: 'a', width: 130, render: (_, r) => (
            <Space size={0}>
              <Button type="text" icon={<CalendarOutlined />} title={t('plans.calendar')} onClick={() => navigate(`/menus/plans/${r.id}`)} />
              {has('menu_plan.edit') && <Button type="text" icon={<EditOutlined />} onClick={() => void open(r)} />}
              {has('menu_plan.delete') && <Popconfirm title={t('common:confirmDelete')} onConfirm={() => void remove(r.id)}><Button type="text" danger icon={<DeleteOutlined />} /></Popconfirm>}
            </Space>
          ) },
        ]}
      />
      <Modal open={editing !== null} title={t('plans.title')} onCancel={() => setEditing(null)} onOk={() => void save()} width={720} destroyOnHidden forceRender okButtonProps={{ 'data-testid': 'plan-save' } as never}>
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={t('plans.name')} rules={[zodRule(zs.required(200))]}><Input data-testid="plan-name" /></Form.Item>
          <Space wrap style={{ width: '100%' }} styles={{ item: { minWidth: 200 } }}>
            <Form.Item name="period" label={t('plans.period')}><Select options={ENUMS.PlanPeriod.map((p) => ({ value: p, label: enumLabel('PlanPeriod', p) }))} style={{ width: 150 }} /></Form.Item>
            <Form.Item name="range" label={`${t('plans.from')} – ${t('plans.to')}`} rules={[{ required: true }]}><DatePicker.RangePicker format={DATE_FORMAT} /></Form.Item>
            <Form.Item name="status" label={t('status')}><Select options={ENUMS.PlanStatus.map((p) => ({ value: p, label: enumLabel('PlanStatus', p) }))} style={{ width: 160 }} /></Form.Item>
          </Space>
          <Form.Item name="mealShiftIds" label={t('gen.shifts')}><LookupSelect resource="meal-shifts" multiple /></Form.Item>
          <Space wrap style={{ width: '100%' }}>
            <Form.Item name="customerId" label={t('customer')}><LookupSelect resource="customers" style={{ width: 300 }} /></Form.Item>
            <Form.Item name="subjectGroupId" label={t('subject')}><LookupSelect resource="subject-groups" style={{ width: 300 }} /></Form.Item>
            <Form.Item name="recommendationId" label={t('recommendation')}><LookupSelect resource="recommendations" style={{ width: 300 }} /></Form.Item>
            <Form.Item name="structureTemplateId" label={t('structure')}><LookupSelect resource="meal-structures" style={{ width: 300 }} /></Form.Item>
            <Form.Item name="budgetPerServing" label={t('budget')}><InputNumber min={0} step={1000} style={{ width: 160 }} /></Form.Item>
            <Form.Item name="expectedServingsPerDay" label={t('plans.expected')}><InputNumber min={0} style={{ width: 160 }} /></Form.Item>
            <Form.Item name="noRepeatDays" label={t('gen.noRepeat')}><InputNumber min={0} max={60} style={{ width: 160 }} /></Form.Item>
          </Space>
          <Form.Item name="note" label={t('note')}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
        </Form>
      </Modal>
      </div>
    </Page>
  );
}

interface Cell {
  id: string; code: string; menuDate: string; mealShiftId: string; status: string; energyKcal?: number; costPerServing?: number | null; nutritionScore?: number; criticalWarnings: number; servings: number;
  items: { id: string; dishId: string; nameVi: string; nameEn?: string; type: string }[];
}
interface Calendar {
  plan: { id: string; name: string; fromDate: string; toDate: string; subjectGroupId?: string; customerId?: string; structureTemplateId?: string; recommendationId?: string; budgetPerServing?: number; expectedServingsPerDay?: number };
  shifts: { id: string; code: string; nameVi: string; nameEn?: string }[];
  cells: Cell[];
}

/** Plan calendar: day × shift grid, drag & drop dishes between editable menus, week duplication, multi-customer application (SPEC §7.4.1). */
export function PlanCalendarPage() {
  const { id } = useParams();
  const { t, i18n } = useTranslation('menu');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const [copyOpen, setCopyOpen] = useState(false);
  const [applyOpen, setApplyOpen] = useState(false);
  const [weeks, setWeeks] = useState<[dayjs.Dayjs | null, dayjs.Dayjs | null]>([null, null]);
  const [customers, setCustomers] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const { data, refetch, isFetching } = useQuery({ queryKey: ['plan-calendar', id], queryFn: async () => (await http.get<Calendar>(`/menu-plans/${id}/calendar`)).data });

  if (!data) return <Card loading />;
  const from = dayjs(data.plan.fromDate);
  const days = Array.from({ length: dayjs(data.plan.toDate).diff(from, 'day') + 1 }, (_, i) => from.add(i, 'day'));
  const cellsAt = (d: dayjs.Dayjs, shiftId: string) => data.cells.filter((c) => c.menuDate === isoDate(d) && c.mealShiftId === shiftId);
  const lang = i18n.language;

  const onDrop = async (e: React.DragEvent, target?: Cell) => {
    e.preventDefault();
    const raw = e.dataTransfer.getData('application/x-bluemeal-item');
    if (!raw || !target) return;
    const { menuId, itemId } = JSON.parse(raw) as { menuId: string; itemId: string };
    if (menuId === target.id) return;
    try {
      await http.post('/menus/move-item', { fromMenuId: menuId, itemId, toMenuId: target.id });
      void refetch();
    } catch (err) {
      message.error(problemMessage(err));
    }
  };
  const generate = async () => {
    setBusy(true);
    try {
      const plan = (await http.get<PlanDetail>(`/menu-plans/${id}`)).data;
      const r = await http.post<{ saved?: string[]; jobId?: string }>('/menus/generate', {
        save: true,
        request: {
          fromDate: plan.fromDate, toDate: plan.toDate, mealShiftIds: plan.mealShiftIds.length ? plan.mealShiftIds : data.shifts.map((s) => s.id), subjectGroupId: plan.subjectGroupId ?? null,
          customerId: plan.customerId ?? null, recommendationId: plan.recommendationId ?? null, structureTemplateId: plan.structureTemplateId ?? null, budgetPerServing: plan.budgetPerServing ?? null,
          noRepeatDays: plan.noRepeatDays, preferExpiringStock: false, newDishRatio: 0.2, servings: plan.expectedServingsPerDay || 100, planId: plan.id, skipWeekends: true,
        },
      });
      message.success(r.data.jobId ? t('gen.queued') : t('gen.saved', { n: r.data.saved?.length ?? 0 }));
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const copyWeek = async () => {
    try {
      const r = await http.post<{ created: number }>(`/menu-plans/${id}/copy-week`, { sourceWeekStart: isoDate(weeks[0] && mondayOf(weeks[0])), targetWeekStart: isoDate(weeks[1] && mondayOf(weeks[1])) });
      message.success(t('gen.saved', { n: r.data.created }));
      setCopyOpen(false);
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const apply = async () => {
    try {
      const r = await http.post<{ created: number }>(`/menu-plans/${id}/apply-customers`, { customerIds: customers });
      message.success(t('gen.saved', { n: r.data.created }));
      setApplyOpen(false);
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const newMenuUrl = (d: dayjs.Dayjs, shiftId: string) =>
    `/menus/new?date=${isoDate(d)}&shiftId=${shiftId}&planId=${id}${data.plan.subjectGroupId ? `&subjectGroupId=${data.plan.subjectGroupId}` : ''}${data.plan.customerId ? `&customerId=${data.plan.customerId}` : ''}`;
  const rowClass = (d: dayjs.Dayjs) => (d.day() === 0 ? 'is-sun' : undefined);

  return (
    <Page
      title={`${t('plans.calendar')} — ${data.plan.name}`}
      sub={t('plans.dragHint')}
      actions={(
        <>
          <Button onClick={() => navigate('/menus/plans')}>{t('common:actions.back')}</Button>
          {has('menu.create') && <Button icon={<ThunderboltOutlined />} className="bm-btn-soft" loading={busy || isFetching} onClick={() => void generate()}>{t('plans.generate')}</Button>}
          {has('menu.create') && <Button icon={<CopyOutlined />} onClick={() => setCopyOpen(true)}>{t('plans.copyWeek')}</Button>}
          {has('menu.create') && <Button icon={<TeamOutlined />} onClick={() => setApplyOpen(true)}>{t('plans.applyCustomers')}</Button>}
        </>
      )}
    >
      <div className="bm-card bm-cal-wrap">
        <table className="bm-cal">
          <thead>
            <tr>
              <th>{t('date')}</th>
              {data.shifts.map((s) => <th key={s.id}>{lang === 'en' && s.nameEn ? s.nameEn : s.nameVi}</th>)}
            </tr>
          </thead>
          <tbody>
            {days.map((d) => (
              <tr key={d.toString()} className={rowClass(d)}>
                <th>{fmt.weekday(d, true)}<small>{fmt.date(d.toDate())}</small></th>
                {data.shifts.map((s) => {
                  const cs = cellsAt(d, s.id);
                  return (
                    <td key={s.id}>
                      {cs.length === 0 && (
                        has('menu.create')
                          ? <button type="button" className="bm-cal-add" aria-label={t('plans.addMenu')} title={t('plans.addMenu')} onClick={() => navigate(newMenuUrl(d, s.id))}>+</button>
                          : <span className="bm-cal-none">{t('plans.empty')}</span>
                      )}
                      {cs.map((c) => {
                        const canDrop = EDITABLE.includes(c.status) && has('menu.edit');
                        return (
                          <div key={c.id} className="bm-cal-card" onDragOver={(e) => canDrop && e.preventDefault()} onDrop={(e) => void onDrop(e, c)} data-testid="plan-cell">
                            <span className="bm-cal-top" role="link" tabIndex={0} onClick={() => navigate(`/menus/${c.id}`)} onKeyDown={(e) => e.key === 'Enter' && navigate(`/menus/${c.id}`)}>
                              <Tag color={MENU_STATUS_COLOR[c.status]}>{enumLabel('MenuStatus', c.status)}</Tag>
                              <span className="bm-cal-kcal">
                                {c.criticalWarnings > 0 && <Tooltip title={t('critical')}><WarningFilled style={{ color: 'var(--danger)', marginRight: 4 }} /></Tooltip>}
                                {fmt.number(c.energyKcal, 0)} kcal
                              </span>
                            </span>
                            <span className="bm-cal-dishes">
                              {c.items.map((i) => (
                                <span key={i.id} className="bm-cal-dish" draggable={canDrop} title={enumLabel('DishType', i.type)}
                                  onDragStart={(e) => e.dataTransfer.setData('application/x-bluemeal-item', JSON.stringify({ menuId: c.id, itemId: i.id }))}>
                                  {lang === 'en' && i.nameEn ? i.nameEn : i.nameVi}
                                </span>
                              ))}
                            </span>
                            {c.costPerServing != null && <span className="bm-cal-none">{fmt.money(c.costPerServing)}</span>}
                          </div>
                        );
                      })}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Modal open={copyOpen} title={t('plans.copyWeek')} onCancel={() => setCopyOpen(false)} onOk={() => void copyWeek()} okButtonProps={{ disabled: !weeks[0] || !weeks[1] }}>
        <Space direction="vertical" style={{ width: '100%' }}>
          <Typography.Text>{t('plans.sourceWeek')}</Typography.Text>
          <DatePicker picker="week" value={weeks[0]} onChange={(v) => setWeeks([v, weeks[1]])} style={{ width: '100%' }} />
          <Typography.Text>{t('plans.targetWeek')}</Typography.Text>
          <DatePicker picker="week" value={weeks[1]} onChange={(v) => setWeeks([weeks[0], v])} style={{ width: '100%' }} />
        </Space>
      </Modal>
      <Modal open={applyOpen} title={t('plans.applyCustomers')} onCancel={() => setApplyOpen(false)} onOk={() => void apply()} okButtonProps={{ disabled: customers.length === 0 }}>
        <LookupSelect resource="customers" multiple value={customers} onChange={(v) => setCustomers(v ?? [])} style={{ width: '100%' }} />
      </Modal>
    </Page>
  );
}
