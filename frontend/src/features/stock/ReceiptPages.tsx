import { useEffect, useMemo, useState } from 'react';
import { Alert, App, Button, Card, Col, DatePicker, Descriptions, Form, Grid, Input, InputNumber, Row, Select, Space, Table, Typography } from 'antd';
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
import { idemKey, ingName, QcTag, StatusTag } from './shared';

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
  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t('receipts.title')}</Typography.Title>
        {has('stock.receipt.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/stock/receipts/new')} data-testid="receipt-new">{t('receipts.new')}</Button>}
      </div>
      <div className="bm-toolbar">
        <DatePicker.RangePicker value={range} onChange={(v) => { setRange(v as never); setPage(1); }} format={DATE_FORMAT} style={{ width: screens.md ? 260 : '100%' }} />
        <Input.Search allowClear placeholder={t('docNo')} onSearch={(v) => set('q', v || undefined)} style={{ width: screens.md ? 200 : '100%' }} />
        <LookupSelect resource="warehouses" placeholder={t('warehouse')} value={filters.warehouseId} onChange={(v) => set('warehouseId', v)} style={{ width: screens.md ? 200 : '100%' }} />
        <LookupSelect resource="partners" placeholder={t('partner')} value={filters.partnerId} onChange={(v) => set('partnerId', v)} style={{ width: screens.md ? 220 : '100%' }} />
        <Select allowClear placeholder={t('status')} value={filters.status} onChange={(v) => set('status', v)} style={{ width: screens.md ? 150 : '100%' }}
          options={ENUMS.StockDocStatus.map((s) => ({ value: s, label: enumLabel('StockDocStatus', s) }))} />
      </div>
      <Table<ReceiptRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} size={screens.md ? 'middle' : 'small'}
        onRow={(r) => ({ onClick: () => navigate(`/stock/receipts/${r.id}`), style: { cursor: 'pointer' } })}
        pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('docNo'), dataIndex: 'docNo', width: 170 },
          { title: t('docDate'), dataIndex: 'docDate', width: 110, render: fmt.date },
          { title: t('warehouse'), dataIndex: 'warehouse', width: 160 },
          { title: t('partner'), dataIndex: 'partner', responsive: ['md'] },
          { title: t('invoiceNo'), dataIndex: 'invoiceNo', width: 120, responsive: ['lg'] },
          { title: t('lines'), dataIndex: 'lineCount', align: 'right', width: 80, render: (v, r) => <>{v}{r.failed > 0 && <Typography.Text type="danger"> ({r.failed} {t('failedQc')})</Typography.Text>}</> },
          { title: t('total'), dataIndex: 'totalAmount', align: 'right', width: 140, render: fmt.money },
          { title: t('status'), dataIndex: 'status', width: 120, render: (v) => <StatusTag enumName="StockDocStatus" status={v} /> },
        ]}
      />
    </Card>
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
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Card>
        <div className="bm-page-header bm-no-print">
          <Space>
            <Typography.Title level={4} style={{ margin: 0 }}>{isNew ? t('receipts.new') : `${t('receipts.detail')} ${data?.docNo ?? ''}`}</Typography.Title>
            {data && <StatusTag enumName="StockDocStatus" status={data.status} />}
          </Space>
          <Space wrap>
            <Button onClick={() => navigate('/stock/receipts')}>{t('common:actions.back')}</Button>
            {!isNew && <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{t('print')}</Button>}
            {!isNew && editable && has('stock.receipt.delete') && <Button danger icon={<DeleteOutlined />} onClick={() => void remove()}>{t('common:actions.delete')}</Button>}
            {editable && <Button icon={<SaveOutlined />} loading={busy} onClick={() => void save()} data-testid="receipt-save">{t('common:actions.save')}</Button>}
            {editable && has('stock.receipt.post') && <Button type="primary" icon={<CheckOutlined />} loading={busy} onClick={post} data-testid="receipt-post">{t('post')}</Button>}
          </Space>
        </div>
        <div className="bm-print-only"><Typography.Title level={4} style={{ textAlign: 'center' }}>{t('receipts.printTitle')}</Typography.Title></div>
        {editable ? (
          <Form layout="vertical">
            <Row gutter={12}>
              <Col xs={12} md={6}><Form.Item label={t('docDate')} required><DatePicker value={head.docDate} onChange={(v) => setHead({ ...head, docDate: v })} format={DATE_FORMAT} style={{ width: '100%' }} /></Form.Item></Col>
              <Col xs={12} md={6}><Form.Item label={t('warehouse')} required><LookupSelect resource="warehouses" value={head.warehouseId} onChange={(v) => setHead({ ...head, warehouseId: v })} data-testid="receipt-warehouse" /></Form.Item></Col>
              <Col xs={24} md={12}><Form.Item label={t('partner')}><LookupSelect resource="partners" value={head.partnerId} onChange={(v) => setHead({ ...head, partnerId: v })} /></Form.Item></Col>
              <Col xs={12} md={6}><Form.Item label={t('invoiceNo')}><Input maxLength={50} value={head.invoiceNo} onChange={(e) => setHead({ ...head, invoiceNo: e.target.value })} /></Form.Item></Col>
              <Col xs={12} md={6}><Form.Item label={t('invoiceDate')}><DatePicker value={head.invoiceDate} onChange={(v) => setHead({ ...head, invoiceDate: v })} format={DATE_FORMAT} style={{ width: '100%' }} /></Form.Item></Col>
              <Col xs={24} md={12}><Form.Item label={t('note')}><Input maxLength={1000} value={head.note} onChange={(e) => setHead({ ...head, note: e.target.value })} /></Form.Item></Col>
            </Row>
          </Form>
        ) : data && (
          <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 4 }}>
            <Descriptions.Item label={t('docNo')}>{data.docNo}</Descriptions.Item>
            <Descriptions.Item label={t('docDate')}>{fmt.date(data.docDate)}</Descriptions.Item>
            <Descriptions.Item label={t('warehouse')}>{data.warehouse}</Descriptions.Item>
            <Descriptions.Item label={t('partner')}>{data.partner?.nameVi}</Descriptions.Item>
            <Descriptions.Item label={t('invoiceNo')}>{data.invoiceNo}</Descriptions.Item>
            <Descriptions.Item label={t('invoiceDate')}>{fmt.date(data.invoiceDate)}</Descriptions.Item>
            <Descriptions.Item label={t('total')}>{fmt.money(data.totalAmount)}</Descriptions.Item>
            {data.note && <Descriptions.Item label={t('note')}>{data.note}</Descriptions.Item>}
          </Descriptions>
        )}
      </Card>
      <Card size="small" title={t('lineItems')} extra={editable && (
        <Space className="bm-no-print">
          <Input prefix={<BarcodeOutlined />} placeholder={t('scanHint')} value={barcode} onChange={(e) => setBarcode(e.target.value)} onPressEnter={() => void scan()} style={{ width: 260 }} aria-label={t('scan')} />
          <Button size="small" icon={<PlusOutlined />} onClick={() => setLines([...lines, { key: k(), quantity: 1, unitPrice: 0, qualityResult: 'Pass' }])}>{t('addLine')}</Button>
        </Space>
      )}>
        {editable && <Alert type="info" showIcon message={t('qcHint')} style={{ marginBottom: 8 }} className="bm-no-print" />}
        <Table<ReceiptLine>
          rowKey="key" dataSource={lines} pagination={false} size="small" scroll={{ x: 'max-content' }}
          summary={() => <Table.Summary.Row><Table.Summary.Cell index={0} colSpan={3}><b>{t('total')}</b></Table.Summary.Cell><Table.Summary.Cell index={1} align="right"><b>{fmt.money(total)}</b></Table.Summary.Cell></Table.Summary.Row>}
          columns={[
            { title: t('ingredient'), key: 'ing', width: 280, render: (_, l) => (editable
              ? <LookupSelect resource="ingredients" value={l.ingredientId} style={{ minWidth: 260 }} data-testid="receipt-ingredient" onChange={(v, item) => patch(l.key, { ingredientId: v ?? undefined, ingredientCode: (item as LookupItem)?.code, ingredientName: (item as LookupItem)?.nameVi })} />
              : ingName(l, i18n.language)) },
            { title: t('quantity'), key: 'q', width: 110, align: 'right', render: (_, l) => (editable ? <InputNumber size="small" min={0.001} value={l.quantity} onChange={(v) => patch(l.key, { quantity: Number(v) || 0 })} data-testid="receipt-qty" /> : `${fmt.number(l.quantity, 3)} ${l.unit ?? ''}`) },
            { title: t('unitPrice'), key: 'p', width: 130, align: 'right', render: (_, l) => (editable ? <InputNumber size="small" min={0} step={1000} value={l.unitPrice} onChange={(v) => patch(l.key, { unitPrice: Number(v) || 0 })} data-testid="receipt-price" /> : fmt.money(l.unitPrice)) },
            { title: t('amount'), key: 'a', width: 130, align: 'right', render: (_, l) => fmt.money((l.quantity || 0) * (l.unitPrice || 0)) },
            { title: t('lot'), key: 'lot', width: 120, render: (_, l) => (editable ? <Input size="small" maxLength={50} value={l.lotNo} onChange={(e) => patch(l.key, { lotNo: e.target.value })} /> : l.lotNo) },
            { title: t('mfg'), key: 'mfg', width: 130, render: (_, l) => (editable ? <DatePicker size="small" format={DATE_FORMAT} value={l.mfgDate ? dayjs(l.mfgDate) : null} onChange={(v) => patch(l.key, { mfgDate: isoDate(v) ?? null })} /> : fmt.date(l.mfgDate)) },
            { title: t('expiry'), key: 'exp', width: 130, render: (_, l) => (editable ? <DatePicker size="small" format={DATE_FORMAT} value={l.expiryDate ? dayjs(l.expiryDate) : null} onChange={(v) => patch(l.key, { expiryDate: isoDate(v) ?? null })} /> : fmt.date(l.expiryDate)) },
            { title: t('qc'), key: 'qc', width: 150, render: (_, l) => (editable
              ? <Select size="small" value={l.qualityResult ?? undefined} style={{ width: 140 }} onChange={(v) => patch(l.key, { qualityResult: v })} options={ENUMS.QualityResult.map((q) => ({ value: q, label: enumLabel('QualityResult', q) }))} />
              : <QcTag value={l.qualityResult} />) },
            { title: t('temp'), key: 't', width: 110, render: (_, l) => (editable ? <InputNumber size="small" min={-60} max={100} value={l.receivedTemp ?? undefined} onChange={(v) => patch(l.key, { receivedTemp: v ?? null })} /> : fmt.number(l.receivedTemp, 1)) },
            { title: t('note'), key: 'n', width: 180, render: (_, l) => (editable ? <Input size="small" maxLength={500} value={l.note} onChange={(e) => patch(l.key, { note: e.target.value })} /> : l.note) },
            ...(editable ? [{ key: 'del', width: 40, render: (_: unknown, l: ReceiptLine) => <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => setLines(lines.filter((x) => x.key !== l.key))} /> }] : []),
          ]}
        />
      </Card>
    </Space>
  );
}
