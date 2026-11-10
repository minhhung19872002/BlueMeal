import { useState } from 'react';
import { Button, Grid, Input, Pagination, Select, Skeleton } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { LookupSelect, useEnumLabel } from '@/shared/components/catalog/inputs';
import { Page } from '@/shared/components/PageChrome';
import { ENUMS } from '@/shared/utils/enums';
import { fmt } from '@/shared/utils/format';

interface RecipeRow { id: string; dishId: string; dishCode: string; dishNameVi: string; dishNameEn?: string; dishType: string; versionNo: number; isCurrent: boolean; status: string; baseServings: number; itemCount: number; updatedAt?: string; createdAt: string; energyKcal?: number | null; costPerServing?: number | null }

export const statusColor: Record<string, string> = { Draft: 'default', Active: 'green', Archived: 'purple' };
/** Design pill tone per recipe status (BMMenus "recipes": Đang dùng green, Lưu trữ orange, Nháp neutral). */
export const statusTone: Record<string, string> = { Draft: 't-neutral', Active: 't-green', Archived: 't-orange' };

/** Recipe list (all versions) with filters (SPEC §7.3.7) — row list from Claude Design BMMenus "recipes". */
export default function RecipesPage() {
  const { t, i18n } = useTranslation('recipe');
  const navigate = useNavigate();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const screens = Grid.useBreakpoint();
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
  const w = (px: number) => (screens.md ? px : '100%');
  return (
    <Page title={t('title')} sub={t('listSub')} actions={has('recipe.create') && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/recipes/new')}>{t('new')}</Button>}>
      <div className="bm-card">
        <div className="bm-toolbar">
          <Input.Search allowClear placeholder={t('common:searchPlaceholder')} onSearch={(v) => { setQ(v); setPage(1); }} style={{ width: w(280) }} />
          <LookupSelect resource="dishes" placeholder={t('dish')} value={dishId} onChange={(v) => { setDishId(v ?? undefined); setPage(1); }} style={{ width: w(260) }} />
          <Select allowClear placeholder={t('status')} value={status} onChange={(v) => { setStatus(v); setPage(1); }} style={{ width: w(170) }}
            options={ENUMS.RecipeStatus.map((s) => ({ value: s, label: enumLabel('RecipeStatus', s) }))} />
          <Select value={currentOnly ? 'current' : 'all'} onChange={(v) => { setCurrentOnly(v === 'current'); setPage(1); }} style={{ width: w(200) }}
            options={[{ value: 'current', label: t('current') }, { value: 'all', label: t('versions') }]} />
        </div>
        {isFetching && !data ? <div className="bm-pad"><Skeleton active /></div> : !data?.items.length ? <div className="bm-noRows">{t('common:noData')}</div> : (
          <div className="bm-rlist">
            {data.items.map((r) => (
              <div key={r.id} className="bm-rrow" role="link" tabIndex={0} onClick={() => navigate(`/recipes/${r.id}`)} onKeyDown={(e) => e.key === 'Enter' && navigate(`/recipes/${r.id}`)}>
                <div className="bm-rrow-main">
                  <div className="bm-rrow-name">{i18n.language === 'en' && r.dishNameEn ? r.dishNameEn : r.dishNameVi}</div>
                  <div className="bm-rrow-sub">{t('rowMeta', { code: r.dishCode, v: r.versionNo, n: r.itemCount })} · {enumLabel('DishType', r.dishType)}</div>
                </div>
                <div className="bm-rrow-nums">
                  {r.energyKcal != null && <span><b>{fmt.number(r.energyKcal, 0)}</b> kcal</span>}
                  {r.costPerServing != null && <span><b>{fmt.money(r.costPerServing)}</b>{t('costPerServingSuffix')}</span>}
                </div>
                <div className="bm-rrow-info">{t('baseInfo', { n: fmt.number(r.baseServings, 0) })} · {fmt.dateTime(r.updatedAt ?? r.createdAt)}</div>
                <div className="bm-rrow-tags">
                  {r.isCurrent && <span className="bm-tag t-info">{t('current')}</span>}
                  <span className={`bm-tag ${statusTone[r.status] ?? 't-neutral'}`}>{enumLabel('RecipeStatus', r.status)}</span>
                </div>
              </div>
            ))}
          </div>
        )}
        <div className="bm-card-foot">
          <span>{t('common:totalItems', { count: data?.totalItems ?? 0 })}</span>
          <Pagination simple current={page} pageSize={20} total={data?.totalItems ?? 0} onChange={setPage} />
        </div>
      </div>
    </Page>
  );
}
