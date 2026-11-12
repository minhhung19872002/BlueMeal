import { useState } from 'react';
import { App, Button, Checkbox, DatePicker, Grid, Input, Pagination, Select, Skeleton, Space, Table, Tag, Tooltip } from 'antd';
import type { CheckboxProps } from 'antd';
import { CheckOutlined, PlusOutlined, ThunderboltOutlined, WarningFilled } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { DATE_FORMAT, fmt, isoDate, mondayOf } from '@/shared/utils/format';
import { Page } from '@/shared/components/PageChrome';
import { MenuStatusTag } from './shared';

interface Row {
  id: string; code: string; menuDate: string; shift: string; shiftEn?: string; subject?: string; customer?: string; servings: number; creationMode: string; status: string;
  approvalLevel: number; energyKcal?: number; costPerServing?: number; nutritionScore?: number; criticalWarnings: number; dishes: { dishId: string; nameVi: string; nameEn?: string; type: string }[];
}

/** Menu list (SPEC §7.4.2) and approval queue (SPEC §7.4.5) when approvalMode is set. */
export default function MenuListPage({ approvalMode = false }: { approvalMode?: boolean }) {
  const { t, i18n } = useTranslation('menu');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs] | null>(approvalMode ? null : [mondayOf(dayjs()), mondayOf(dayjs()).add(4, 'week')]);
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [q, setQ] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const params = { page, pageSize: 50, q: q || undefined, from: isoDate(range?.[0]), to: isoDate(range?.[1]), pendingMyApproval: approvalMode || undefined, ...filters };
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['menus', params],
    queryFn: async () => (await http.get<PagedResult<Row>>('/menus', { params })).data,
    placeholderData: keepPreviousData,
  });
  const set = (k: string, v?: string | null) => { setFilters({ ...filters, [k]: v ?? undefined }); setPage(1); };
  const bulk = async (action: string) => {
    try {
      const r = await http.post<{ id: string; ok: boolean; error?: string }[]>('/menus/bulk', { ids: selected, action });
      const ok = r.data.filter((x) => x.ok).length;
      message.info(t('bulkResult', { ok, total: r.data.length }));
      r.data.filter((x) => !x.ok).forEach((x) => message.warning(x.error));
      setSelected([]);
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const canCost = has('menu.view_cost') || has('cost.view');
  const isMobile = !screens.lg;
  const en = i18n.language === 'en';
  const dishName = (d: Row['dishes'][number]) => (en && d.nameEn ? d.nameEn : d.nameVi);
  const shiftName = (r: Row) => (en && r.shiftEn ? r.shiftEn : r.shift);
  const toggle = (rowId: string) => setSelected((s) => (s.includes(rowId) ? s.filter((x) => x !== rowId) : [...s, rowId]));
  const statusCell = (r: Row) => (
    <Space size={4}>
      <MenuStatusTag status={r.status} />
      {r.status === 'Submitted' && r.approvalLevel > 0 && <Tag>L{r.approvalLevel}</Tag>}
      {r.criticalWarnings > 0 && <Tooltip title={t('critical')}><WarningFilled style={{ color: 'var(--danger)' }} /></Tooltip>}
    </Space>
  );
  const actions = (
    <>
      {approvalMode && selected.length > 0 && <Button type="primary" icon={<CheckOutlined />} onClick={() => void bulk('approve')}>{t('bulkApprove')} ({selected.length})</Button>}
      {!approvalMode && selected.length > 0 && has('menu.publish') && <Button className="bm-btn-soft" onClick={() => void bulk('publish')}>{t('bulkPublish')} ({selected.length})</Button>}
      {!approvalMode && has('menu.create') && <Button icon={<ThunderboltOutlined />} onClick={() => navigate('/menus/auto')}>{t('auto')}</Button>}
      {!approvalMode && has('menu.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/menus/new')}>{t('new')}</Button>}
    </>
  );
  return (
    <Page title={approvalMode ? t('approval') : t('list')} actions={actions}>
      <div className="bm-card">
        <div className="bm-toolbar">
          {!approvalMode && <DatePicker.RangePicker value={range} onChange={(v) => { setRange(v as never); setPage(1); }} format={DATE_FORMAT} style={{ width: screens.md ? 260 : '100%' }} />}
          <Input.Search allowClear placeholder={t('code')} onSearch={(v) => { setQ(v); setPage(1); }} style={{ width: screens.md ? 180 : '100%' }} />
          <LookupSelect resource="meal-shifts" placeholder={t('shift')} value={filters.mealShiftId} onChange={(v) => set('mealShiftId', v)} style={{ width: screens.md ? 160 : '100%' }} />
          <LookupSelect resource="subject-groups" placeholder={t('subject')} value={filters.subjectGroupId} onChange={(v) => set('subjectGroupId', v)} style={{ width: screens.md ? 220 : '100%' }} />
          <LookupSelect resource="customers" placeholder={t('customer')} value={filters.customerId} onChange={(v) => set('customerId', v)} style={{ width: screens.md ? 220 : '100%' }} />
          {!approvalMode && (
            <Select aria-label={t('status')} allowClear placeholder={t('status')} value={filters.status} onChange={(v) => set('status', v)} style={{ width: screens.md ? 150 : '100%' }}
              options={ENUMS.MenuStatus.map((s) => ({ value: s, label: enumLabel('MenuStatus', s) }))} />
          )}
        </div>
        {isMobile ? (
          <>
            {isFetching && !data ? <div className="bm-pad"><Skeleton active /></div> : (data?.items ?? []).length === 0 ? <div className="bm-noRows">{t('common:noData')}</div> : (
              <div className="bm-mlist">
                {data!.items.map((r) => {
                  const sel = selected.includes(r.id);
                  return (
                    <div key={r.id} className={sel ? 'bm-mcard is-sel' : 'bm-mcard'} onClick={() => navigate(`/menus/${r.id}`)}>
                      <div className="bm-mcard-row">
                        <span className="bm-mcard-cb" onClick={(e) => e.stopPropagation()}><Checkbox checked={sel} onChange={() => toggle(r.id)} aria-label={r.code} /></span>
                        <div className="bm-mcard-main">
                          <div className="bm-mcard-top">
                            <div><div className="bm-mcard-title">{fmt.date(r.menuDate)} · {shiftName(r)}</div><div className="bm-mcard-sub">{r.code}</div></div>
                            {statusCell(r)}
                          </div>
                          <div className="bm-mcard-dishes">{r.dishes.map(dishName).join(' · ')}</div>
                          <div className="bm-mgrid4">
                            <div><div className="bm-mcard-l">{t('servings')}</div><div className="bm-mcard-v">{fmt.number(r.servings)}</div></div>
                            <div><div className="bm-mcard-l">{t('energy')}</div><div className="bm-mcard-v">{fmt.number(r.energyKcal, 0)}</div></div>
                            <div><div className="bm-mcard-l">{t('cost')}</div><div className="bm-mcard-v">{canCost ? fmt.money(r.costPerServing) : '—'}</div></div>
                            <div><div className="bm-mcard-l">{t('score')}</div><div className="bm-mcard-v">{fmt.number(r.nutritionScore, 0)}</div></div>
                          </div>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
            <div className="bm-card-foot">
              <span>{t('common:totalItems', { count: data?.totalItems ?? 0 })}</span>
              <Pagination simple current={page} pageSize={50} total={data?.totalItems ?? 0} onChange={setPage} />
            </div>
          </>
        ) : (
          <Table<Row>
            rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }}
            rowSelection={{ selectedRowKeys: selected, onChange: (k) => setSelected(k as string[]), getCheckboxProps: (r) => ({ 'aria-label': r.code }) as CheckboxProps }}
            onRow={(r) => ({ onClick: () => navigate(`/menus/${r.id}`), style: { cursor: 'pointer' } })}
            pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
            columns={[
              { title: t('date'), dataIndex: 'menuDate', render: (v) => <b style={{ fontWeight: 500 }}>{fmt.date(v)}</b>, width: 110 },
              { title: t('shift'), key: 's', render: (_, r) => shiftName(r), width: 130 },
              { title: t('code'), dataIndex: 'code', width: 160, render: (v) => <span style={{ color: 'var(--ink-2)' }}>{v}</span> },
              { title: t('dishes'), key: 'd', render: (_, r) => <div className="bm-dchips">{r.dishes.map((d) => <span key={d.dishId} className="bm-dchip">{dishName(d)}</span>)}</div> },
              { title: t('subject'), dataIndex: 'subject', width: 180, responsive: ['xxl'] },
              { title: t('customer'), dataIndex: 'customer', width: 200, responsive: ['xxl'] },
              { title: t('servings'), dataIndex: 'servings', align: 'right', width: 90, render: (v) => fmt.number(v) },
              { title: t('energy'), dataIndex: 'energyKcal', align: 'right', width: 100, render: (v) => fmt.number(v, 0) },
              ...(canCost ? [{ title: t('cost'), dataIndex: 'costPerServing', align: 'right' as const, width: 120, render: (v: number) => fmt.money(v) }] : []),
              { title: t('score'), dataIndex: 'nutritionScore', align: 'right', width: 80, render: (v) => <b>{fmt.number(v, 0)}</b> },
              { title: t('status'), key: 'st', width: 160, render: (_, r) => statusCell(r) },
              { title: t('mode'), dataIndex: 'creationMode', width: 110, render: (v) => enumLabel('MenuCreationMode', v), responsive: ['xxl'] },
            ]}
          />
        )}
      </div>
    </Page>
  );
}

export function MenuApprovalPage() {
  return <MenuListPage approvalMode />;
}
