import { useEffect, useState } from 'react';
import { App, Button, Card, Col, DatePicker, Descriptions, Form, Grid, Input, InputNumber, Modal, Row, Select, Space, Table, Tag, Typography } from 'antd';
import { BulbOutlined, CheckOutlined, DeleteOutlined, PlusOutlined, PrinterOutlined, SaveOutlined, ScheduleOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel, type LookupItem } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';
import { idemKey, ingName, StatusTag } from './shared';

interface IssueRow { id: string; docNo: string; docDate: string; warehouse: string; reason: string; menu?: string; status: string; isPlanned: boolean; totalAmount: number; lineCount: number }
interface IssueLine { key: string; ingredientId?: string; ingredientCode?: string; ingredientName?: string; ingredientNameEn?: string; unit?: string; lotId?: string | null; lotNo?: string | null; quantity: number; unitCost?: number; amount?: number; available?: number; note?: string }
interface IssueDetail {
  id: string; docNo: string; docDate: string; warehouseId: string; warehouse?: string; reason: string; menuId?: string; menu?: { id: string; code: string; menuDate: string; servings: number } | null;
  targetWarehouseId?: string; status: string; isPlanned: boolean; totalAmount: number; note?: string; version: number; lines: (Omit<IssueLine, 'key'> & { id: string })[];
}
interface LotSuggestion { lotId: string; lotNo?: string; expiryDate?: string; available: number; quantity: number; unitCost: number }
interface MenuLite { id: string; code: string; menuDate: string; shift: string; servings: number; status: string }

let seq = 0;
const k = () => `i${++seq}`;

/** Stock issues list with "create from approved menu" (SPEC §7.8). */
export function IssuesPage() {
  const { t } = useTranslation('stock');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs] | null>([dayjs().subtract(30, 'day'), dayjs().add(14, 'day')]);
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [menuOpen, setMenuOpen] = useState(false);
  const [menuId, setMenuId] = useState<string>();
  const params = { page, pageSize: 50, from: isoDate(range?.[0]), to: isoDate(range?.[1]), ...filters };
  const { data, isFetching, refetch } = useQuery({ queryKey: ['stock-issues', params], queryFn: async () => (await http.get<PagedResult<IssueRow>>('/stock-issues', { params })).data, placeholderData: keepPreviousData });
  const menus = useQuery({
    queryKey: ['menus-approved'], enabled: menuOpen,
    queryFn: async () => (await http.get<PagedResult<MenuLite>>('/menus', { params: { status: 'Approved', from: isoDate(dayjs().subtract(7, 'day')), to: isoDate(dayjs().add(60, 'day')), pageSize: 200 } })).data.items,
  });
  const set = (key: string, v?: string | null) => { setFilters({ ...filters, [key]: v ?? undefined }); setPage(1); };
  const fromMenu = async () => {
    try {
      const r = await http.post<{ id: string }[]>(`/stock-issues/from-menu/${menuId}`, null, { headers: { 'Idempotency-Key': idemKey() } });
      message.success(t('issues.created', { n: r.data.length }));
      setMenuOpen(false);
      if (r.data.length === 1) navigate(`/stock/issues/${r.data[0].id}`);
      else void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t('issues.title')}</Typography.Title>
        <Space wrap>
          {has('stock.issue.create') && <Button icon={<ScheduleOutlined />} onClick={() => setMenuOpen(true)} data-testid="issue-from-menu">{t('fromMenu')}</Button>}
          {has('stock.issue.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/stock/issues/new')}>{t('issues.new')}</Button>}
        </Space>
      </div>
      <div className="bm-toolbar">
        <DatePicker.RangePicker value={range} onChange={(v) => { setRange(v as never); setPage(1); }} format={DATE_FORMAT} style={{ width: screens.md ? 260 : '100%' }} />
        <Input.Search allowClear placeholder={t('docNo')} onSearch={(v) => set('q', v || undefined)} style={{ width: screens.md ? 200 : '100%' }} />
        <LookupSelect resource="warehouses" placeholder={t('warehouse')} value={filters.warehouseId} onChange={(v) => set('warehouseId', v)} style={{ width: screens.md ? 200 : '100%' }} />
        <Select allowClear placeholder={t('reason')} value={filters.reason} onChange={(v) => set('reason', v)} style={{ width: screens.md ? 170 : '100%' }} options={ENUMS.IssueReason.map((s) => ({ value: s, label: enumLabel('IssueReason', s) }))} />
        <Select allowClear placeholder={t('status')} value={filters.status} onChange={(v) => set('status', v)} style={{ width: screens.md ? 150 : '100%' }} options={ENUMS.StockDocStatus.map((s) => ({ value: s, label: enumLabel('StockDocStatus', s) }))} />
      </div>
      <Table<IssueRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} size={screens.md ? 'middle' : 'small'}
        onRow={(r) => ({ onClick: () => navigate(`/stock/issues/${r.id}`), style: { cursor: 'pointer' } })}
        pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('docNo'), dataIndex: 'docNo', width: 170, render: (v, r) => <Space size={4}>{v}{r.isPlanned && <Tag color="purple">{t('planned')}</Tag>}</Space> },
          { title: t('docDate'), dataIndex: 'docDate', width: 110, render: fmt.date },
          { title: t('warehouse'), dataIndex: 'warehouse', width: 160 },
          { title: t('reason'), dataIndex: 'reason', width: 150, render: (v) => enumLabel('IssueReason', v) },
          { title: t('menu'), dataIndex: 'menu', width: 170, responsive: ['md'] },
          { title: t('lines'), dataIndex: 'lineCount', align: 'right', width: 80 },
          { title: t('total'), dataIndex: 'totalAmount', align: 'right', width: 140, render: fmt.money },
          { title: t('status'), dataIndex: 'status', width: 120, render: (v) => <StatusTag enumName="StockDocStatus" status={v} /> },
        ]}
      />
      <Modal open={menuOpen} title={t('fromMenu')} onCancel={() => setMenuOpen(false)} onOk={() => void fromMenu()} okButtonProps={{ disabled: !menuId, 'data-testid': 'issue-from-menu-ok' } as never}>
        <Select showSearch optionFilterProp="label" loading={menus.isFetching} value={menuId} onChange={setMenuId} placeholder={t('issues.pickMenu')} style={{ width: '100%' }} data-testid="issue-menu-select"
          options={(menus.data ?? []).map((m) => ({ value: m.id, label: `${fmt.date(m.menuDate)} · ${m.shift} · ${m.code} · ${fmt.number(m.servings)}` }))} />
      </Modal>
    </Card>
  );
}

/** Issue editor: FEFO lot suggestion per line, posting with issue cost by the configured method. */
export function IssueEditorPage() {
  const { id } = useParams();
  const isNew = !id || id === 'new';
  const { t, i18n } = useTranslation('stock');
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const [head, setHead] = useState<{ docDate: dayjs.Dayjs | null; warehouseId?: string; reason: string; targetWarehouseId?: string; note?: string; menuId?: string }>({ docDate: dayjs(), reason: 'Other' });
  const [lines, setLines] = useState<IssueLine[]>([{ key: k(), quantity: 1 }]);
  const [busy, setBusy] = useState(false);
  const { data } = useQuery({ queryKey: ['stock-issue', id], enabled: !isNew, queryFn: async () => (await http.get<IssueDetail>(`/stock-issues/${id}`)).data });
  useEffect(() => {
    if (!data) return;
    setHead({ docDate: dayjs(data.docDate), warehouseId: data.warehouseId, reason: data.reason, targetWarehouseId: data.targetWarehouseId, note: data.note, menuId: data.menuId });
    setLines(data.lines.map((l) => ({ ...l, key: k() })));
  }, [data]);
  const editable = isNew ? has('stock.issue.create') : data?.status === 'Draft' && has('stock.issue.edit');
  const patch = (key: string, p: Partial<IssueLine>) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...p } : l)));

  const body = () => ({
    docDate: isoDate(head.docDate), warehouseId: head.warehouseId, reason: head.reason, menuId: head.menuId ?? null, targetWarehouseId: head.reason === 'Transfer' ? head.targetWarehouseId ?? null : null,
    note: head.note || null, version: data?.version,
    lines: lines.filter((l) => l.ingredientId && l.quantity > 0).map((l) => ({ ingredientId: l.ingredientId, lotId: l.lotId ?? null, lotNo: l.lotNo ?? null, quantity: l.quantity, note: l.note || null })),
  });
  const save = async (): Promise<string | null> => {
    if (!head.warehouseId || !head.docDate || body().lines.length === 0) { message.error(t('menu:fixErrors')); return null; }
    setBusy(true);
    try {
      if (isNew) {
        const r = await http.post<{ id: string }>('/stock-issues', body(), { headers: { 'Idempotency-Key': idemKey() } });
        message.success(t('common:saved'));
        navigate(`/stock/issues/${r.data.id}`, { replace: true });
        return r.data.id;
      }
      await http.put(`/stock-issues/${id}`, body());
      await qc.invalidateQueries({ queryKey: ['stock-issue', id] });
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
        await http.post(`/stock-issues/${rid}/post`, null, { headers: { 'Idempotency-Key': idemKey() } });
        message.success(t('posted'));
        await qc.invalidateQueries({ queryKey: ['stock-issue', rid] });
        void qc.invalidateQueries({ queryKey: ['stock-issues'] });
      } catch (e) {
        message.error(problemMessage(e));
      }
    },
  });
  /** Splits a line into FEFO lots. */
  const suggest = async (l: IssueLine) => {
    if (!head.warehouseId || !l.ingredientId) return;
    const r = await http.get<LotSuggestion[]>('/stock-issues/suggest-lots', { params: { warehouseId: head.warehouseId, ingredientId: l.ingredientId, quantity: l.quantity } });
    if (r.data.length === 0) { message.warning(t('available') + ': 0'); return; }
    const covered = r.data.reduce((s, x) => s + x.quantity, 0);
    const split: IssueLine[] = r.data.map((x) => ({ ...l, key: k(), lotId: x.lotId, lotNo: x.lotNo, quantity: x.quantity, available: x.available }));
    if (covered < l.quantity) split.push({ ...l, key: k(), lotId: null, lotNo: null, quantity: Number((l.quantity - covered).toFixed(4)) });
    setLines((ls) => ls.flatMap((x) => (x.key === l.key ? split : [x])));
  };
  const remove = async () => { try { await http.delete(`/stock-issues/${id}`); navigate('/stock/issues'); } catch (e) { message.error(problemMessage(e)); } };

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Card>
        <div className="bm-page-header bm-no-print">
          <Space>
            <Typography.Title level={4} style={{ margin: 0 }}>{isNew ? t('issues.new') : `${t('issues.detail')} ${data?.docNo ?? ''}`}</Typography.Title>
            {data && <StatusTag enumName="StockDocStatus" status={data.status} />}
            {data?.isPlanned && <Tag color="purple">{t('planned')}</Tag>}
          </Space>
          <Space wrap>
            <Button onClick={() => navigate('/stock/issues')}>{t('common:actions.back')}</Button>
            {!isNew && <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{t('print')}</Button>}
            {!isNew && editable && has('stock.issue.delete') && <Button danger icon={<DeleteOutlined />} onClick={() => void remove()}>{t('common:actions.delete')}</Button>}
            {editable && <Button icon={<SaveOutlined />} loading={busy} onClick={() => void save()}>{t('common:actions.save')}</Button>}
            {editable && has('stock.issue.post') && <Button type="primary" icon={<CheckOutlined />} loading={busy} onClick={post} data-testid="issue-post">{t('post')}</Button>}
          </Space>
        </div>
        <div className="bm-print-only"><Typography.Title level={4} style={{ textAlign: 'center' }}>{t('issues.printTitle')}</Typography.Title></div>
        {editable ? (
          <Form layout="vertical">
            <Row gutter={12}>
              <Col xs={12} md={6}><Form.Item label={t('docDate')} required><DatePicker value={head.docDate} onChange={(v) => setHead({ ...head, docDate: v })} format={DATE_FORMAT} style={{ width: '100%' }} /></Form.Item></Col>
              <Col xs={12} md={6}><Form.Item label={t('warehouse')} required><LookupSelect resource="warehouses" value={head.warehouseId} onChange={(v) => setHead({ ...head, warehouseId: v })} /></Form.Item></Col>
              <Col xs={12} md={6}><Form.Item label={t('reason')}><Select value={head.reason} onChange={(v) => setHead({ ...head, reason: v })} options={ENUMS.IssueReason.filter((r) => r !== 'Adjustment').map((r) => ({ value: r, label: enumLabel('IssueReason', r) }))} /></Form.Item></Col>
              {head.reason === 'Transfer' && <Col xs={12} md={6}><Form.Item label={t('targetWarehouse')} required><LookupSelect resource="warehouses" value={head.targetWarehouseId} onChange={(v) => setHead({ ...head, targetWarehouseId: v })} /></Form.Item></Col>}
              <Col xs={24} md={head.reason === 'Transfer' ? 24 : 6}><Form.Item label={t('note')}><Input maxLength={1000} value={head.note} onChange={(e) => setHead({ ...head, note: e.target.value })} /></Form.Item></Col>
            </Row>
          </Form>
        ) : data && (
          <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 4 }}>
            <Descriptions.Item label={t('docNo')}>{data.docNo}</Descriptions.Item>
            <Descriptions.Item label={t('docDate')}>{fmt.date(data.docDate)}</Descriptions.Item>
            <Descriptions.Item label={t('warehouse')}>{data.warehouse}</Descriptions.Item>
            <Descriptions.Item label={t('reason')}>{enumLabel('IssueReason', data.reason)}</Descriptions.Item>
            {data.menu && <Descriptions.Item label={t('menu')}><a onClick={() => navigate(`/menus/${data.menu!.id}`)}>{data.menu.code}</a> · {fmt.number(data.menu.servings)}</Descriptions.Item>}
            <Descriptions.Item label={t('total')}>{fmt.money(data.totalAmount)}</Descriptions.Item>
            {data.note && <Descriptions.Item label={t('note')}>{data.note}</Descriptions.Item>}
          </Descriptions>
        )}
        {editable && data?.menu && <Typography.Text type="secondary">{t('menu')}: {data.menu.code} · {fmt.date(data.menu.menuDate)} · {fmt.number(data.menu.servings)}</Typography.Text>}
      </Card>
      <Card size="small" title={t('lineItems')} extra={editable && <Button size="small" icon={<PlusOutlined />} className="bm-no-print" onClick={() => setLines([...lines, { key: k(), quantity: 1 }])}>{t('addLine')}</Button>}>
        <Table<IssueLine>
          rowKey="key" dataSource={lines} pagination={false} size="small" scroll={{ x: 'max-content' }}
          columns={[
            { title: t('ingredient'), key: 'ing', width: 280, render: (_, l) => (editable
              ? <LookupSelect resource="ingredients" value={l.ingredientId} style={{ minWidth: 260 }} onChange={(v, item) => patch(l.key, { ingredientId: v ?? undefined, ingredientCode: (item as LookupItem)?.code, ingredientName: (item as LookupItem)?.nameVi, lotId: null, lotNo: null })} />
              : ingName(l, i18n.language)) },
            { title: t('lot'), key: 'lot', width: 130, render: (_, l) => l.lotNo },
            { title: t('quantity'), key: 'q', width: 120, align: 'right', render: (_, l) => (editable ? <InputNumber size="small" min={0.001} value={l.quantity} onChange={(v) => patch(l.key, { quantity: Number(v) || 0 })} /> : `${fmt.number(l.quantity, 3)} ${l.unit ?? ''}`) },
            ...(editable ? [{ title: t('available'), key: 'av', width: 110, align: 'right' as const, render: (_: unknown, l: IssueLine) => fmt.number(l.available, 3) }] : []),
            { title: t('unitCost'), dataIndex: 'unitCost', width: 120, align: 'right', render: (v) => (v ? fmt.money(v) : '') },
            { title: t('amount'), dataIndex: 'amount', width: 130, align: 'right', render: (v) => (v ? fmt.money(v) : '') },
            { title: t('note'), key: 'n', width: 180, render: (_, l) => (editable ? <Input size="small" maxLength={500} value={l.note} onChange={(e) => patch(l.key, { note: e.target.value })} /> : l.note) },
            ...(editable ? [{ key: 'act', width: 80, render: (_: unknown, l: IssueLine) => (
              <Space size={0}>
                <Button size="small" type="text" icon={<BulbOutlined />} title={t('suggestLots')} disabled={!l.ingredientId || !head.warehouseId} onClick={() => void suggest(l)} />
                <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => setLines(lines.filter((x) => x.key !== l.key))} />
              </Space>
            ) }] : []),
          ]}
        />
      </Card>
    </Space>
  );
}
