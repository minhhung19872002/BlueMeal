import { useState } from 'react';
import { App, Button, Card, DatePicker, Empty, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Table, Tag, Typography } from 'antd';
import { DeleteOutlined, EditOutlined, PlusOutlined, PrinterOutlined, SyncOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { DATE_FORMAT, fmt, isoDate, mondayOf } from '@/shared/utils/format';

interface PublishedMenu { id: string; code: string; menuDate: string; shift: string; shiftEn?: string; shiftOrder: number; customer?: string; subject?: string; energyKcal?: number; dishes: { nameVi: string; nameEn?: string; type: string }[] }
interface PublishedResult { organization?: { name: string; nameEn?: string; logoFileId?: string } | null; from: string; to: string; menus: PublishedMenu[] }

/** Weekly published menu, printable on A4 (SPEC §7.4.6) — dishes and energy only, never costs. */
export function PublishedMenuPage() {
  const { t, i18n } = useTranslation('menu');
  const [week, setWeek] = useState(mondayOf(dayjs()));
  const [customerId, setCustomerId] = useState<string | undefined>();
  const from = week;
  const to = week.add(6, 'day');
  const { data, isFetching } = useQuery({
    queryKey: ['menus-published', isoDate(from), customerId],
    queryFn: async () => (await http.get<PublishedResult>('/menus/published', { params: { from: isoDate(from), to: isoDate(to), customerId } })).data,
    placeholderData: keepPreviousData,
  });
  const en = i18n.language === 'en';
  const menus = data?.menus ?? [];
  const shifts = [...new Map(menus.map((m) => [m.shift, { name: en && m.shiftEn ? m.shiftEn : m.shift, order: m.shiftOrder, key: m.shift }])).values()].sort((a, b) => a.order - b.order);
  const days = [...new Set(menus.map((m) => m.menuDate))].sort();

  return (
    <Card loading={isFetching && !data}>
      <div className="bm-page-header bm-no-print">
        <Typography.Title level={4}>{t('published.title')}</Typography.Title>
        <Space wrap>
          <DatePicker picker="week" value={week} onChange={(v) => v && setWeek(mondayOf(v))} allowClear={false} />
          <LookupSelect resource="customers" value={customerId} onChange={setCustomerId} placeholder={t('customer')} style={{ width: 240 }} />
          <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{t('print')}</Button>
        </Space>
      </div>
      <div className="bm-print-a4">
        <div style={{ textAlign: 'center', marginBottom: 12 }}>
          {data?.organization && <Typography.Text strong>{en && data.organization.nameEn ? data.organization.nameEn : data.organization.name}</Typography.Text>}
          <Typography.Title level={4} style={{ margin: '4px 0' }}>{t('published.subtitle', { from: fmt.date(isoDate(from)), to: fmt.date(isoDate(to)) })}</Typography.Title>
        </div>
        {menus.length === 0 ? <Empty description={t('published.none')} /> : (
          <table className="bm-calendar bm-published">
            <thead>
              <tr><th>{t('date')}</th>{shifts.map((s) => <th key={s.key}>{s.name}</th>)}</tr>
            </thead>
            <tbody>
              {days.map((d) => (
                <tr key={d}>
                  <th>{fmt.weekday(d)}<br />{fmt.date(d)}</th>
                  {shifts.map((s) => (
                    <td key={s.key}>
                      {menus.filter((m) => m.menuDate === d && m.shift === s.key).map((m) => (
                        <div key={m.id} data-testid="published-menu">
                          {(m.customer || m.subject) && <Typography.Text type="secondary" style={{ fontSize: 11 }}>{[m.customer, m.subject].filter(Boolean).join(' · ')}</Typography.Text>}
                          <ul style={{ margin: '2px 0', paddingInlineStart: 16 }}>
                            {m.dishes.map((x, i) => <li key={i}>{en && x.nameEn ? x.nameEn : x.nameVi}</li>)}
                          </ul>
                          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{fmt.number(m.energyKcal, 0)} {t('published.kcal')}</Typography.Text>
                        </div>
                      ))}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </Card>
  );
}

interface OrderRow { id: string; customerId: string; customer: string; orderDate: string; mealShiftId: string; shift?: string; subjectGroupId?: string; registeredServings: number; actualServings?: number | null; status: string; menuId?: string; note?: string; version: number }
interface SummaryRow { orderDate: string; mealShiftId: string; registered: number; actual: number; customers: number }

/** Meal orders by customers and daily totals (SPEC §7.4.7). */
export function MealOrdersPage() {
  const { t } = useTranslation('menu');
  const { message } = App.useApp();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([mondayOf(dayjs()), mondayOf(dayjs()).add(6, 'day')]);
  const [customerId, setCustomerId] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<OrderRow | 'new' | null>(null);
  const [form] = Form.useForm();
  const params = { from: isoDate(range[0]), to: isoDate(range[1]) };
  const list = useQuery({
    queryKey: ['meal-orders', params, customerId, page],
    queryFn: async () => (await http.get<PagedResult<OrderRow>>('/meal-orders', { params: { ...params, customerId, page, pageSize: 50 } })).data,
    placeholderData: keepPreviousData,
  });
  const summary = useQuery({ queryKey: ['meal-orders-summary', params], queryFn: async () => (await http.get<SummaryRow[]>('/meal-orders/summary', { params })).data });
  const shifts = useQuery({ queryKey: ['lookup', 'meal-shifts', 'all'], queryFn: async () => (await http.get<{ id: string; nameVi: string }[]>('/meal-shifts/lookup', { params: { take: 50 } })).data, staleTime: 300_000 });
  const shiftName = (id: string) => shifts.data?.find((s) => s.id === id)?.nameVi ?? '';
  const reload = () => { void list.refetch(); void summary.refetch(); };

  const open = (r?: OrderRow) => {
    form.setFieldsValue(r ? { ...r, orderDate: dayjs(r.orderDate) } : { orderDate: dayjs().add(1, 'day'), registeredServings: 100, status: 'Confirmed', customerId });
    setEditing(r ?? 'new');
  };
  const save = async () => {
    const v = await form.validateFields();
    const body = { ...v, orderDate: isoDate(v.orderDate), actualServings: v.actualServings ?? null, version: editing !== 'new' ? editing?.version : undefined };
    try {
      if (editing === 'new') await http.post('/meal-orders', body);
      else await http.put(`/meal-orders/${(editing as OrderRow).id}`, body);
      message.success(t('common:saved'));
      setEditing(null);
      reload();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const remove = async (id: string) => { try { await http.delete(`/meal-orders/${id}`); reload(); } catch (e) { message.error(problemMessage(e)); } };
  const applyToMenus = async () => {
    try {
      const r = await http.post<{ updated: number }>('/meal-orders/apply-to-menus', null, { params });
      message.success(t('orders.applied', { n: r.data.updated }));
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Card>
        <div className="bm-page-header">
          <Typography.Title level={4}>{t('orders.title')}</Typography.Title>
          <Space wrap>
            {has('menu.edit') && <Button icon={<SyncOutlined />} onClick={() => void applyToMenus()}>{t('orders.applyToMenus')}</Button>}
            {has('meal_order.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => open()} data-testid="order-add">{t('common:actions.add')}</Button>}
          </Space>
        </div>
        <div className="bm-toolbar">
          <DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} allowClear={false} />
          <LookupSelect resource="customers" value={customerId} onChange={setCustomerId} placeholder={t('customer')} style={{ width: 260 }} />
        </div>
        <Table<OrderRow>
          rowKey="id" loading={list.isFetching} dataSource={list.data?.items} scroll={{ x: 'max-content' }} size="middle"
          pagination={{ current: page, pageSize: 50, total: list.data?.totalItems, onChange: setPage, showTotal: (n) => t('common:totalItems', { count: n }) }}
          columns={[
            { title: t('date'), dataIndex: 'orderDate', width: 110, render: fmt.date },
            { title: t('shift'), dataIndex: 'shift', width: 140 },
            { title: t('customer'), dataIndex: 'customer' },
            { title: t('orders.registered'), dataIndex: 'registeredServings', align: 'right', width: 140, render: (v) => fmt.number(v) },
            { title: t('orders.actual'), dataIndex: 'actualServings', align: 'right', width: 140, render: (v) => fmt.number(v) },
            { title: t('status'), dataIndex: 'status', width: 130, render: (v) => <Tag>{enumLabel('MealOrderStatus', v)}</Tag> },
            { title: t('note'), dataIndex: 'note', responsive: ['lg'] },
            { key: 'a', width: 90, render: (_, r) => (
              <Space size={0}>
                {has('meal_order.edit') && <Button type="text" icon={<EditOutlined />} onClick={() => open(r)} />}
                {has('meal_order.delete') && <Popconfirm title={t('common:confirmDelete')} onConfirm={() => void remove(r.id)}><Button type="text" danger icon={<DeleteOutlined />} /></Popconfirm>}
              </Space>
            ) },
          ]}
        />
      </Card>
      <Card title={t('orders.summary')} size="small">
        <Table<SummaryRow>
          rowKey={(r) => r.orderDate + r.mealShiftId} dataSource={summary.data} pagination={false} size="small"
          columns={[
            { title: t('date'), dataIndex: 'orderDate', render: fmt.date },
            { title: t('shift'), dataIndex: 'mealShiftId', render: shiftName },
            { title: t('orders.customers'), dataIndex: 'customers', align: 'right' },
            { title: t('orders.registered'), dataIndex: 'registered', align: 'right', render: (v) => fmt.number(v) },
            { title: t('orders.actual'), dataIndex: 'actual', align: 'right', render: (v) => fmt.number(v) },
          ]}
        />
      </Card>
      <Modal open={editing !== null} title={t('orders.title')} onCancel={() => setEditing(null)} onOk={() => void save()} destroyOnHidden forceRender>
        <Form form={form} layout="vertical">
          <Form.Item name="customerId" label={t('customer')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}><LookupSelect resource="customers" disabled={editing !== 'new'} /></Form.Item>
          <Form.Item name="orderDate" label={t('date')} rules={[{ required: true }]}><DatePicker format={DATE_FORMAT} disabled={editing !== 'new'} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="mealShiftId" label={t('shift')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}><LookupSelect resource="meal-shifts" disabled={editing !== 'new'} /></Form.Item>
          <Form.Item name="subjectGroupId" label={t('subject')}><LookupSelect resource="subject-groups" /></Form.Item>
          <Space>
            <Form.Item name="registeredServings" label={t('orders.registered')} rules={[{ required: true }]}><InputNumber min={0} max={100000} /></Form.Item>
            <Form.Item name="actualServings" label={t('orders.actual')}><InputNumber min={0} max={100000} /></Form.Item>
          </Space>
          <Form.Item name="status" label={t('status')}><Select options={ENUMS.MealOrderStatus.map((s) => ({ value: s, label: enumLabel('MealOrderStatus', s) }))} /></Form.Item>
          <Form.Item name="note" label={t('note')}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}
