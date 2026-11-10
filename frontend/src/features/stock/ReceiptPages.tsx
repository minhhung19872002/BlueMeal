import { useEffect, useMemo, useState } from 'react';
import { App, Button, DatePicker, Form, Grid, Input, InputNumber, Select, Table, Typography } from 'antd';
import { BarcodeOutlined, CheckOutlined, DeleteOutlined, PlusOutlined, PrinterOutlined, SaveOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel, type LookupItem } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';
import { InfoGrid, LineCell, LineRow, Page, Section, Totals } from '@/shared/components/PageChrome';
import { DocCards, idemKey, ingName, QcTag, qtyText, StatusTag } from './shared';

interface ReceiptRow { id: string; docNo: string; docDate: string; warehouse: string; partner?: string; invoiceNo?: string; status: string; totalAmount: number; lineCount: number; failed: number }
interface ReceiptLine {
  key: string; ingredientId?: string; ingredientCode?: string; ingredientName?: string; ingredientNameEn?: string; unit?: string; quantity: number; unitPrice: number; lotNo?: string;
  mfgDate?: string | null; expiryDate?: string | null; qualityResult?: string | null; receivedTemp?: number | null; note?: string;
}
interface ReceiptDetail {
  id: string; docNo: string; docDate: string; warehouseId: string; warehouse?: string; partnerId?: string; partner?: { code: string; nameVi: string; address?: string } | null; invoiceNo?: string;
  invoiceDate?: string; status: string; totalAmount: number; note?: string; version: number; purchaseRequestId?: string; lines: (Omit<ReceiptLine, 'key'> & { id: string; amount: number })[];
}

let seq = 0;
const k = () => `r${++seq}`;

/** Stock receipts list (SPEC §7.8). */
export function ReceiptsPage() {
  const { t } = useTranslation('stock');
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs] | null>([dayjs().subtract(30, 'day'), dayjs().add(7, 'day')]);
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const params = { page, pageSize: 50, from: isoDate(range?.[0]), to: isoDate(range?.[1]), ...filters };
  const { data, isFetching } = useQuery({ queryKey: ['stock-receipts', params], queryFn: async () => (await http.get<PagedResult<ReceiptRow>>('/stock-receipts', { params })).data, placeholderData: keepPreviousData });
  const set = (key: string, v?: string | null) => { setFilters({ ...filters, [key]: v ?? undefined }); setPage(1); };
  const isMobile = !screens.lg;
  return (
    <Page title={t('receipts.title')} actions={has('stock.receipt.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/stock/receipts/new')} data-testid="receipt-new">{t('receipts.new')}</Button>}>
      <div className="bm-card">
        <div className="bm-toolbar">
          <DatePicker.RangePicker value={range} onChange={(v) => { setRange(v as never); setPage(1); }} format={DATE_FORMAT} style={{ width: screens.md ? 260 : '100%' }} />
          <Input.Search allowClear placeholder={t('docNo')} onSearch={(v) => set('q', v || undefined)} style={{ width: screens.md ? 200 : '100%' }} />
          <LookupSelect resource="warehouses" placeholder={t('warehouse')} value={filters.warehouseId} onChange={(v) => set('warehouseId', v)} style={{ width: screens.md ? 200 : '100%' }} />
          <LookupSelect resource="partners" placeholder={t('partner')} value={filters.partnerId} onChange={(v) => set('partnerId', v)} style={{ width: screens.md ? 220 : '100%' }} />
          <Select allowClear placeholder={t('status')} value={filters.status} onChange={(v) => set('status', v)} style={{ width: screens.md ? 150 : '100%' }}
            options={ENUMS.StockDocStatus.map((s) => ({ value: s, label: enumLabel('StockDocStatus', s) }))} />
        </div>
        {isMobile ? (
          <DocCards
            loading={isFetching && !data} items={data?.items} total={data?.totalItems ?? 0} page={page} onPage={setPage}
            render={(r) => ({
              key: r.id, onClick: () => navigate(`/stock/receipts/${r.id}`), title: r.docNo, sub: fmt.date(r.docDate), tag: <StatusTag enumName="StockDocStatus" status={r.status} />,
              meta: [
                { label: t('warehouse'), value: r.warehouse },
                { label: t('partner'), value: r.partner ?? '—' },
                { label: t('lines'), value: r.failed > 0 ? `${r.lineCount} (${r.failed} ${t('failedQc')})` : r.lineCount },
                { label: t('total'), value: fmt.money(r.totalAmount) },
              ],
            })}
          />
        ) : (
          <Table<ReceiptRow>
            rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }}
            onRow={(r) => ({ onClick: () => navigate(`/stock/receipts/${r.id}`), style: { cursor: 'pointer' } })}
            pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
            columns={[
              { title: t('docNo'), dataIndex: 'docNo', width: 170, render: (v) => <b style={{ fontWeight: 600 }}>{v}</b> },
              { title: t('docDate'), dataIndex: 'docDate', width: 110, render: fmt.date },
              { title: t('warehouse'), dataIndex: 'warehouse', width: 160 },
              { title: t('partner'), dataIndex: 'partner' },
              { title: t('invoiceNo'), dataIndex: 'invoiceNo', width: 120, responsive: ['xl'] },
              { title: t('lines'), dataIndex: 'lineCount', align: 'right', width: 80, render: (v, r) => <>{v}{r.failed > 0 && <Typography.Text type="danger"> ({r.failed} {t('failedQc')})</Typography.Text>}</> },
              { title: t('total'), dataIndex: 'totalAmount', align: 'right', width: 140, render: fmt.money },
              { title: t('status'), dataIndex: 'status', width: 120, render: (v) => <StatusTag enumName="StockDocStatus" status={v} /> },
            ]}
          />
        )}
      </div>
    </Page>
  );
}

/** Receipt editor with incoming quality check per line (step 1 inspection) and barcode entry. */
export function ReceiptEditorPage() {
  const { id } = useParams();
  const isNew = !id || id === 'new';
  const { t, i18n } = useTranslation('stock');
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const [head, setHead] = useState<{ docDate: dayjs.Dayjs | null; warehouseId?: string; partnerId?: string; invoiceNo?: string; invoiceDate?: dayjs.Dayjs | null; note?: string }>({ docDate: dayjs() });
  const [lines, setLines] = useState<ReceiptLine[]>([{ key: k(), quantity: 1, unitPrice: 0, qualityResult: 'Pass' }]);
  const [busy, setBusy] = useState(false);
  const [barcode, setBarcode] = useState('');
  const { data } = useQuery({ queryKey: ['stock-receipt', id], enabled: !isNew, queryFn: async () => (await http.get<ReceiptDetail>(`/stock-receipts/${id}`)).data });
  useEffect(() => {
    if (!data) return;
    setHead({ docDate: dayjs(data.docDate), warehouseId: data.warehouseId, partnerId: data.partnerId, invoiceNo: data.invoiceNo, invoiceDate: data.invoiceDate ? dayjs(data.invoiceDate) : null, note: data.note });
    setLines(data.lines.map((l) => ({ ...l, key: k() })));
  }, [data]);
  const editable = isNew ? has('stock.receipt.create') : data?.status === 'Draft' && has('stock.receipt.edit');
  const total = useMemo(() => lines.reduce((s, l) => s + (l.quantity || 0) * (l.unitPrice || 0), 0), [lines]);
  const patch = (key: string, p: Partial<ReceiptLine>) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...p } : l)));

  const body = () => ({
    docDate: isoDate(head.docDate), warehouseId: head.warehouseId, partnerId: head.partnerId ?? null, invoiceNo: head.invoiceNo || null, invoiceDate: isoDate(head.invoiceDate) ?? null, note: head.note || null,
    version: data?.version,
    lines: lines.filter((l) => l.ingredientId).map((l) => ({
      ingredientId: l.ingredientId, quantity: l.quantity, unitPrice: l.unitPrice, lotNo: l.lotNo || null, mfgDate: l.mfgDate ?? null, expiryDate: l.expiryDate ?? null,
      qualityResult: l.qualityResult ?? null, receivedTemp: l.receivedTemp ?? null, note: l.note || null,
    })),
  });
  const save = async (): Promise<string | null> => {
    if (!head.warehouseId || !head.docDate || body().lines.length === 0) { message.error(t('menu:fixErrors')); return null; }
    setBusy(true);
    try {
      if (isNew) {
        const r = await http.post<{ id: string }>('/stock-receipts', body(), { headers: { 'Idempotency-Key': idemKey() } });
        message.success(t('common:saved'));
        navigate(`/stock/receipts/${r.data.id}`, { replace: true });
        return r.data.id;
      }
      await http.put(`/stock-receipts/${id}`, body());
      await qc.invalidateQueries({ queryKey: ['stock-receipt', id] });
      message.success(t('common:saved'));
      return id!;
    } catch (e) {
      message.error(problemMessage(e));
      return null;
    } finally {
      setBusy(false);
    }
  };
  const post = () => modal.confirm({
    title: t('post'), content: t('postConfirm'), okButtonProps: { 'data-testid': 'confirm-post' } as never,
    onOk: async () => {
      const rid = await save();
      if (!rid) return;
      try {
        await http.post(`/stock-receipts/${rid}/post`, null, { headers: { 'Idempotency-Key': idemKey() } });
        message.success(t('posted'));
        await qc.invalidateQueries({ queryKey: ['stock-receipt', rid] });
        void qc.invalidateQueries({ queryKey: ['stock-receipts'] });
      } catch (e) {
        message.error(problemMessage(e));
      }
    },
  });
  const remove = async () => {
    try { await http.delete(`/stock-receipts/${id}`); navigate('/stock/receipts'); } catch (e) { message.error(problemMessage(e)); }
  };
  const scan = async () => {
    const code = barcode.trim();
    if (!code) return;
    const r = await http.get<LookupItem[]>('/ingredients/lookup', { params: { q: code, take: 1 } });
    const item = r.data[0];
    if (!item) { message.warning(t('notFound')); return; }
    setLines((ls) => [...ls.filter((l) => l.ingredientId), { key: k(), ingredientId: item.id, ingredientCode: item.code, ingredientName: item.nameVi, ingredientNameEn: item.nameEn ?? undefined, quantity: 1, unitPrice: 0, qualityResult: 'Pass' }]);
    setBarcode('');
  };

  return (
    <Page
      title={isNew ? t('receipts.new') : `${t('receipts.detail')} ${data?.docNo ?? ''}`}
      tag={data && <StatusTag enumName="StockDocStatus" status={data.status} />}
      actions={(
        <>
          <Button onClick={() => navigate('/stock/receipts')}>{t('common:actions.back')}</Button>
          {!isNew && <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{t('print')}</Button>}
          {!isNew && editable && has('stock.receipt.delete') && <Button danger icon={<DeleteOutlined />} onClick={() => void remove()}>{t('common:actions.delete')}</Button>}
          {editable && <Button icon={<SaveOutlined />} loading={busy} onClick={() => void save()} data-testid="receipt-save">{t('common:actions.save')}</Button>}
          {editable && has('stock.receipt.post') && <Button type="primary" icon={<CheckOutlined />} loading={busy} onClick={post} data-testid="receipt-post">{t('post')}</Button>}
        </>
      )}
    >
      <div className="bm-print-only"><Typography.Title level={4} style={{ textAlign: 'center' }}>{t('receipts.printTitle')}</Typography.Title></div>
      {editable ? (
        <Form layout="vertical" className="bm-formcard">
          <Form.Item label={t('docDate')} required><DatePicker value={head.docDate} onChange={(v) => setHead({ ...head, docDate: v })} format={DATE_FORMAT} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t('warehouse')} required><LookupSelect resource="warehouses" value={head.warehouseId} onChange={(v) => setHead({ ...head, warehouseId: v })} data-testid="receipt-warehouse" /></Form.Item>
          <Form.Item label={t('partner')} className="bm-span-2"><LookupSelect resource="partners" value={head.partnerId} onChange={(v) => setHead({ ...head, partnerId: v })} /></Form.Item>
          <Form.Item label={t('invoiceNo')}><Input maxLength={50} value={head.invoiceNo} onChange={(e) => setHead({ ...head, invoiceNo: e.target.value })} /></Form.Item>
          <Form.Item label={t('invoiceDate')}><DatePicker value={head.invoiceDate} onChange={(v) => setHead({ ...head, invoiceDate: v })} format={DATE_FORMAT} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t('note')} className="bm-span-2"><Input maxLength={1000} value={head.note} onChange={(e) => setHead({ ...head, note: e.target.value })} /></Form.Item>
        </Form>
      ) : data && (
        <div className="bm-card bm-pad">
          <InfoGrid items={[
            { key: 'no', label: t('docNo'), value: data.docNo },
            { key: 'd', label: t('docDate'), value: fmt.date(data.docDate) },
            { key: 'w', label: t('warehouse'), value: data.warehouse },
            { key: 'p', label: t('partner'), value: data.partner?.nameVi ?? '—' },
            { key: 'i', label: t('invoiceNo'), value: data.invoiceNo || '—' },
            { key: 'id', label: t('invoiceDate'), value: fmt.date(data.invoiceDate) || '—' },
            { key: 't', label: t('total'), value: fmt.money(data.totalAmount) },
            ...(data.note ? [{ key: 'n', label: t('note'), value: data.note }] : []),
          ]} />
        </div>
      )}
      <Section
        title={t('lineItems')}
        extra={editable && (
          <span className="bm-sec-extra bm-no-print">
            <Input prefix={<BarcodeOutlined />} placeholder={t('scanHint')} value={barcode} onChange={(e) => setBarcode(e.target.value)} onPressEnter={() => void scan()} style={{ width: 240, maxWidth: '100%' }} aria-label={t('scan')} />
            <Button icon={<PlusOutlined />} onClick={() => setLines([...lines, { key: k(), quantity: 1, unitPrice: 0, qualityResult: 'Pass' }])}>{t('addLine')}</Button>
          </span>
        )}
      >
        {editable && <div className="bm-note is-info bm-no-print" style={{ margin: '12px 16px 0' }}>{t('qcHint')}</div>}
        <div className="bm-lines">
          {lines.map((l) => (
            <LineRow key={l.key} actions={editable && <Button type="text" danger icon={<DeleteOutlined />} aria-label={t('common:actions.delete')} onClick={() => setLines(lines.filter((x) => x.key !== l.key))} />}>
              <LineCell label={t('ingredient')} flex="1 1 220px" static={!editable}>
                {editable
                  ? <LookupSelect resource="ingredients" value={l.ingredientId} data-testid="receipt-ingredient" onChange={(v, item) => patch(l.key, { ingredientId: v ?? undefined, ingredientCode: (item as LookupItem)?.code, ingredientName: (item as LookupItem)?.nameVi })} />
                  : ingName(l, i18n.language)}
              </LineCell>
              <LineCell label={l.unit ? `${t('quantity')} (${l.unit})` : t('quantity')} flex="0 0 100px" static={!editable}>
                {editable ? <InputNumber min={0.001} value={l.quantity} onChange={(v) => patch(l.key, { quantity: Number(v) || 0 })} data-testid="receipt-qty" /> : qtyText(l.quantity)}
              </LineCell>
              <LineCell label={t('unitPrice')} flex="0 0 120px" static={!editable}>
                {editable ? <InputNumber min={0} step={1000} value={l.unitPrice} onChange={(v) => patch(l.key, { unitPrice: Number(v) || 0 })} data-testid="receipt-price" /> : fmt.money(l.unitPrice)}
              </LineCell>
              <LineCell label={t('lot')} flex="0 0 110px" static={!editable}>
                {editable ? <Input maxLength={50} value={l.lotNo} onChange={(e) => patch(l.key, { lotNo: e.target.value })} /> : l.lotNo || '—'}
              </LineCell>
              <LineCell label={t('mfg')} flex="0 0 140px" static={!editable}>
                {editable ? <DatePicker format={DATE_FORMAT} value={l.mfgDate ? dayjs(l.mfgDate) : null} onChange={(v) => patch(l.key, { mfgDate: isoDate(v) ?? null })} /> : fmt.date(l.mfgDate) || '—'}
              </LineCell>
              <LineCell label={t('expiry')} flex="0 0 140px" static={!editable}>
                {editable ? <DatePicker format={DATE_FORMAT} value={l.expiryDate ? dayjs(l.expiryDate) : null} onChange={(v) => patch(l.key, { expiryDate: isoDate(v) ?? null })} /> : fmt.date(l.expiryDate) || '—'}
              </LineCell>
              <LineCell label={t('qc')} flex="0 0 160px" static={!editable}>
                {editable
                  ? <Select value={l.qualityResult ?? undefined} status={l.qualityResult === 'Fail' ? 'error' : undefined} onChange={(v) => patch(l.key, { qualityResult: v })} options={ENUMS.QualityResult.map((q) => ({ value: q, label: enumLabel('QualityResult', q) }))} />
                  : <QcTag value={l.qualityResult} />}
              </LineCell>
              <LineCell label={t('temp')} flex="0 0 90px" static={!editable}>
                {editable ? <InputNumber min={-60} max={100} value={l.receivedTemp ?? undefined} onChange={(v) => patch(l.key, { receivedTemp: v ?? null })} /> : fmt.number(l.receivedTemp, 1) || '—'}
              </LineCell>
              <LineCell label={t('note')} flex="1 1 160px" static={!editable}>
                {editable ? <Input maxLength={500} value={l.note} onChange={(e) => patch(l.key, { note: e.target.value })} /> : l.note || '—'}
              </LineCell>
              <LineCell label={t('amount')} flex="1 0 110px" static>{fmt.money((l.quantity || 0) * (l.unitPrice || 0))}</LineCell>
            </LineRow>
          ))}
        </div>
        <Totals items={[{ label: t('lines'), value: lines.filter((l) => l.ingredientId).length }, { label: t('total'), value: fmt.money(total) }]} />
      </Section>
    </Page>
  );
}
