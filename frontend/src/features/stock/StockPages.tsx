import { useEffect, useRef, useState } from 'react';
import { App, Button, Card, Checkbox, Col, DatePicker, Empty, Form, Grid, Input, InputNumber, Modal, Popconfirm, Row, Space, Statistic, Table, Tabs, Tag, Typography } from 'antd';
import { CheckOutlined, LockOutlined, PlusOutlined, SaveOutlined, UnlockOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';
import { InfoGrid, Page, Section, Totals } from '@/shared/components/PageChrome';
import { idemKey, ingName, StatusTag } from './shared';

// ================================================================== stock takes

interface TakeRow { id: string; docNo: string; takeDate: string; warehouse: string; status: string; members?: string; lineCount: number; counted: number; differences: number }
interface TakeLine { id: string; ingredientId: string; ingredientCode: string; ingredientName: string; ingredientNameEn?: string; unit?: string; lotNo?: string; bookQty: number; actualQty?: number | null; difference: number; unitCost: number; cause?: string; resolution?: string }
interface TakeDetail { id: string; docNo: string; takeDate: string; warehouseId: string; warehouse?: string; status: string; members?: string; note?: string; adjustDocs: string[]; version: number; lines: TakeLine[] }

export function StockTakesPage() {
  const { t } = useTranslation('stock');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState(false);
  const [form] = Form.useForm();
  const { data, isFetching } = useQuery({ queryKey: ['stock-takes', page], queryFn: async () => (await http.get<PagedResult<TakeRow>>('/stock-takes', { params: { page, pageSize: 30 } })).data, placeholderData: keepPreviousData });
  const start = async () => {
    const v = await form.validateFields();
    try {
      const r = await http.post<{ id: string }>('/stock-takes', { ...v, takeDate: isoDate(v.takeDate) }, { headers: { 'Idempotency-Key': idemKey() } });
      navigate(`/stock/takes/${r.data.id}`);
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  return (
    <Page title={t('takes.title')} actions={has('stock.take.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.setFieldsValue({ takeDate: dayjs() }); setOpen(true); }} data-testid="take-new">{t('takes.new')}</Button>}>
      <div className="bm-card">
      <Table<TakeRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} onRow={(r) => ({ onClick: () => navigate(`/stock/takes/${r.id}`), style: { cursor: 'pointer' } })}
        pagination={{ current: page, pageSize: 30, total: data?.totalItems, onChange: setPage, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('docNo'), dataIndex: 'docNo', width: 170 },
          { title: t('docDate'), dataIndex: 'takeDate', width: 110, render: fmt.date },
          { title: t('warehouse'), dataIndex: 'warehouse' },
          { title: t('takes.counted'), key: 'c', width: 110, align: 'right', render: (_, r) => `${r.counted}/${r.lineCount}` },
          { title: t('takes.diff'), dataIndex: 'differences', width: 100, align: 'right' },
          { title: t('status'), dataIndex: 'status', width: 130, render: (v) => <StatusTag enumName="StockTakeStatus" status={v} /> },
        ]}
      />
      <Modal open={open} title={t('takes.new')} onCancel={() => setOpen(false)} onOk={() => void start()} destroyOnHidden forceRender okButtonProps={{ 'data-testid': 'take-start' } as never}>
        <Form form={form} layout="vertical">
          <Form.Item name="warehouseId" label={t('warehouse')} rules={[{ required: true, message: t('common:validation.requiredShort') }]}><LookupSelect resource="warehouses" data-testid="take-warehouse" /></Form.Item>
          <Form.Item name="takeDate" label={t('docDate')} rules={[{ required: true }]}><DatePicker format={DATE_FORMAT} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="members" label={t('takes.members')}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
          <Form.Item name="ingredientIds" label={t('ingredient')}><LookupSelect resource="ingredients" multiple /></Form.Item>
          <Form.Item name="note" label={t('note')}><Input maxLength={1000} /></Form.Item>
        </Form>
      </Modal>
      </div>
    </Page>
  );
}

/** Counting screen (mobile friendly: one input per line) and approval with automatic adjustments. */
export function StockTakeDetailPage() {
  const { id } = useParams();
  const { t, i18n } = useTranslation('stock');
  const { message, modal } = App.useApp();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const { has } = usePermission();
  const screens = Grid.useBreakpoint();
  const { data } = useQuery({ queryKey: ['stock-take', id], queryFn: async () => (await http.get<TakeDetail>(`/stock-takes/${id}`)).data });
  const [lines, setLines] = useState<TakeLine[]>([]);
  const [members, setMembers] = useState('');
  const [busy, setBusy] = useState(false);
  const [extra, setExtra] = useState<{ ingredientId?: string; actualQty?: number }>({});
  // latest row version: a save followed quickly by approve must not send a stale version
  const versionRef = useRef<number | undefined>(undefined);
  useEffect(() => { if (data) { setLines(data.lines); setMembers(data.members ?? ''); versionRef.current = data.version; } }, [data]);
  if (!data) return <Card loading />;
  const editable = (data.status === 'Draft' || data.status === 'Counting') && has('stock.take.edit');
  const patch = (lid: string, p: Partial<TakeLine>) => setLines((ls) => ls.map((l) => (l.id === lid ? { ...l, ...p, difference: (p.actualQty !== undefined ? (p.actualQty ?? l.bookQty) : (l.actualQty ?? l.bookQty)) - l.bookQty } : l)));
  const save = async (newLines?: { ingredientId: string; actualQty: number }[]) => {
    setBusy(true);
    try {
      await http.put(`/stock-takes/${id}`, { members, note: data.note, version: versionRef.current, lines: lines.map((l) => ({ id: l.id, actualQty: l.actualQty ?? null, cause: l.cause ?? null, resolution: l.resolution ?? null })), newLines });
      const fresh = await qc.fetchQuery({ queryKey: ['stock-take', id], queryFn: async () => (await http.get<TakeDetail>(`/stock-takes/${id}`)).data, staleTime: 0 });
      versionRef.current = fresh.version;
      message.success(t('common:saved'));
      return true;
    } catch (e) {
      message.error(problemMessage(e));
      return false;
    } finally {
      setBusy(false);
    }
  };
  const approve = () => modal.confirm({
    title: t('takes.approve'), content: t('takes.approveConfirm'), okButtonProps: { 'data-testid': 'confirm-approve' } as never,
    onOk: async () => {
      if (!(await save())) return;
      try {
        await http.post(`/stock-takes/${id}/approve`, null, { headers: { 'Idempotency-Key': idemKey() } });
        message.success(t('takes.approve'));
        await qc.invalidateQueries({ queryKey: ['stock-take', id] });
      } catch (e) {
        message.error(problemMessage(e));
      }
    },
  });
  const counted = lines.filter((l) => l.actualQty != null).length;
  return (
    <Page
      title={`${t('takes.title')} ${data.docNo}`}
      tag={<StatusTag enumName="StockTakeStatus" status={data.status} />}
      actions={(
        <>
          <Button onClick={() => navigate('/stock/takes')}>{t('common:actions.back')}</Button>
          {editable && <Button icon={<SaveOutlined />} loading={busy} onClick={() => void save()} data-testid="take-save">{t('takes.saveCounts')}</Button>}
          {editable && has('stock.take.approve') && <Button type="primary" icon={<CheckOutlined />} disabled={counted < lines.length} onClick={approve} data-testid="take-approve">{t('takes.approve')}</Button>}
        </>
      )}
    >
      <div className="bm-card bm-pad bm-stack">
        <InfoGrid items={[
          { key: 'w', label: t('warehouse'), value: data.warehouse },
          { key: 'd', label: t('docDate'), value: fmt.date(data.takeDate) },
          { key: 'c', label: t('takes.counted'), value: `${counted}/${lines.length}` },
          ...(data.adjustDocs.length > 0 ? [{ key: 'a', label: t('takes.adjustDocs'), value: data.adjustDocs.join(', ') }] : []),
        ]} />
        {editable ? <Input.TextArea rows={1} value={members} onChange={(e) => setMembers(e.target.value)} placeholder={t('takes.members')} maxLength={1000} /> : data.members && <div className="bm-info-l">{t('takes.members')}: <span style={{ color: 'var(--ink)' }}>{data.members}</span></div>}
      </div>
      <Section title={t('lineItems')} extra={editable && (
        <Space wrap>
          <LookupSelect resource="ingredients" value={extra.ingredientId} onChange={(v) => setExtra({ ...extra, ingredientId: v })} placeholder={t('takes.addItem')} style={{ width: 240 }} />
          <InputNumber min={0} value={extra.actualQty} onChange={(v) => setExtra({ ...extra, actualQty: v ?? undefined })} placeholder={t('takes.actual')} />
          <Button icon={<PlusOutlined />} disabled={!extra.ingredientId || extra.actualQty == null} onClick={() => void save([{ ingredientId: extra.ingredientId!, actualQty: extra.actualQty! }]).then((ok) => ok && setExtra({}))} />
        </Space>
      )}>
        <Table<TakeLine>
          rowKey="id" dataSource={lines} pagination={false} size="small" scroll={{ x: 'max-content' }}
          columns={[
            { title: t('ingredient'), key: 'ing', render: (_, l) => <>{ingName(l, i18n.language)}{l.lotNo && <Typography.Text type="secondary"> · {l.lotNo}</Typography.Text>}</> },
            { title: t('takes.book'), dataIndex: 'bookQty', width: 110, align: 'right', render: (v, l) => `${fmt.number(v, 3)} ${l.unit ?? ''}` },
            { title: t('takes.actual'), key: 'a', width: 130, align: 'right', render: (_, l) => (editable
              ? <InputNumber size={screens.md ? 'small' : 'middle'} min={0} value={l.actualQty ?? undefined} onChange={(v) => patch(l.id, { actualQty: v ?? null })} inputMode="decimal" data-testid="take-actual" />
              : fmt.number(l.actualQty, 3)) },
            { title: t('takes.diff'), key: 'd', width: 100, align: 'right', render: (_, l) => <span style={{ fontWeight: 600, color: l.difference ? 'var(--danger-text)' : undefined }}>{l.difference > 0 ? '+' : ''}{fmt.number(l.difference, 3)}</span> },
            { title: t('takes.cause'), key: 'c', width: 200, render: (_, l) => (editable ? <Input size="small" maxLength={500} value={l.cause} onChange={(e) => patch(l.id, { cause: e.target.value })} /> : l.cause) },
            { title: t('takes.resolution'), key: 'r', width: 200, responsive: ['lg'], render: (_, l) => (editable ? <Input size="small" maxLength={500} value={l.resolution} onChange={(e) => patch(l.id, { resolution: e.target.value })} /> : l.resolution) },
          ]}
        />
      </Section>
    </Page>
  );
}

// ================================================================== balances, lots, stock card

interface BalanceRow { warehouseId: string; warehouse: string; ingredientId: string; ingredientCode: string; ingredientName: string; ingredientNameEn?: string; unit?: string; quantity: number; value: number; averageCost: number; minStock: number; belowMin: boolean; nearestExpiry?: string }
interface LotRow { id: string; warehouse: string; ingredientCode: string; ingredientName: string; ingredientNameEn?: string; unit?: string; lotNo?: string; expiryDate?: string; receivedDate: string; quantity: number; unitCost: number; daysLeft?: number | null }

export function StockBalancePage() {
  const { t, i18n } = useTranslation('stock');
  const screens = Grid.useBreakpoint();
  const [params] = useSearchParams();
  const [page, setPage] = useState(1);
  const [warehouseId, setWarehouseId] = useState<string>();
  const [date, setDate] = useState<dayjs.Dayjs | null>(null);
  const [q, setQ] = useState('');
  const [belowMin, setBelowMin] = useState(false);
  const [days, setDays] = useState<number | null>(7);
  const query = { page, pageSize: 50, warehouseId, date: isoDate(date), q: q || undefined, belowMin: belowMin || undefined };
  const { data, isFetching } = useQuery({
    queryKey: ['stock-balances', query],
    queryFn: async () => (await http.get<PagedResult<BalanceRow> & { totalValue: number }>('/stock/balances', { params: query })).data, placeholderData: keepPreviousData,
  });
  const lots = useQuery({
    queryKey: ['stock-lots', warehouseId, days, params.get('ingredientId')],
    queryFn: async () => (await http.get<LotRow[]>('/stock/lots', { params: { warehouseId, expiringDays: days ?? undefined, ingredientId: params.get('ingredientId') ?? undefined } })).data,
  });
  return (
    <Page title={t('balance.title')}>
      <div className="bm-card bm-pad">
      <div className="bm-toolbar">
        <LookupSelect resource="warehouses" placeholder={t('warehouse')} value={warehouseId} onChange={(v) => { setWarehouseId(v); setPage(1); }} style={{ width: screens.md ? 200 : '100%' }} />
        <DatePicker placeholder={t('balance.atDate')} value={date} onChange={(v) => { setDate(v); setPage(1); }} format={DATE_FORMAT} />
        <Input.Search allowClear placeholder={t('ingredient')} onSearch={(v) => { setQ(v); setPage(1); }} style={{ width: screens.md ? 220 : '100%' }} />
        <Checkbox checked={belowMin} onChange={(e) => { setBelowMin(e.target.checked); setPage(1); }}>{t('balance.belowMin')}</Checkbox>
      </div>
      <Tabs items={[
        {
          key: 'balance', label: t('balance.title'),
          children: (
            <>
              <Statistic title={t('balance.totalValue')} value={fmt.money(data?.totalValue ?? 0)} style={{ marginBottom: 12 }} />
              <Table<BalanceRow>
                rowKey={(r) => r.warehouseId + r.ingredientId} loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} size="middle"
                pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false }}
                columns={[
                  { title: t('ingredient'), key: 'i', render: (_, r) => ingName(r, i18n.language) },
                  { title: t('warehouse'), dataIndex: 'warehouse', width: 150 },
                  { title: t('quantity'), key: 'q', align: 'right', width: 130, render: (_, r) => <Typography.Text type={r.belowMin ? 'danger' : undefined}>{fmt.number(r.quantity, 3)} {r.unit}</Typography.Text> },
                  { title: t('balance.avg'), dataIndex: 'averageCost', align: 'right', width: 130, render: fmt.money },
                  { title: t('balance.value'), dataIndex: 'value', align: 'right', width: 140, render: fmt.money },
                  { title: t('balance.min'), dataIndex: 'minStock', align: 'right', width: 110, render: (v) => fmt.number(v, 3) },
                  { title: t('balance.nearestExpiry'), dataIndex: 'nearestExpiry', width: 120, render: fmt.date },
                  { key: 'card', width: 90, render: (_, r) => <Link to={`/stock/card?ingredientId=${r.ingredientId}&warehouseId=${r.warehouseId}`}>{t('balance.card')}</Link> },
                ]}
              />
            </>
          ),
        },
        {
          key: 'lots', label: t('balance.lots'),
          children: (
            <>
              <Space style={{ marginBottom: 8 }}><span>{t('balance.expiring')}</span><InputNumber min={0} value={days ?? undefined} onChange={(v) => setDays(v ?? null)} /></Space>
              <Table<LotRow>
                rowKey="id" loading={lots.isFetching} dataSource={lots.data} scroll={{ x: 'max-content' }} size="small" pagination={{ pageSize: 50 }}
                columns={[
                  { title: t('ingredient'), key: 'i', render: (_, r) => ingName(r, i18n.language) },
                  { title: t('warehouse'), dataIndex: 'warehouse', width: 150 },
                  { title: t('lot'), dataIndex: 'lotNo', width: 120 },
                  { title: t('expiry'), dataIndex: 'expiryDate', width: 110, render: fmt.date },
                  { title: t('balance.daysLeft'), dataIndex: 'daysLeft', width: 100, align: 'right', render: (v) => (v == null ? '' : <Tag color={v < 0 ? 'red' : v <= 3 ? 'orange' : 'default'}>{v}</Tag>) },
                  { title: t('quantity'), key: 'q', width: 120, align: 'right', render: (_, r) => `${fmt.number(r.quantity, 3)} ${r.unit ?? ''}` },
                  { title: t('unitPrice'), dataIndex: 'unitCost', width: 120, align: 'right', render: fmt.money },
                ]}
              />
            </>
          ),
        },
      ]} />
      </div>
    </Page>
  );
}

interface CardResult {
  ingredient: { id: string; code: string; name: string; nameEn?: string; unit?: string }; opening: { quantity: number; value: number }; closing: { quantity: number; value: number };
  totals: { inQty: number; outQty: number };
  rows: { id: string; docDate: string; docType: string; docId: string; docNo: string; type: string; lotNo?: string; inQty: number; outQty: number; unitCost: number; amount: number; balanceQty: number; balanceValue: number }[];
}

export function StockCardPage() {
  const { t } = useTranslation('stock');
  const enumLabel = useEnumLabel();
  const [params] = useSearchParams();
  const [ingredientId, setIngredientId] = useState<string | undefined>(params.get('ingredientId') ?? undefined);
  const [warehouseId, setWarehouseId] = useState<string | undefined>(params.get('warehouseId') ?? undefined);
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([dayjs().startOf('month'), dayjs()]);
  const { data, isFetching } = useQuery({
    queryKey: ['stock-card', ingredientId, warehouseId, isoDate(range[0]), isoDate(range[1])], enabled: !!ingredientId,
    queryFn: async () => (await http.get<CardResult>('/stock/card', { params: { ingredientId, warehouseId, from: isoDate(range[0]), to: isoDate(range[1]) } })).data,
  });
  return (
    <Page title={t('card.title')}>
      <div className="bm-card bm-pad">
      <div className="bm-toolbar bm-no-print">
        <LookupSelect resource="ingredients" value={ingredientId} onChange={setIngredientId} placeholder={t('ingredient')} style={{ width: 300 }} data-testid="card-ingredient" />
        <LookupSelect resource="warehouses" value={warehouseId} onChange={setWarehouseId} placeholder={t('warehouse')} style={{ width: 200 }} />
        <DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} allowClear={false} />
      </div>
      {!ingredientId ? <Empty description={t('card.pick')} /> : (
        <>
          {data && (
            <Row gutter={16} style={{ marginBottom: 12 }}>
              <Col xs={12} md={6}><Statistic title={t('card.opening')} value={`${fmt.number(data.opening.quantity, 3)} ${data.ingredient.unit ?? ''}`} /></Col>
              <Col xs={12} md={6}><Statistic title={t('card.in')} value={fmt.number(data.totals.inQty, 3)} /></Col>
              <Col xs={12} md={6}><Statistic title={t('card.out')} value={fmt.number(data.totals.outQty, 3)} /></Col>
              <Col xs={12} md={6}><Statistic title={t('card.closing')} value={`${fmt.number(data.closing.quantity, 3)} · ${fmt.money(data.closing.value)}`} /></Col>
            </Row>
          )}
          <Table
            rowKey="id" loading={isFetching} dataSource={data?.rows} pagination={false} size="small" scroll={{ x: 'max-content' }}
            columns={[
              { title: t('docDate'), dataIndex: 'docDate', width: 110, render: fmt.date },
              { title: t('docNo'), dataIndex: 'docNo', width: 170, render: (v, r) => <Link to={r.docType === 'Receipt' ? `/stock/receipts/${r.docId}` : `/stock/issues/${r.docId}`}>{v}</Link> },
              { title: t('card.movement'), dataIndex: 'type', width: 150, render: (v) => enumLabel('LedgerType', v) },
              { title: t('lot'), dataIndex: 'lotNo', width: 110 },
              { title: t('card.in'), dataIndex: 'inQty', width: 100, align: 'right', render: (v) => (v ? fmt.number(v, 3) : '') },
              { title: t('card.out'), dataIndex: 'outQty', width: 100, align: 'right', render: (v) => (v ? fmt.number(v, 3) : '') },
              { title: t('unitPrice'), dataIndex: 'unitCost', width: 120, align: 'right', render: fmt.money },
              { title: t('amount'), dataIndex: 'amount', width: 130, align: 'right', render: fmt.money },
              { title: t('card.balanceQty'), dataIndex: 'balanceQty', width: 110, align: 'right', render: (v) => fmt.number(v, 3) },
              { title: t('balance.value'), dataIndex: 'balanceValue', width: 140, align: 'right', render: fmt.money },
            ]}
          />
        </>
      )}
      </div>
    </Page>
  );
}

// ================================================================== purchase requests

interface PrRow { id: string; docNo: string; docDate: string; needDate?: string; status: string; estimatedAmount: number; lineCount: number; menuCount: number; note?: string }
interface PrLine { id: string; ingredientCode: string; ingredientName: string; ingredientNameEn?: string; unit?: string; requiredQty: number; availableQty: number; minStock: number; suggestedQty: number; orderQty?: number | null; estimatedPrice?: number | null; amount: number; partnerId?: string | null; partner?: string }
interface PrDetail { id: string; docNo: string; docDate: string; needDate?: string; status: string; estimatedAmount: number; note?: string; version: number; menus: { id: string; code: string; menuDate: string; servings: number }[]; receipts: { id: string; docNo: string; status: string }[]; lines: PrLine[] }

export function PurchaseRequestsPage() {
  const { t } = useTranslation('stock');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState(false);
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([dayjs().add(1, 'day'), dayjs().add(7, 'day')]);
  const [warehouseId, setWarehouseId] = useState<string>();
  const { data, isFetching } = useQuery({ queryKey: ['purchase-requests', page], queryFn: async () => (await http.get<PagedResult<PrRow>>('/purchase-requests', { params: { page, pageSize: 30 } })).data, placeholderData: keepPreviousData });
  const generate = async () => {
    try {
      const r = await http.post<{ id: string }>('/purchase-requests/generate', { from: isoDate(range[0]), to: isoDate(range[1]), warehouseId: warehouseId ?? null }, { headers: { 'Idempotency-Key': idemKey() } });
      navigate(`/stock/purchase-requests/${r.data.id}`);
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  return (
    <Page title={t('purchase.title')} actions={has('purchase.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => setOpen(true)} data-testid="pr-generate">{t('purchase.generate')}</Button>}>
      <div className="bm-card">
      <Table<PrRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} onRow={(r) => ({ onClick: () => navigate(`/stock/purchase-requests/${r.id}`), style: { cursor: 'pointer' } })}
        pagination={{ current: page, pageSize: 30, total: data?.totalItems, onChange: setPage }}
        columns={[
          { title: t('docNo'), dataIndex: 'docNo', width: 170 },
          { title: t('docDate'), dataIndex: 'docDate', width: 110, render: fmt.date },
          { title: t('purchase.needDate'), dataIndex: 'needDate', width: 120, render: fmt.date },
          { title: t('purchase.menus'), dataIndex: 'menuCount', width: 100, align: 'right' },
          { title: t('lines'), dataIndex: 'lineCount', width: 90, align: 'right' },
          { title: t('purchase.estAmount'), dataIndex: 'estimatedAmount', width: 150, align: 'right', render: fmt.money },
          { title: t('status'), dataIndex: 'status', width: 130, render: (v) => <StatusTag enumName="PurchaseRequestStatus" status={v} /> },
        ]}
      />
      <Modal open={open} title={t('purchase.generate')} onCancel={() => setOpen(false)} onOk={() => void generate()} okButtonProps={{ 'data-testid': 'pr-generate-ok' } as never}>
        <Space direction="vertical" style={{ width: '100%' }}>
          <DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} style={{ width: '100%' }} />
          <LookupSelect resource="warehouses" value={warehouseId} onChange={setWarehouseId} placeholder={t('warehouse')} />
          <Typography.Text type="secondary">{t('purchase.formula')}</Typography.Text>
        </Space>
      </Modal>
      </div>
    </Page>
  );
}

export function PurchaseRequestDetailPage() {
  const { id } = useParams();
  const { t, i18n } = useTranslation('stock');
  const { message } = App.useApp();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const { has } = usePermission();
  const { data } = useQuery({ queryKey: ['purchase-request', id], queryFn: async () => (await http.get<PrDetail>(`/purchase-requests/${id}`)).data });
  const [lines, setLines] = useState<PrLine[]>([]);
  useEffect(() => { if (data) setLines(data.lines); }, [data]);
  if (!data) return <Card loading />;
  const editable = (data.status === 'Draft' || data.status === 'Submitted') && has('purchase.edit');
  const patch = (lid: string, p: Partial<PrLine>) => setLines((ls) => ls.map((l) => (l.id === lid ? { ...l, ...p } : l)));
  const reload = () => qc.invalidateQueries({ queryKey: ['purchase-request', id] });
  const save = async () => {
    try {
      await http.put(`/purchase-requests/${id}`, { needDate: data.needDate ?? null, note: data.note ?? null, version: data.version, lines: lines.map((l) => ({ id: l.id, orderQty: l.orderQty ?? null, estimatedPrice: l.estimatedPrice ?? null, partnerId: l.partnerId ?? null })) });
      message.success(t('common:saved'));
      await reload();
      return true;
    } catch (e) {
      message.error(problemMessage(e));
      return false;
    }
  };
  const act = async (action: string) => {
    try {
      if (editable && !(await save())) return;
      if (action === 'receipts') {
        const r = await http.post<{ id: string }[]>(`/purchase-requests/${id}/receipts`, null, { headers: { 'Idempotency-Key': idemKey() } });
        message.success(t('purchase.receiptsCreated', { n: r.data.length }));
      } else {
        await http.post(`/purchase-requests/${id}/${action}`);
      }
      await reload();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const total = lines.reduce((s, l) => s + (l.orderQty ?? 0) * (l.estimatedPrice ?? 0), 0);
  return (
    <Page
      title={`${t('purchase.title')} ${data.docNo}`}
      tag={<StatusTag enumName="PurchaseRequestStatus" status={data.status} />}
      actions={(
        <>
            <Button onClick={() => navigate('/stock/purchase-requests')}>{t('common:actions.back')}</Button>
            <Button onClick={() => window.print()}>{t('print')}</Button>
            {editable && <Button icon={<SaveOutlined />} onClick={() => void save()}>{t('common:actions.save')}</Button>}
            {data.status === 'Draft' && has('purchase.edit') && <Button onClick={() => void act('submit')}>{t('purchase.submit')}</Button>}
            {['Draft', 'Submitted'].includes(data.status) && has('purchase.approve') && <Button type="primary" onClick={() => void act('approve')} data-testid="pr-approve">{t('purchase.approve')}</Button>}
            {['Approved', 'Ordered'].includes(data.status) && has('stock.receipt.create') && <Button type="primary" onClick={() => void act('receipts')} data-testid="pr-receipts">{t('purchase.toReceipts')}</Button>}
            {['Draft', 'Submitted', 'Approved'].includes(data.status) && has('purchase.edit') && <Popconfirm title={t('purchase.cancel')} onConfirm={() => void act('cancel')}><Button danger>{t('purchase.cancel')}</Button></Popconfirm>}
        </>
      )}
      sub={t('purchase.formula')}
    >
      <div className="bm-card bm-pad">
        <InfoGrid items={[
          { key: 'd', label: t('docDate'), value: fmt.date(data.docDate) },
          { key: 'n', label: t('purchase.needDate'), value: fmt.date(data.needDate) },
          { key: 'a', label: t('purchase.estAmount'), value: fmt.money(total) },
          { key: 'm', label: t('purchase.menus'), value: <span className="bm-dchips">{data.menus.map((m) => <a key={m.id} className="bm-dchip" onClick={() => navigate(`/menus/${m.id}`)}>{m.code}</a>)}</span> },
          ...(data.receipts.length > 0 ? [{ key: 'r', label: t('receipts.title'), value: <span className="bm-dchips">{data.receipts.map((r) => <a key={r.id} className="bm-dchip" onClick={() => navigate(`/stock/receipts/${r.id}`)}>{r.docNo}</a>)}</span> }] : []),
        ]} />
      </div>
      <div className="bm-card">
        <Table<PrLine>
          rowKey="id" dataSource={lines} pagination={false} size="small" scroll={{ x: 'max-content' }}
          columns={[
            { title: t('ingredient'), key: 'i', render: (_, l) => ingName(l, i18n.language) },
            { title: t('unit'), dataIndex: 'unit', width: 70 },
            { title: t('purchase.required'), dataIndex: 'requiredQty', width: 110, align: 'right', render: (v) => fmt.number(v, 3) },
            { title: t('available'), dataIndex: 'availableQty', width: 110, align: 'right', render: (v) => fmt.number(v, 3) },
            { title: t('purchase.minStock'), dataIndex: 'minStock', width: 110, align: 'right', render: (v) => fmt.number(v, 3) },
            { title: t('purchase.suggested'), dataIndex: 'suggestedQty', width: 110, align: 'right', render: (v) => <b>{fmt.number(v, 3)}</b> },
            { title: t('purchase.order'), key: 'o', width: 120, align: 'right', render: (_, l) => (editable ? <InputNumber size="small" min={0} value={l.orderQty ?? undefined} onChange={(v) => patch(l.id, { orderQty: v ?? null })} /> : fmt.number(l.orderQty, 3)) },
            { title: t('purchase.estPrice'), key: 'p', width: 130, align: 'right', render: (_, l) => (editable ? <InputNumber size="small" min={0} step={1000} value={l.estimatedPrice ?? undefined} onChange={(v) => patch(l.id, { estimatedPrice: v ?? null })} /> : fmt.money(l.estimatedPrice)) },
            { title: t('amount'), key: 'a', width: 130, align: 'right', render: (_, l) => fmt.money((l.orderQty ?? 0) * (l.estimatedPrice ?? 0)) },
            { title: t('partner'), key: 'pt', width: 220, render: (_, l) => (editable ? <LookupSelect resource="partners" value={l.partnerId} onChange={(v) => patch(l.id, { partnerId: v ?? null })} /> : l.partner) },
          ]}
        />
        <Totals items={[{ label: t('purchase.estAmount'), value: fmt.money(total) }]} />
      </div>
    </Page>
  );
}

// ================================================================== period locks

interface LockRow { id: string; warehouseId?: string; warehouse?: string; period: string; lockedAt: string; lockedBy?: string }

export function PeriodLocksPage() {
  const { t } = useTranslation('stock');
  const { message } = App.useApp();
  const [warehouseId, setWarehouseId] = useState<string>();
  const [period, setPeriod] = useState<dayjs.Dayjs | null>(dayjs().subtract(1, 'month'));
  const { data, refetch, isFetching } = useQuery({ queryKey: ['period-locks'], queryFn: async () => (await http.get<LockRow[]>('/stock/period-locks')).data });
  const lock = async () => {
    try {
      await http.post('/stock/period-locks', { warehouseId: warehouseId ?? null, period: isoDate(period?.startOf('month')) });
      message.success(t('locks.lock'));
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const unlock = async (id: string) => { try { await http.delete(`/stock/period-locks/${id}`); void refetch(); } catch (e) { message.error(problemMessage(e)); } };
  return (
    <Page title={t('locks.title')} sub={t('locks.hint')}>
      <div className="bm-card">
      <div className="bm-toolbar">
        <LookupSelect resource="warehouses" value={warehouseId} onChange={setWarehouseId} placeholder={t('locks.allWarehouses')} style={{ width: 220 }} />
        <DatePicker picker="month" value={period} onChange={setPeriod} format="MM/YYYY" />
        <Button type="primary" icon={<LockOutlined />} disabled={!period} onClick={() => void lock()}>{t('locks.lock')}</Button>
      </div>
      <Table<LockRow>
        rowKey="id" loading={isFetching} dataSource={data} pagination={false}
        columns={[
          { title: t('locks.period'), dataIndex: 'period', render: (v) => dayjs(v).format('MM/YYYY') },
          { title: t('warehouse'), dataIndex: 'warehouse', render: (v) => v ?? t('locks.allWarehouses') },
          { title: t('locks.lockedAt'), dataIndex: 'lockedAt', render: fmt.dateTime },
          { title: t('locks.lockedBy'), dataIndex: 'lockedBy' },
          { key: 'a', width: 120, render: (_, r) => <Popconfirm title={t('locks.unlock')} onConfirm={() => void unlock(r.id)}><Button size="small" icon={<UnlockOutlined />}>{t('locks.unlock')}</Button></Popconfirm> },
        ]}
      />
      </div>
    </Page>
  );
}
