import { useState } from 'react';
import { App, Button, Card, Checkbox, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Radio, Select, Space, Table, Tabs, Tag, Typography } from 'antd';
import { DeleteOutlined, EditOutlined, PlusOutlined, PrinterOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';
import { QcTag } from '@/features/stock/shared';

interface Criterion { code: string; labelVi: string; labelEn: string; type?: string }
interface FormConfig { step1: Criterion[]; step2: Criterion[]; step3: Criterion[] }
type Step = 1 | 2 | 3;
type Row = Record<string, any> & { id: string; result: string; criteria?: string | null; version: number };

const DATE_TIME = 'DD/MM/YYYY HH:mm';

/** Criteria inputs rendered from the configurable form (A-23): checkbox for yes/no, number input for measurements. */
function CriteriaInputs({ items, value, onChange }: { items: Criterion[]; value?: Record<string, unknown>; onChange?: (v: Record<string, unknown>) => void }) {
  const { i18n } = useTranslation();
  const v = value ?? {};
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      {items.map((c) => {
        const label = i18n.language === 'en' ? c.labelEn : c.labelVi;
        return c.type === 'number'
          ? <Space key={c.code}><span>{label}</span><InputNumber size="small" value={v[c.code] as number | undefined} onChange={(n) => onChange?.({ ...v, [c.code]: n })} /></Space>
          : <Checkbox key={c.code} checked={v[c.code] === true} onChange={(e) => onChange?.({ ...v, [c.code]: e.target.checked })}>{label}</Checkbox>;
      })}
    </Space>
  );
}

/** Three-step food inspection logs (SPEC §5.6) with printable forms. */
export function InspectionsPage() {
  const { t, i18n } = useTranslation('inspection');
  const { message } = App.useApp();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const [step, setStep] = useState<Step>(1);
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([dayjs().subtract(7, 'day'), dayjs()]);
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Row | 'new' | null>(null);
  const [form] = Form.useForm();
  const config = useQuery({ queryKey: ['inspection-form'], queryFn: async () => (await http.get<FormConfig>('/inspections/form')).data, staleTime: 300_000 });
  const params = { page, pageSize: 50, from: isoDate(range[0]), to: isoDate(range[1]) };
  const list = useQuery({ queryKey: ['inspections', step, params], queryFn: async () => (await http.get<PagedResult<Row>>(`/inspections/step${step}`, { params })).data, placeholderData: keepPreviousData });
  const dayDishes = useQuery({
    queryKey: ['day-dishes', isoDate(range[1])], enabled: editing !== null && step !== 1,
    queryFn: async () => (await http.get<{ menuId: string; menuCode: string; shift: string; dishId: string; dishName: string }[]>('/inspections/day-dishes', { params: { date: isoDate(form.getFieldValue('inspectDate') ?? range[1]) } })).data,
  });
  const criteria = step === 1 ? config.data?.step1 : step === 2 ? config.data?.step2 : config.data?.step3;
  const open = (r?: Row) => {
    form.resetFields();
    if (r) {
      const parsed = r.criteria ? JSON.parse(r.criteria) : {};
      form.setFieldsValue({
        ...r, criteria: parsed, inspectedAt: r.inspectedAt ? dayjs(r.inspectedAt) : undefined, inspectDate: r.inspectDate ? dayjs(r.inspectDate) : undefined,
        cookStart: r.cookStart ? dayjs(r.cookStart) : undefined, cookEnd: r.cookEnd ? dayjs(r.cookEnd) : undefined, portionTime: r.portionTime ? dayjs(r.portionTime) : undefined,
        expiryDate: r.expiryDate ? dayjs(r.expiryDate) : undefined, dishMenu: r.dishId ? `${r.menuId ?? ''}|${r.dishId}` : undefined,
      });
    } else {
      form.setFieldsValue({ result: 'Pass', inspectedAt: dayjs(), inspectDate: dayjs(), originDocsOk: true, criteria: Object.fromEntries((criteria ?? []).filter((c) => c.type !== 'number').map((c) => [c.code, true])) });
    }
    setEditing(r ?? 'new');
  };
  const save = async () => {
    const v = await form.validateFields();
    const [menuId, dishId] = (v.dishMenu ?? '|').split('|');
    const body = {
      ...v, dishMenu: undefined, menuId: v.menuId ?? (menuId || null), dishId: v.dishId ?? (dishId || null), version: editing !== 'new' ? editing?.version : undefined,
      inspectedAt: v.inspectedAt?.toISOString(), inspectDate: isoDate(v.inspectDate), cookStart: v.cookStart?.toISOString() ?? null, cookEnd: v.cookEnd?.toISOString() ?? null,
      portionTime: v.portionTime?.toISOString() ?? null, expiryDate: isoDate(v.expiryDate) ?? null,
    };
    try {
      if (editing === 'new') await http.post(`/inspections/step${step}`, body);
      else await http.put(`/inspections/step${step}/${(editing as Row).id}`, body);
      message.success(t('common:saved'));
      setEditing(null);
      void list.refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const remove = async (id: string) => { try { await http.delete(`/inspections/step${step}/${id}`); void list.refetch(); } catch (e) { message.error(problemMessage(e)); } };
  const dish = (r: Row) => (i18n.language === 'en' && r.dishNameEn ? r.dishNameEn : r.dishName);
  const common = [
    { title: t('result'), dataIndex: 'result', width: 130, render: (v: string) => <QcTag value={v} /> },
    { title: t('sensory'), dataIndex: 'sensoryNote', width: 200 },
    { title: t('action'), dataIndex: 'action', width: 180 },
    { title: t('inspector'), dataIndex: 'inspector', width: 150 },
    { key: 'a', width: 90, className: 'bm-no-print', render: (_: unknown, r: Row) => (
      <Space size={0}>
        {has('inspection.edit') && <Button size="small" type="text" icon={<EditOutlined />} onClick={() => open(r)} />}
        {has('inspection.delete') && <Popconfirm title={t('common:confirmDelete')} onConfirm={() => void remove(r.id)}><Button size="small" type="text" danger icon={<DeleteOutlined />} /></Popconfirm>}
      </Space>
    ) },
  ];
  const columns = step === 1 ? [
    { title: t('inspectedAt'), dataIndex: 'inspectedAt', width: 140, render: fmt.dateTime },
    { title: t('ingredient'), key: 'i', width: 220, render: (_: unknown, r: Row) => `${r.ingredientCode} — ${i18n.language === 'en' && r.ingredientNameEn ? r.ingredientNameEn : r.ingredientName}` },
    { title: t('supplier'), dataIndex: 'partner', width: 180 },
    { title: t('receipt'), dataIndex: 'receiptNo', width: 160 },
    { title: t('quantity'), dataIndex: 'quantity', width: 100, align: 'right' as const, render: (v: number) => fmt.number(v, 3) },
    { title: t('lot'), dataIndex: 'lotNo', width: 100 },
    { title: t('expiry'), dataIndex: 'expiryDate', width: 110, render: fmt.date },
    { title: t('temp'), dataIndex: 'temperature', width: 90, align: 'right' as const, render: (v: number) => fmt.number(v, 1) },
    { title: t('originOk'), dataIndex: 'originDocsOk', width: 100, render: (v: boolean) => (v ? '✓' : '✗') },
    ...common,
  ] : step === 2 ? [
    { title: t('date'), dataIndex: 'inspectDate', width: 110, render: fmt.date },
    { title: t('menu'), dataIndex: 'menuCode', width: 160 },
    { title: t('dish'), key: 'd', width: 200, render: (_: unknown, r: Row) => dish(r) },
    { title: t('cookStart'), dataIndex: 'cookStart', width: 140, render: fmt.dateTime },
    { title: t('cookEnd'), dataIndex: 'cookEnd', width: 140, render: fmt.dateTime },
    { title: t('participants'), dataIndex: 'participants', width: 180 },
    { title: t('hygiene'), dataIndex: 'hygiene', width: 180 },
    ...common,
  ] : [
    { title: t('date'), dataIndex: 'inspectDate', width: 110, render: fmt.date },
    { title: t('menu'), dataIndex: 'menuCode', width: 160 },
    { title: t('dish'), key: 'd', width: 200, render: (_: unknown, r: Row) => dish(r) },
    { title: t('portionTime'), dataIndex: 'portionTime', width: 140, render: fmt.dateTime },
    { title: t('temp'), dataIndex: 'temperature', width: 90, align: 'right' as const, render: (v: number) => fmt.number(v, 1) },
    { title: t('utensils'), dataIndex: 'utensils', width: 180 },
    ...common,
  ];
  const result = Form.useWatch('result', form);

  return (
    <Card>
      <div className="bm-page-header bm-no-print">
        <Typography.Title level={4}>{t('title')}</Typography.Title>
        <Space wrap>
          <DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} allowClear={false} />
          <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{t('printForm')}</Button>
          {has('inspection.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => open()} data-testid="inspection-add">{t('common:actions.add')}</Button>}
        </Space>
      </div>
      <div className="bm-print-only"><Typography.Title level={5} style={{ textAlign: 'center' }}>{t(`form${step}`)}</Typography.Title><div style={{ textAlign: 'center' }}>{`${fmt.date(range[0].toDate())} – ${fmt.date(range[1].toDate())}`}</div></div>
      <Tabs className="bm-no-print" activeKey={String(step)} onChange={(k) => { setStep(Number(k) as Step); setPage(1); }}
        items={[{ key: '1', label: t('step1') }, { key: '2', label: t('step2') }, { key: '3', label: t('step3') }]} />
      <Table<Row>
        rowKey="id" loading={list.isFetching} dataSource={list.data?.items} scroll={{ x: 'max-content' }} size="small" columns={columns as never}
        pagination={{ current: page, pageSize: 50, total: list.data?.totalItems, onChange: setPage, showTotal: (n) => t('common:totalItems', { count: n }) }}
      />
      <Modal open={editing !== null} title={t(`step${step}`)} onCancel={() => setEditing(null)} onOk={() => void save()} width={720} destroyOnHidden forceRender okButtonProps={{ 'data-testid': 'inspection-save' } as never}>
        <Form form={form} layout="vertical">
          {step === 1 ? (
            <>
              <Space wrap>
                <Form.Item name="inspectedAt" label={t('inspectedAt')} rules={[{ required: true }]}><DatePicker showTime format={DATE_TIME} /></Form.Item>
                <Form.Item name="ingredientId" label={t('ingredient')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}><LookupSelect resource="ingredients" style={{ width: 300 }} /></Form.Item>
              </Space>
              <Space wrap>
                <Form.Item name="partnerId" label={t('supplier')}><LookupSelect resource="partners" style={{ width: 280 }} /></Form.Item>
                <Form.Item name="quantity" label={t('quantity')}><InputNumber min={0} /></Form.Item>
                <Form.Item name="lotNo" label={t('lot')}><Input maxLength={50} /></Form.Item>
                <Form.Item name="expiryDate" label={t('expiry')}><DatePicker format={DATE_FORMAT} /></Form.Item>
                <Form.Item name="temperature" label={t('temp')}><InputNumber min={-60} max={100} /></Form.Item>
              </Space>
              <Form.Item name="originDocsOk" valuePropName="checked"><Checkbox>{t('originOk')}</Checkbox></Form.Item>
              <Form.Item name="originDocs" label={t('origin')}><Input maxLength={500} /></Form.Item>
            </>
          ) : (
            <>
              <Space wrap>
                <Form.Item name="inspectDate" label={t('date')} rules={[{ required: true }]}><DatePicker format={DATE_FORMAT} onChange={() => void dayDishes.refetch()} /></Form.Item>
                <Form.Item name="dishMenu" label={t('dish')}>
                  <Select allowClear style={{ width: 360 }} loading={dayDishes.isFetching} placeholder={t('fromMenus')}
                    options={(dayDishes.data ?? []).map((d) => ({ value: `${d.menuId}|${d.dishId}`, label: `${d.shift} · ${d.menuCode} · ${d.dishName}` }))} />
                </Form.Item>
              </Space>
              {step === 2 ? (
                <>
                  <Space wrap>
                    <Form.Item name="cookStart" label={t('cookStart')}><DatePicker showTime format={DATE_TIME} /></Form.Item>
                    <Form.Item name="cookEnd" label={t('cookEnd')}><DatePicker showTime format={DATE_TIME} /></Form.Item>
                  </Space>
                  <Form.Item name="participants" label={t('participants')}><Input maxLength={1000} /></Form.Item>
                  <Form.Item name="hygiene" label={t('hygiene')}><Input maxLength={1000} /></Form.Item>
                </>
              ) : (
                <Space wrap>
                  <Form.Item name="portionTime" label={t('portionTime')}><DatePicker showTime format={DATE_TIME} /></Form.Item>
                  <Form.Item name="temperature" label={t('temp')}><InputNumber min={-60} max={150} /></Form.Item>
                  <Form.Item name="utensils" label={t('utensils')}><Input maxLength={500} style={{ width: 300 }} /></Form.Item>
                </Space>
              )}
            </>
          )}
          <Form.Item name="criteria" label={t('criteriaTitle')}><CriteriaInputs items={criteria ?? []} /></Form.Item>
          <Form.Item name="sensoryNote" label={t('sensory')}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
          <Form.Item name="result" label={t('result')}>
            <Radio.Group options={ENUMS.QualityResult.map((q) => ({ value: q, label: enumLabel('QualityResult', q) }))} />
          </Form.Item>
          <Form.Item name="action" label={t('action')} rules={[{ required: result === 'Fail', message: t('actionRequired') }]}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
          <Form.Item name="inspector" label={t('inspector')}><Input maxLength={200} /></Form.Item>
        </Form>
      </Modal>
    </Card>
  );
}

interface SampleRow { id: string; code: string; menuId?: string; menuCode?: string; dishId: string; dishName: string; dishNameEn?: string; sampledAt: string; weightG: number; storageTemp?: number; sampler: string; plannedDisposeAt: string; disposedAt?: string; disposer?: string; note?: string; state: string; version: number }

const STATE_COLOR: Record<string, string> = { kept: 'blue', due: 'orange', disposed: 'default' };

/** Food sample retention book (SPEC §5.6). */
export function SamplesPage() {
  const { t, i18n } = useTranslation('inspection');
  const { message } = App.useApp();
  const { has } = usePermission();
  const [state, setState] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState(false);
  const [dispose, setDispose] = useState<SampleRow | null>(null);
  const [disposeNote, setDisposeNote] = useState('');
  const [form] = Form.useForm();
  const settings = useQuery({ queryKey: ['sample-rules'], queryFn: async () => (await http.get<{ retentionHours: number; minWeightG: number }>('/food-samples/rules')).data, staleTime: 300_000 });
  const params = { page, pageSize: 50, state };
  const { data, isFetching, refetch } = useQuery({ queryKey: ['samples', params], queryFn: async () => (await http.get<PagedResult<SampleRow>>('/food-samples', { params })).data, placeholderData: keepPreviousData });
  const dishes = useQuery({
    queryKey: ['day-dishes-sample', open], enabled: open,
    queryFn: async () => (await http.get<{ menuId: string; menuCode: string; shift: string; dishId: string; dishName: string }[]>('/inspections/day-dishes', { params: { date: isoDate(dayjs()) } })).data,
  });
  const save = async () => {
    const v = await form.validateFields();
    const [menuId, dishId] = v.dishMenu ? String(v.dishMenu).split('|') : [null, v.dishId];
    try {
      await http.post('/food-samples', { menuId: menuId || null, dishId: dishId || v.dishId, sampledAt: v.sampledAt.toISOString(), weightG: v.weightG, storageTemp: v.storageTemp ?? null, sampler: v.sampler || null, note: v.note || null });
      message.success(t('common:saved'));
      setOpen(false);
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const doDispose = async () => {
    try {
      await http.post(`/food-samples/${dispose!.id}/dispose`, { note: disposeNote || null });
      setDispose(null);
      setDisposeNote('');
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  return (
    <Card>
      <div className="bm-page-header bm-no-print">
        <Typography.Title level={4}>{t('samples.title')}</Typography.Title>
        <Space wrap>
          <Select allowClear placeholder={t('samples.state')} value={state} onChange={(v) => { setState(v); setPage(1); }} style={{ width: 160 }}
            options={['kept', 'due', 'disposed'].map((s) => ({ value: s, label: t(`samples.${s}`) }))} />
          <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{t('printForm')}</Button>
          {has('sample.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); form.setFieldsValue({ sampledAt: dayjs(), weightG: settings.data?.minWeightG ?? 150 }); setOpen(true); }} data-testid="sample-add">{t('samples.new')}</Button>}
        </Space>
      </div>
      <div className="bm-print-only"><Typography.Title level={5} style={{ textAlign: 'center' }}>{t('samples.printTitle')}</Typography.Title></div>
      {settings.data && <Typography.Paragraph type="secondary" className="bm-no-print">{t('samples.rule', { h: settings.data.retentionHours, g: settings.data.minWeightG })}</Typography.Paragraph>}
      <Table<SampleRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} size="small"
        pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('samples.code'), dataIndex: 'code', width: 160 },
          { title: t('dish'), key: 'd', render: (_, r) => (i18n.language === 'en' && r.dishNameEn ? r.dishNameEn : r.dishName) },
          { title: t('menu'), dataIndex: 'menuCode', width: 160, responsive: ['lg'] },
          { title: t('samples.sampledAt'), dataIndex: 'sampledAt', width: 140, render: fmt.dateTime },
          { title: t('samples.weight'), dataIndex: 'weightG', width: 100, align: 'right', render: (v) => fmt.number(v, 0) },
          { title: t('samples.storageTemp'), dataIndex: 'storageTemp', width: 110, align: 'right', render: (v) => fmt.number(v, 1) },
          { title: t('samples.sampler'), dataIndex: 'sampler', width: 150 },
          { title: t('samples.plannedDispose'), dataIndex: 'plannedDisposeAt', width: 140, render: fmt.dateTime },
          { title: t('samples.disposedAt'), dataIndex: 'disposedAt', width: 140, render: fmt.dateTime },
          { title: t('samples.disposer'), dataIndex: 'disposer', width: 140 },
          { title: t('samples.state'), dataIndex: 'state', width: 120, render: (v) => <Tag color={STATE_COLOR[v]}>{t(`samples.${v}`)}</Tag> },
          { key: 'a', width: 110, className: 'bm-no-print', render: (_, r) => r.state !== 'disposed' && has('sample.dispose') && <Button size="small" onClick={() => setDispose(r)} data-testid="sample-dispose">{t('samples.dispose')}</Button> },
        ]}
      />
      <Modal open={open} title={t('samples.new')} onCancel={() => setOpen(false)} onOk={() => void save()} destroyOnHidden forceRender okButtonProps={{ 'data-testid': 'sample-save' } as never}>
        <Form form={form} layout="vertical">
          <Form.Item name="dishMenu" label={t('fromMenus')}>
            <Select allowClear loading={dishes.isFetching} options={(dishes.data ?? []).map((d) => ({ value: `${d.menuId}|${d.dishId}`, label: `${d.shift} · ${d.menuCode} · ${d.dishName}` }))} />
          </Form.Item>
          <Form.Item name="dishId" label={t('dish')} dependencies={['dishMenu']} rules={[({ getFieldValue }) => ({ validator: (_, v) => (v || getFieldValue('dishMenu') ? Promise.resolve() : Promise.reject(new Error(t('common:validation.requiredShort')))) })]}>
            <LookupSelect resource="dishes" data-testid="sample-dish" />
          </Form.Item>
          <Space wrap>
            <Form.Item name="sampledAt" label={t('samples.sampledAt')} rules={[{ required: true }]}><DatePicker showTime format={DATE_TIME} /></Form.Item>
            <Form.Item name="weightG" label={t('samples.weight')} rules={[{ required: true }]}><InputNumber min={1} max={10000} /></Form.Item>
            <Form.Item name="storageTemp" label={t('samples.storageTemp')}><InputNumber min={-30} max={30} /></Form.Item>
          </Space>
          <Form.Item name="sampler" label={t('samples.sampler')}><Input maxLength={200} /></Form.Item>
          <Form.Item name="note" label={t('common:fields.note')}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
        </Form>
      </Modal>
      <Modal open={!!dispose} title={`${t('samples.dispose')} ${dispose?.code ?? ''}`} onCancel={() => setDispose(null)} onOk={() => void doDispose()}>
        <Input.TextArea rows={2} value={disposeNote} onChange={(e) => setDisposeNote(e.target.value)} placeholder={t('common:fields.note')} maxLength={1000} />
      </Modal>
    </Card>
  );
}
