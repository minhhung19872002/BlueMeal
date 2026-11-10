import type { ReactNode } from 'react';
import { Pagination, Skeleton, Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import { useEnumLabel } from '@/shared/components/catalog/inputs';
import { fmt } from '@/shared/utils/format';

export const DOC_STATUS_COLOR: Record<string, string> = {
  Draft: 'default', Posted: 'green', Cancelled: 'default', Counting: 'processing', Approved: 'green',
  Submitted: 'processing', Ordered: 'cyan', Received: 'green',
};

export function StatusTag({ enumName, status }: { enumName: string; status: string }) {
  const enumLabel = useEnumLabel();
  return <Tag color={DOC_STATUS_COLOR[status]}>{enumLabel(enumName, status)}</Tag>;
}

export const QC_COLOR: Record<string, string> = { Pass: 'green', ConditionalPass: 'gold', Fail: 'red' };

export function QcTag({ value }: { value?: string | null }) {
  const enumLabel = useEnumLabel();
  if (!value) return null;
  return <Tag color={QC_COLOR[value]}>{enumLabel('QualityResult', value)}</Tag>;
}

/** Ingredient label with the English name when the UI is in English. */
export const ingName = (r: { ingredientCode?: string; ingredientName?: string; ingredientNameEn?: string | null }, lang: string) =>
  `${r.ingredientCode ?? ''} — ${lang === 'en' && r.ingredientNameEn ? r.ingredientNameEn : r.ingredientName ?? ''}`;

/** Quantity with up to 3 decimals and no trailing zeros (40 → "40", 36.5 → "36,5"). */
export function qtyText(v: number | null | undefined): string {
  if (v == null) return '';
  const s = String(Math.round(v * 1000) / 1000);
  return fmt.number(v, s.includes('.') ? s.length - s.indexOf('.') - 1 : 0);
}

/** Fresh idempotency key for one user action (create/post) so retries do not duplicate documents. */
export const idemKey = () => (crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`);

export interface DocCard { key: string; onClick?: () => void; title: ReactNode; sub?: ReactNode; tag?: ReactNode; meta: { label: ReactNode; value: ReactNode }[]; actions?: ReactNode }

/** Mobile list (< 992 px) used instead of the table on document pages — design BMOps list cards. */
export function DocCards<T>({ loading, items, total, page, pageSize = 50, onPage, render }: {
  loading?: boolean; items?: T[]; total: number; page: number; pageSize?: number; onPage: (p: number) => void; render: (item: T) => DocCard;
}) {
  const { t } = useTranslation();
  return (
    <>
      {loading ? <div className="bm-pad"><Skeleton active /></div> : !items || items.length === 0 ? <div className="bm-noRows">{t('common:noData')}</div> : (
        <div className="bm-mlist">
          {items.map((it) => {
            const c = render(it);
            return (
              <div key={c.key} className="bm-mcard" onClick={c.onClick} style={c.onClick ? undefined : { cursor: 'default' }}>
                <div className="bm-mcard-top">
                  <div style={{ minWidth: 0 }}><div className="bm-mcard-title">{c.title}</div>{c.sub && <div className="bm-mcard-sub">{c.sub}</div>}</div>
                  {c.tag}
                </div>
                <div className="bm-mcard-meta">
                  {c.meta.map((m, i) => <div key={i} style={{ minWidth: 0 }}><div className="bm-mcard-l">{m.label}</div><div className="bm-mcard-v">{m.value}</div></div>)}
                </div>
                {c.actions && <div className="bm-mcard-acts" onClick={(e) => e.stopPropagation()}>{c.actions}</div>}
              </div>
            );
          })}
        </div>
      )}
      <div className="bm-card-foot">
        <span>{t('common:totalItems', { count: total })}</span>
        <Pagination simple current={page} pageSize={pageSize} total={total} onChange={onPage} />
      </div>
    </>
  );
}
