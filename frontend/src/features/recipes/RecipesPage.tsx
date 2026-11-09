import { useState } from 'react';
import { Button, Card, Input, Select, Space, Table, Tag, Typography } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { ENUMS } from '@/shared/utils/enums';
import { fmt } from '@/shared/utils/format';

interface RecipeRow { id: string; dishId: string; dishCode: string; dishNameVi: string; dishNameEn?: string; dishType: string; versionNo: number; isCurrent: boolean; status: string; baseServings: number; itemCount: number; updatedAt?: string; createdAt: string }

export const statusColor: Record<string, string> = { Draft: 'default', Active: 'green', Archived: 'purple' };

/** Recipe list (all versions) with filters (SPEC §7.3.7). */
export default function RecipesPage() {
  const { t, i18n } = useTranslation('recipe');
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const [dishId, setDishId] = useState<string>();
  const [status, setStatus] = useState<string>();
  const [currentOnly, setCurrentOnly] = useState(true);
  const { data, isFetching } = useQuery({
    queryKey: ['recipes', page, q, dishId, status, currentOnly],
    queryFn: async () => (await http.get<PagedResult<RecipeRow>>('/recipes', { params: { page, pageSize: 20, q: q || undefined, dishId, status, currentOnly } })).data,
    placeholderData: keepPreviousData,
  });
  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t('title')}</Typography.Title>
        {has('recipe.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/recipes/new')}>{t('new')}</Button>}
      </div>
      <div className="bm-toolbar">
        <Input.Search allowClear placeholder={t('common:searchPlaceholder')} onSearch={(v) => { setQ(v); setPage(1); }} style={{ maxWidth: 280 }} />
        <LookupSelect resource="dishes" placeholder={t('dish')} value={dishId} onChange={(v) => { setDishId(v ?? undefined); setPage(1); }} style={{ width: 260 }} />
        <Select allowClear placeholder={t('status')} value={status} onChange={(v) => { setStatus(v); setPage(1); }} style={{ width: 170 }}
          options={ENUMS.RecipeStatus.map((s) => ({ value: s, label: enumLabel('RecipeStatus', s) }))} />
        <Select value={currentOnly ? 'current' : 'all'} onChange={(v) => { setCurrentOnly(v === 'current'); setPage(1); }} style={{ width: 200 }}
          options={[{ value: 'current', label: t('current') }, { value: 'all', label: t('versions') }]} />
      </div>
      <Table<RecipeRow>
        rowKey="id" loading={isFetching} dataSource={data?.items} scroll={{ x: 'max-content' }}
        onRow={(r) => ({ onClick: () => navigate(`/recipes/${r.id}`), style: { cursor: 'pointer' } })}
        pagination={{ current: page, pageSize: 20, total: data?.totalItems, onChange: setPage, showSizeChanger: false, showTotal: (n) => t('common:totalItems', { count: n }) }}
        columns={[
          { title: t('dish'), key: 'dish', render: (_, r) => <Space direction="vertical" size={0}><b>{i18n.language === 'en' && r.dishNameEn ? r.dishNameEn : r.dishNameVi}</b><Typography.Text type="secondary">{r.dishCode}</Typography.Text></Space> },
          { title: t('catalog:f.dishType'), dataIndex: 'dishType', render: (v) => enumLabel('DishType', v) },
          { title: t('version'), dataIndex: 'versionNo', render: (v, r) => <Space>v{v}{r.isCurrent && <Tag color="green">{t('current')}</Tag>}</Space> },
          { title: t('status'), dataIndex: 'status', render: (v) => <Tag color={statusColor[v]}>{enumLabel('RecipeStatus', v)}</Tag> },
          { title: t('baseServings'), dataIndex: 'baseServings', align: 'right', render: (v) => fmt.number(v, 0) },
          { title: t('items'), dataIndex: 'itemCount', align: 'right' },
          { title: t('updated'), key: 'u', render: (_, r) => fmt.dateTime(r.updatedAt ?? r.createdAt) },
        ]}
      />
    </Card>
  );
}
