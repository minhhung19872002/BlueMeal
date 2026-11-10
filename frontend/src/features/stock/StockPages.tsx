import { useEffect, useRef, useState } from 'react';
import { App, Button, Card, Checkbox, DatePicker, Empty, Form, Grid, Input, InputNumber, Modal, Popconfirm, Space, Table, Tabs, Typography } from 'antd';
import { CheckOutlined, LockOutlined, PlusOutlined, SaveOutlined, UnlockOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';
import { InfoGrid, LineCell, LineRow, Page, Section, Totals } from '@/shared/components/PageChrome';
import { Tile } from '@/app/layout/designKit';
import { DocCards, idemKey, ingName, qtyText, StatusTag } from './shared';

/** Signed quantity: +1,5 / -2 / 0. */
const signed = (v: number) => (v > 0 ? '+' : '') + qtyText(v);

// ================================================================== stock takes

interface TakeRow { id: string; docNo: string; takeDate: string; warehouse: string; status: string; members?: string; lineCount: number; counted: number; differences: number }
interface TakeLine { id: string; ingredientId: string; ingredientCode: string; ingredientName: string; ingredientNameEn?: string; unit?: string; lotNo?: string; bookQty: number; actualQty?: number | null; difference: number; unitCost: number; cause?: string; resolution?: string }
interface TakeDetail { id: string; docNo: string; takeDate: string; warehouseId: string; warehouse?: string; status: string; members?: string; note?: string; adjustDocs: string[]; version: number; lines: TakeLine[] }

export function StockTakesPage() {
  const { t } = useTranslation('stock');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const screens = Grid.useBreakpoint();
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
      {!screens.lg ? (
        <DocCards
          loading={isFetching && !data} items={data?.items} total={data?.totalItems ?? 0} page={page} pageSize={30} onPage={setPage}
          render={(r) => ({
            key: r.id, onClick: () => navigate(`/stock/takes/${r.id}`), title: r.docNo, sub: fmt.date(r.takeDate), tag: <StatusTag enumName="StockTakeStatus" status={r.status} />,
            meta: [{ label: t('warehouse'), value: r.warehouse }, { label: t('takes.counted'), value: `${r.counted}/${r.lineCount}` }, { label: t('takes.diff'), value: r.differences }],
          })}
        />
      ) : (
      <Table<TakeRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} onRow={(r) => ({ onClick: () => navigate(`/stock/takes/${r.id}`), style: { cursor: 'pointer' } })}
        pagination={{ current: page, pageSize: 30, total: data?.totalItems, onChange: setPage, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('docNo'), dataIndex: 'docNo', width: 170, render: (v) => <b style={{ fontWeight: 600 }}>{v}</b> },
          { title: t('docDate'), dataIndex: 'takeDate', width: 110, render: fmt.date },
          { title: t('warehouse'), dataIndex: 'warehouse' },
          { title: t('takes.counted'), key: 'c', width: 110, align: 'right', render: (_, r) => `${r.counted}/${r.lineCount}` },
          { title: t('takes.diff'), dataIndex: 'differences', width: 100, align: 'right', render: (v) => <span style={{ fontWeight: v ? 600 : undefined, color: v ? 'var(--danger-text)' : undefined }}>{v}</span> },
          { title: t('status'), dataIndex: 'status', width: 130, render: (v) => <StatusTag enumName="StockTakeStatus" status={v} /> },
        ]}
      />
      )}
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
  const diffLines = lines.filter((l) => l.actualQty != null && l.difference !== 0);
  const diffValue = diffLines.reduce((sum, l) => sum + l.difference * l.unitCost, 0);
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
      <div className="bm-x-tiles">
        <Tile label={t('takes.counted')} value={`${counted}/${lines.length}`} />
        <Tile label={t('takes.lineDiff')} value={diffLines.length} bad={diffLines.length > 0} />
        <Tile label={t('takes.diffValue')} value={fmt.money(diffValue)} bad={diffValue < 0} />
      </div>
      <div className="bm-card bm-pad bm-stack">
        <InfoGrid items={[
          { key: 'w', label: t('warehouse'), value: data.warehouse },
          { key: 'd', label: t('docDate'), value: fmt.date(data.takeDate) },
          ...(data.adjustDocs.length > 0 ? [{ key: 'a', label: t('takes.adjustDocs'), value: data.adjustDocs.join(', ') }] : []),
        ]} />
        {editable ? <Input.TextArea rows={1} value={members} onChange={(e) => setMembers(e.target.value)} placeholder={t('takes.members')} maxLength={1000} /> : data.members && <div className="bm-info-l">{t('takes.members')}: <span style={{ color: 'var(--ink)' }}>{data.members}</span></div>}
      </div>
      <Section title={t('lineItems')} extra={editable && (
        <Space wrap>
          <LookupSelect resource="ingredients" value={extra.ingredientId} onChange={(v) => setExtra({ ...extra, ingredientId: v })} placeholder={t('takes.addItem')} style={{ width: screens.md ? 240 : 220 }} />
          <InputNumber min={0} value={extra.actualQty} onChange={(v) => setExtra({ ...extra, actualQty: v ?? undefined })} placeholder={t('takes.actual')} />
          <Button icon={<PlusOutlined />} disabled={!extra.ingredientId || extra.actualQty == null} onClick={() => void save([{ ingredientId: extra.ingredientId!, actualQty: extra.actualQty! }]).then((ok) => ok && setExtra({}))} />
        </Space>
      )}>
        {!screens.lg ? (
          <div className="bm-lines">
            {lines.map((l) => (
              <LineRow key={l.id}>
                <LineCell label={t('ingredient')} flex="1 1 100%" static><span className="bm-take-ing">{ingName(l, i18n.language)}{l.lotNo && <span className="bm-take-lot"> · {l.lotNo}</span>}</span></LineCell>
                <LineCell label={t('takes.book')} flex="1 1 90px" static>{qtyText(l.bookQty)} {l.unit ?? ''}</LineCell>
                <LineCell label={t('takes.actual')} flex="1 1 110px" static={!editable}>
                  {editable ? <InputNumber min={0} value={l.actualQty ?? undefined} onChange={(v) => patch(l.id, { actualQty: v ?? null })} inputMode="decimal" data-testid="take-actual" /> : qtyText(l.actualQty)}
                </LineCell>
                <LineCell label={t('takes.diff')} flex="1 1 80px" static color={l.difference ? 'var(--danger-text)' : undefined}>{signed(l.difference)}</LineCell>
                {(editable || l.cause) && (
                  <LineCell label={t('takes.cause')} flex="1 1 100%" static={!editable}>
                    {editable ? <Input maxLength={500} value={l.cause} onChange={(e) => patch(l.id, { cause: e.target.value })} /> : l.cause}
                  </LineCell>
                )}
              </LineRow>
            ))}
          </div>
        ) : (
        <Table<TakeLine>
          rowKey="id" dataSource={lines} pagination={false} size="small" scroll={{ x: 'max-content' }}
          rowClassName={(l) => (l.actualQty != null && l.difference !== 0 ? 'bm-row-diff' : '')}
          columns={[
            { title: t('ingredient'), key: 'ing', render: (_, l) => <>{ingName(l, i18n.language)}{l.lotNo && <Typography.Text type="secondary"> · {l.lotNo}</Typography.Text>}</> },
            { title: t('takes.book'), dataIndex: 'bookQty', width: 110, align: 'right', render: (v, l) => `${fmt.number(v, 3)} ${l.unit ?? ''}` },
            { title: t('takes.actual'), key: 'a', width: 130, align: 'right', render: (_, l) => (editable
              ? <InputNumber size={screens.md ? 'small' : 'middle'} min={0} value={l.actualQty ?? undefined} onChange={(v) => patch(l.id, { actualQty: v ?? null })} inputMode="decimal" data-testid="take-actual" />
              : fmt.number(l.actualQty, 3)) },
            { title: t('takes.diff'), key: 'd', width: 100, align: 'right', render: (_, l) => <span style={{ fontWeight: 600, color: l.difference ? 'var(--danger-text)' : undefined }}>{signed(l.difference)}</span> },
            { title: t('takes.cause'), key: 'c', width: 200, render: (_, l) => (editable ? <Input size="small" maxLength={500} value={l.cause} onChange={(e) => patch(l.id, { cause: e.target.value })} /> : l.cause) },
            { title: t('takes.resolution'), key: 'r', width: 200, responsive: ['lg'], render: (_, l) => (editable ? <Input size="small" maxLength={500} value={l.resolution} onChange={(e) => patch(l.id, { resolution: e.target.value })} /> : l.resolution) },
          ]}
        />
        )}
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
  const isMobile = !screens.lg;
  const today = dayjs().startOf('day');
  /** Design state tag: expired / expiring (≤ 3 days) / below minimum / OK. */
  const state = (r: BalanceRow) => {
    const left = r.nearestExpiry ? dayjs(r.nearestExpiry).diff(today, 'day') : null;
    if (left != null && left < 0) return { cls: 't-red', label: t('balance.expired') };
    if (left != null && left <= 3) return { cls: 't-orange', label: t('balance.soon') };
    if (r.belowMin) return { cls: 't-orange', label: t('balance.belowMin') };
    return { cls: 't-green', label: t('balance.ok') };
  };
  const stateTag = (r: BalanceRow) => { const s = state(r); return <span className={`bm-tag ${s.cls}`}>{s.label}</span>; };
  const lotTone = (v?: number | null) => (v == null ? '' : v < 0 ? 't-red' : v <= 3 ? 't-orange' : 't-neutral');
  const cardLink = (r: BalanceRow) => <Link to={`/stock/card?ingredientId=${r.ingredientId}&warehouseId=${r.warehouseId}`}>{t('balance.card')}</Link>;
  const expiringLots = (lots.data ?? []).filter((l) => l.daysLeft != null);
  return (
    <Page title={t('balance.title')}>
      <div className="bm-x-tiles">
        <Tile label={t('balance.totalValue')} value={fmt.money(data?.totalValue ?? 0)} />
        <Tile label={t('balance.items')} value={fmt.number(data?.totalItems ?? 0)} />
        <Tile label={t('balance.lots')} value={expiringLots.length} extra={days != null ? `${t('balance.expiring')}: ${days}` : undefined} bad={expiringLots.some((l) => (l.daysLeft ?? 99) <= 3)} />
      </div>
      <div className="bm-card">
      <div className="bm-toolbar">
        <LookupSelect resource="warehouses" placeholder={t('warehouse')} value={warehouseId} onChange={(v) => { setWarehouseId(v); setPage(1); }} style={{ width: screens.md ? 200 : '100%' }} />
        <DatePicker placeholder={t('balance.atDate')} value={date} onChange={(v) => { setDate(v); setPage(1); }} format={DATE_FORMAT} style={{ width: screens.md ? undefined : '100%' }} />
        <Input.Search allowClear placeholder={t('ingredient')} onSearch={(v) => { setQ(v); setPage(1); }} style={{ width: screens.md ? 220 : '100%' }} />
        <Checkbox checked={belowMin} onChange={(e) => { setBelowMin(e.target.checked); setPage(1); }}>{t('balance.belowMin')}</Checkbox>
      </div>
      <Tabs className="bm-pilltabs" items={[
        {
          key: 'balance', label: t('balance.title'),
          children: isMobile ? (
            <DocCards
              loading={isFetching && !data} items={data?.items} total={data?.totalItems ?? 0} page={page} onPage={setPage}
              render={(r) => ({
                key: r.warehouseId + r.ingredientId, title: ingName(r, i18n.language), sub: r.warehouse, tag: stateTag(r),
                meta: [
                  { label: t('quantity'), value: <span style={{ color: r.belowMin ? 'var(--danger-text)' : undefined }}>{qtyText(r.quantity)} {r.unit}</span> },
                  { label: t('balance.value'), value: fmt.money(r.value) },
                  { label: t('balance.min'), value: qtyText(r.minStock) },
                  { label: t('balance.nearestExpiry'), value: fmt.date(r.nearestExpiry) || '—' },
                ],
                actions: cardLink(r),
              })}
            />
          ) : (
            <Table<BalanceRow>
              rowKey={(r) => r.warehouseId + r.ingredientId} loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} size="middle"
              pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
              columns={[
                { title: t('ingredient'), key: 'i', render: (_, r) => <span style={{ fontWeight: 500 }}>{ingName(r, i18n.language)}</span> },
                { title: t('warehouse'), dataIndex: 'warehouse', width: 150 },
                { title: t('quantity'), key: 'q', align: 'right', width: 130, render: (_, r) => <span style={{ color: r.belowMin ? 'var(--danger-text)' : undefined, fontWeight: 600 }}>{qtyText(r.quantity)} {r.unit}</span> },
                { title: t('balance.avg'), dataIndex: 'averageCost', align: 'right', width: 130, render: fmt.money },
                { title: t('balance.value'), dataIndex: 'value', align: 'right', width: 140, render: fmt.money },
                { title: t('balance.min'), dataIndex: 'minStock', align: 'right', width: 110, render: (v) => qtyText(v) },
                { title: t('balance.nearestExpiry'), dataIndex: 'nearestExpiry', width: 120, render: fmt.date },
                { title: t('balance.state'), key: 's', width: 140, render: (_, r) => stateTag(r) },
                { key: 'card', width: 90, render: (_, r) => cardLink(r) },
              ]}
            />
          ),
        },
        {
          key: 'lots', label: t('balance.lots'),
          children: (
            <>
              <div className="bm-toolbar"><span>{t('balance.expiring')}</span><InputNumber min={0} value={days ?? undefined} onChange={(v) => setDays(v ?? null)} /></div>
              {isMobile ? (
                <DocCards
                  loading={lots.isFetching && !lots.data} items={lots.data} total={lots.data?.length ?? 0} page={1} pageSize={Math.max(1, lots.data?.length ?? 1)} onPage={() => undefined}
                  render={(r) => ({
                    key: r.id, title: ingName(r, i18n.language), sub: `${r.warehouse} · ${t('lot')} ${r.lotNo ?? '—'}`,
                    tag: r.daysLeft != null ? <span className={`bm-tag ${lotTone(r.daysLeft)}`}>{r.daysLeft}</span> : undefined,
                    meta: [{ label: t('expiry'), value: fmt.date(r.expiryDate) || '—' }, { label: t('quantity'), value: `${qtyText(r.quantity)} ${r.unit ?? ''}` }, { label: t('unitPrice'), value: fmt.money(r.unitCost) }],
                  })}
                />
              ) : (
                <Table<LotRow>
                  rowKey="id" loading={lots.isFetching} dataSource={lots.data} scroll={{ x: 'max-content' }} size="small" pagination={{ pageSize: 50 }}
                  columns={[
                    { title: t('ingredient'), key: 'i', render: (_, r) => ingName(r, i18n.language) },
                    { title: t('warehouse'), dataIndex: 'warehouse', width: 150 },
                    { title: t('lot'), dataIndex: 'lotNo', width: 120 },
                    { title: t('expiry'), dataIndex: 'expiryDate', width: 110, render: fmt.date },
                    { title: t('balance.daysLeft'), dataIndex: 'daysLeft', width: 100, align: 'right', render: (v) => (v == null ? '' : <span className={`bm-tag ${lotTone(v)}`}>{v}</span>) },
                    { title: t('quantity'), key: 'q', width: 120, align: 'right', render: (_, r) => `${qtyText(r.quantity)} ${r.unit ?? ''}` },
                    { title: t('unitPrice'), dataIndex: 'unitCost', width: 120, align: 'right', render: fmt.money },
                  ]}
                />
              )}
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
  const screens = Grid.useBreakpoint();
  const unit = data?.ingredient.unit ?? '';
  const docLink = (r: CardResult['rows'][number]) => <Link to={r.docType === 'Receipt' ? `/stock/receipts/${r.docId}` : `/stock/issues/${r.docId}`}>{r.docNo}</Link>;
  return (
    <Page title={t('card.title')} sub={data ? `${data.ingredient.code} — ${data.ingredient.name}` : undefined}>
      <div className="bm-card">
      <div className="bm-toolbar bm-no-print">
        <LookupSelect resource="ingredients" value={ingredientId} onChange={setIngredientId} placeholder={t('ingredient')} style={{ width: screens.md ? 300 : '100%' }} data-testid="card-ingredient" />
        <LookupSelect resource="warehouses" value={warehouseId} onChange={setWarehouseId} placeholder={t('warehouse')} style={{ width: screens.md ? 200 : '100%' }} />
        <DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v as never)} format={DATE_FORMAT} allowClear={false} style={{ width: screens.md ? undefined : '100%' }} />
      </div>
      {!ingredientId ? <div className="bm-pad"><Empty description={t('card.pick')} /></div> : (
        <>
          {data && (
            <div className="bm-x-tiles bm-pad">
              <Tile label={t('card.opening')} value={`${qtyText(data.opening.quantity)} ${unit}`} extra={fmt.money(data.opening.value)} />
              <Tile label={t('card.in')} value={qtyText(data.totals.inQty)} extra={unit} />
              <Tile label={t('card.out')} value={qtyText(data.totals.outQty)} extra={unit} />
              <Tile label={t('card.closing')} value={`${qtyText(data.closing.quantity)} ${unit}`} extra={fmt.money(data.closing.value)} />
            </div>
          )}
          {!screens.lg ? (
            <DocCards
              loading={isFetching && !data} items={data?.rows} total={data?.rows.length ?? 0} page={1} pageSize={Math.max(1, data?.rows.length ?? 1)} onPage={() => undefined}
              render={(r) => ({
                key: r.id, title: docLink(r), sub: `${fmt.date(r.docDate)} · ${enumLabel('LedgerType', r.type)}`,
                tag: <span className={`bm-tag ${r.inQty ? 't-green' : 't-orange'}`}>{r.inQty ? `+${qtyText(r.inQty)}` : `−${qtyText(r.outQty)}`}</span>,
                meta: [{ label: t('lot'), value: r.lotNo ?? '—' }, { label: t('amount'), value: fmt.money(r.amount) }, { label: t('card.balanceQty'), value: `${qtyText(r.balanceQty)} ${unit}` }],
              })}
            />
          ) : (
          <Table
            rowKey="id" loading={isFetching} dataSource={data?.rows} pagination={false} size="small" scroll={{ x: 'max-content' }}
            columns={[
              { title: t('docDate'), dataIndex: 'docDate', width: 110, render: fmt.date },
              { title: t('docNo'), dataIndex: 'docNo', width: 170, render: (_, r) => docLink(r) },
              { title: t('card.movement'), dataIndex: 'type', width: 150, render: (v) => enumLabel('LedgerType', v) },
              { title: t('lot'), dataIndex: 'lotNo', width: 110 },
              { title: t('card.in'), dataIndex: 'inQty', width: 100, align: 'right', render: (v) => (v ? qtyText(v) : '') },
              { title: t('card.out'), dataIndex: 'outQty', width: 100, align: 'right', render: (v) => (v ? qtyText(v) : '') },
              { title: t('unitPrice'), dataIndex: 'unitCost', width: 120, align: 'right', render: fmt.money },
              { title: t('amount'), dataIndex: 'amount', width: 130, align: 'right', render: fmt.money },
              { title: t('card.balanceQty'), dataIndex: 'balanceQty', width: 110, align: 'right', render: (v) => <b style={{ fontWeight: 600 }}>{qtyText(v)}</b> },
              { title: t('balance.value'), dataIndex: 'balanceValue', width: 140, align: 'right', render: fmt.money },
            ]}
          />
          )}
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
  const screens = Grid.useBreakpoint();
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
      {!screens.lg ? (
        <DocCards
          loading={isFetching && !data} items={data?.items} total={data?.totalItems ?? 0} page={page} pageSize={30} onPage={setPage}
          render={(r) => ({
            key: r.id, onClick: () => navigate(`/stock/purchase-requests/${r.id}`), title: r.docNo, sub: fmt.date(r.docDate), tag: <StatusTag enumName="PurchaseRequestStatus" status={r.status} />,
            meta: [
              { label: t('purchase.needDate'), value: fmt.date(r.needDate) || '—' }, { label: t('purchase.menus'), value: r.menuCount },
              { label: t('lines'), value: r.lineCount }, { label: t('purchase.estAmount'), value: fmt.money(r.estimatedAmount) },
            ],
          })}
        />
      ) : (
      <Table<PrRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} onRow={(r) => ({ onClick: () => navigate(`/stock/purchase-requests/${r.id}`), style: { cursor: 'pointer' } })}
        pagination={{ current: page, pageSize: 30, total: data?.totalItems, onChange: setPage, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('docNo'), dataIndex: 'docNo', width: 170, render: (v) => <b style={{ fontWeight: 600 }}>{v}</b> },
          { title: t('docDate'), dataIndex: 'docDate', width: 110, render: fmt.date },
          { title: t('purchase.needDate'), dataIndex: 'needDate', width: 120, render: fmt.date },
          { title: t('purchase.menus'), dataIndex: 'menuCount', width: 100, align: 'right' },
          { title: t('lines'), dataIndex: 'lineCount', width: 90, align: 'right' },
          { title: t('purchase.estAmount'), dataIndex: 'estimatedAmount', width: 150, align: 'right', render: fmt.money },
          { title: t('status'), dataIndex: 'status', width: 130, render: (v) => <StatusTag enumName="PurchaseRequestStatus" status={v} /> },
        ]}
      />
      )}
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
  const screens = Grid.useBreakpoint();
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
      <Section title={t('lineItems')} hint={t('purchase.formula')}>
        {!screens.lg ? (
          <div className="bm-lines">
            {lines.map((l) => (
              <LineRow key={l.id}>
                <LineCell label={t('ingredient')} flex="1 1 100%" static><span className="bm-take-ing">{ingName(l, i18n.language)}</span></LineCell>
                <LineCell label={t('purchase.required')} flex="1 1 80px" static>{qtyText(l.requiredQty)} {l.unit ?? ''}</LineCell>
                <LineCell label={t('available')} flex="1 1 80px" static>{qtyText(l.availableQty)}</LineCell>
                <LineCell label={t('purchase.suggested')} flex="1 1 80px" static color="var(--primary-text)">{qtyText(l.suggestedQty)}</LineCell>
                <LineCell label={t('purchase.order')} flex="1 1 100px" static={!editable}>
                  {editable ? <InputNumber min={0} value={l.orderQty ?? undefined} onChange={(v) => patch(l.id, { orderQty: v ?? null })} /> : qtyText(l.orderQty)}
                </LineCell>
                <LineCell label={t('purchase.estPrice')} flex="1 1 120px" static={!editable}>
                  {editable ? <InputNumber min={0} step={1000} value={l.estimatedPrice ?? undefined} onChange={(v) => patch(l.id, { estimatedPrice: v ?? null })} /> : fmt.money(l.estimatedPrice)}
                </LineCell>
                <LineCell label={t('amount')} flex="1 1 110px" static>{fmt.money((l.orderQty ?? 0) * (l.estimatedPrice ?? 0))}</LineCell>
                <LineCell label={t('partner')} flex="1 1 100%" static={!editable}>
                  {editable ? <LookupSelect resource="partners" value={l.partnerId} onChange={(v) => patch(l.id, { partnerId: v ?? null })} /> : (l.partner ?? '—')}
                </LineCell>
              </LineRow>
            ))}
          </div>
        ) : (
        <Table<PrLine>
          rowKey="id" dataSource={lines} pagination={false} size="small" scroll={{ x: 'max-content' }}
          columns={[
            { title: t('ingredient'), key: 'i', render: (_, l) => ingName(l, i18n.language) },
            { title: t('unit'), dataIndex: 'unit', width: 70 },
            { title: t('purchase.required'), dataIndex: 'requiredQty', width: 110, align: 'right', render: (v) => qtyText(v) },
            { title: t('available'), dataIndex: 'availableQty', width: 110, align: 'right', render: (v) => qtyText(v) },
            { title: t('purchase.minStock'), dataIndex: 'minStock', width: 110, align: 'right', render: (v) => qtyText(v) },
            { title: t('purchase.suggested'), dataIndex: 'suggestedQty', width: 110, align: 'right', render: (v) => <b style={{ color: 'var(--primary-text)' }}>{qtyText(v)}</b> },
            { title: t('purchase.order'), key: 'o', width: 120, align: 'right', render: (_, l) => (editable ? <InputNumber size="small" min={0} value={l.orderQty ?? undefined} onChange={(v) => patch(l.id, { orderQty: v ?? null })} /> : fmt.number(l.orderQty, 3)) },
            { title: t('purchase.estPrice'), key: 'p', width: 130, align: 'right', render: (_, l) => (editable ? <InputNumber size="small" min={0} step={1000} value={l.estimatedPrice ?? undefined} onChange={(v) => patch(l.id, { estimatedPrice: v ?? null })} /> : fmt.money(l.estimatedPrice)) },
            { title: t('amount'), key: 'a', width: 130, align: 'right', render: (_, l) => fmt.money((l.orderQty ?? 0) * (l.estimatedPrice ?? 0)) },
            { title: t('partner'), key: 'pt', width: 220, render: (_, l) => (editable ? <LookupSelect resource="partners" value={l.partnerId} onChange={(v) => patch(l.id, { partnerId: v ?? null })} /> : l.partner) },
          ]}
        />
        )}
        <Totals items={[{ label: t('lines'), value: lines.length }, { label: t('purchase.estAmount'), value: fmt.money(total) }]} />
      </Section>
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
  const screens = Grid.useBreakpoint();
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
      <Section title={t('locks.new')}>
        <div className="bm-toolbar bm-locks-form">
          <LookupSelect resource="warehouses" value={warehouseId} onChange={setWarehouseId} placeholder={t('locks.allWarehouses')} style={{ width: screens.md ? 220 : '100%' }} />
          <DatePicker picker="month" value={period} onChange={setPeriod} format="MM/YYYY" style={{ width: screens.md ? 160 : '100%' }} />
          <Button type="primary" icon={<LockOutlined />} disabled={!period} onClick={() => void lock()}>{t('locks.lock')}</Button>
        </div>
      </Section>
      <div className="bm-card">
      {!screens.lg ? (
        <DocCards
          loading={isFetching && !data} items={data} total={data?.length ?? 0} page={1} pageSize={Math.max(1, data?.length ?? 1)} onPage={() => undefined}
          render={(r) => ({
            key: r.id, title: dayjs(r.period).format('MM/YYYY'), sub: r.warehouse ?? t('locks.allWarehouses'), tag: <span className="bm-tag t-red">{t('locks.locked')}</span>,
            meta: [{ label: t('locks.lockedAt'), value: fmt.dateTime(r.lockedAt) }, { label: t('locks.lockedBy'), value: r.lockedBy ?? '—' }],
            actions: <Popconfirm title={t('locks.unlock')} onConfirm={() => void unlock(r.id)}><Button size="small" icon={<UnlockOutlined />}>{t('locks.unlock')}</Button></Popconfirm>,
          })}
        />
      ) : (
      <Table<LockRow>
        rowKey="id" loading={isFetching} dataSource={data} pagination={false}
        columns={[
          { title: t('locks.period'), dataIndex: 'period', render: (v) => <b style={{ fontWeight: 600 }}>{dayjs(v).format('MM/YYYY')}</b> },
          { title: t('warehouse'), dataIndex: 'warehouse', render: (v) => v ?? t('locks.allWarehouses') },
          { title: t('locks.lockedAt'), dataIndex: 'lockedAt', render: fmt.dateTime },
          { title: t('locks.lockedBy'), dataIndex: 'lockedBy' },
          { title: t('status'), key: 's', width: 120, render: () => <span className="bm-tag t-red">{t('locks.locked')}</span> },
          { key: 'a', width: 120, render: (_, r) => <Popconfirm title={t('locks.unlock')} onConfirm={() => void unlock(r.id)}><Button size="small" icon={<UnlockOutlined />}>{t('locks.unlock')}</Button></Popconfirm> },
        ]}
      />
      )}
      </div>
    </Page>
  );
}
