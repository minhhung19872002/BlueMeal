import { useState } from 'react';
import { App, Button, Card, DatePicker, Grid, Input, Select, Space, Table, Tag, Tooltip, Typography } from 'antd';
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
  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{approvalMode ? t('approval') : t('list')}</Typography.Title>
        <Space wrap>
          {approvalMode && selected.length > 0 && <Button type="primary" icon={<CheckOutlined />} onClick={() => void bulk('approve')}>{t('bulkApprove')} ({selected.length})</Button>}
          {!approvalMode && selected.length > 0 && has('menu.publish') && <Button onClick={() => void bulk('publish')}>{t('bulkPublish')} ({selected.length})</Button>}
          {!approvalMode && has('menu.create') && <Button icon={<ThunderboltOutlined />} onClick={() => navigate('/menus/auto')}>{t('auto')}</Button>}
          {!approvalMode && has('menu.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/menus/new')}>{t('new')}</Button>}
        </Space>
      </div>
      <div className="bm-toolbar">
        {!approvalMode && <DatePicker.RangePicker value={range} onChange={(v) => { setRange(v as never); setPage(1); }} format={DATE_FORMAT} style={{ width: screens.md ? 260 : '100%' }} />}
        <Input.Search allowClear placeholder={t('code')} onSearch={(v) => { setQ(v); setPage(1); }} style={{ width: screens.md ? 180 : '100%' }} />
        <LookupSelect resource="meal-shifts" placeholder={t('shift')} value={filters.mealShiftId} onChange={(v) => set('mealShiftId', v)} style={{ width: screens.md ? 160 : '100%' }} />
        <LookupSelect resource="subject-groups" placeholder={t('subject')} value={filters.subjectGroupId} onChange={(v) => set('subjectGroupId', v)} style={{ width: screens.md ? 220 : '100%' }} />
        <LookupSelect resource="customers" placeholder={t('customer')} value={filters.customerId} onChange={(v) => set('customerId', v)} style={{ width: screens.md ? 220 : '100%' }} />
        {!approvalMode && (
          <Select allowClear placeholder={t('status')} value={filters.status} onChange={(v) => set('status', v)} style={{ width: screens.md ? 150 : '100%' }}
            options={ENUMS.MenuStatus.map((s) => ({ value: s, label: enumLabel('MenuStatus', s) }))} />
        )}
      </div>
      <Table<Row>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }} size={screens.md ? 'middle' : 'small'}
        rowSelection={{ selectedRowKeys: selected, onChange: (k) => setSelected(k as string[]) }}
        onRow={(r) => ({ onClick: () => navigate(`/menus/${r.id}`), style: { cursor: 'pointer' } })}
        pagination={{ current: page, pageSize: 50, total: data?.totalItems, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('date'), dataIndex: 'menuDate', render: (v) => fmt.date(v), width: 110, fixed: screens.md ? 'left' : undefined },
          { title: t('shift'), key: 's', render: (_, r) => (i18n.language === 'en' && r.shiftEn ? r.shiftEn : r.shift), width: 130 },
          { title: t('code'), dataIndex: 'code', width: 160, responsive: ['md'] },
          { title: t('dishes'), key: 'd', render: (_, r) => <Space size={[4, 4]} wrap>{r.dishes.map((d) => <Tag key={d.dishId}>{i18n.language === 'en' && d.nameEn ? d.nameEn : d.nameVi}</Tag>)}</Space> },
          { title: t('subject'), dataIndex: 'subject', width: 180, responsive: ['lg'] },
          { title: t('customer'), dataIndex: 'customer', width: 200, responsive: ['lg'] },
          { title: t('servings'), dataIndex: 'servings', align: 'right', width: 90 },
          { title: t('energy'), dataIndex: 'energyKcal', align: 'right', width: 110, render: (v) => fmt.number(v, 0) },
          ...(canCost ? [{ title: t('cost'), dataIndex: 'costPerServing', align: 'right' as const, width: 120, render: (v: number) => fmt.money(v) }] : []),
          { title: t('score'), dataIndex: 'nutritionScore', align: 'right', width: 80, render: (v) => fmt.number(v, 0) },
          { title: t('status'), key: 'st', width: 160, render: (_, r) => (
            <Space size={4}>
              <MenuStatusTag status={r.status} />
              {r.status === 'Submitted' && r.approvalLevel > 0 && <Tag>L{r.approvalLevel}</Tag>}
              {r.criticalWarnings > 0 && <Tooltip title={t('critical')}><WarningFilled style={{ color: '#d03b3b' }} /></Tooltip>}
            </Space>
          ) },
          { title: t('mode'), dataIndex: 'creationMode', width: 110, render: (v) => enumLabel('MenuCreationMode', v), responsive: ['xl'] },
        ]}
      />
    </Card>
  );
}

export function MenuApprovalPage() {
  return <MenuListPage approvalMode />;
}
