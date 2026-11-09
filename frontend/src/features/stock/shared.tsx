import { Tag } from 'antd';
import { useEnumLabel } from '@/shared/components/catalog/inputs';

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

/** Fresh idempotency key for one user action (create/post) so retries do not duplicate documents. */
export const idemKey = () => (crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`);
